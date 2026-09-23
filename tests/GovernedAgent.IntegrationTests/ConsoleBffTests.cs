using GovernedAgent.Console.Bff;
using GovernedAgent.Core.Contracts;
using GovernedAgent.Governance;
using GovernedAgent.Host.Verification;
using GovernedAgent.Simulator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace GovernedAgent.IntegrationTests;

public sealed class ConsoleBffTests
{
    [Fact]
    public void ReadModelIncludesIncidentEvidenceAndVerifiedPlan()
    {
        var state = CreateState();

        var incident = state.GetIncident(IncidentSimulator.DemoIncidentId);
        var evidence = state.GetEvidence(IncidentSimulator.DemoIncidentId);
        var verification = state.GetVerification(IncidentSimulator.DemoIncidentId);

        Assert.Equal(IncidentStatus.Open, incident.Incident.Status);
        Assert.Contains(evidence, item => item.ContainsUntrustedContent);
        Assert.Equal(VerificationResult.Verified, verification.Result);
        var step = Assert.Single(verification.Plan.Steps);
        Assert.Equal("restart_service", step.Tool);
        Assert.Equal(["instanceId", "serviceId"], step.Arguments.Keys.Order().ToArray());
        var compensation = Assert.IsType<CompensationAction>(step.Compensation);
        Assert.Equal("restore_service_state", compensation.Tool);
        Assert.Equal(
            ["instanceId", "previousHealth", "serviceId", "sourceVersion"],
            compensation.Arguments.Keys.Order().ToArray());
    }

    [Fact]
    public async Task RepresentedSnapshotIsAcceptedByTheWorkflowVerifier()
    {
        var state = CreateState();
        var snapshot = state.GetVerification(IncidentSimulator.DemoIncidentId);
        var repositoryRoot = FindRepositoryRoot();
        var verifier = new NodePlanVerifier(
            "node",
            Path.Combine(repositoryRoot, "src", "plan-verifier", "dist", "cli.js"),
            TimeSpan.FromSeconds(5));
        var request = new PlanVerificationRequest(
            snapshot.Plan,
            new DateTimeOffset(2026, 8, 12, 12, 47, 32, TimeSpan.Zero),
            8,
            ["service.restart", "service.restore"],
            new Dictionary<string, VerifierToolMetadata>(StringComparer.Ordinal)
            {
                ["restart_service"] = new(
                    "service.restart",
                    EffectKind.Write,
                    ApprovalClass.IncidentCommander,
                    "serviceId"),
                ["restore_service_state"] = new(
                    "service.restore",
                    EffectKind.Write,
                    ApprovalClass.IncidentCommander,
                    "serviceId")
            },
            snapshot.PlanDigest,
            snapshot.SpecificationVersion,
            snapshot.VerifierVersion);

        var decision = await verifier.VerifyAsync(request, CancellationToken.None);

        Assert.Equal(VerificationResult.Verified, decision.Status);
        Assert.Equal(snapshot.PlanDigest, decision.PlanDigest);
    }

    [Fact]
    public void ApprovalRequiresExplicitIncidentCommanderIdentity()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[DemoIdentity.UserHeader] = "local-user";
        context.Request.Headers[DemoIdentity.RolesHeader] = "IncidentCommander";

        var denied = DemoIdentity.Require(
            context.Request,
            DemoIdentity.IncidentCommanderRole);

        Assert.IsAssignableFrom<IStatusCodeHttpResult>(denied);
        Assert.Equal(
            StatusCodes.Status403Forbidden,
            ((IStatusCodeHttpResult)denied!).StatusCode);
    }

    [Fact]
    public void ExactApprovalIsOneTimeAndCreatesValidAuditRecord()
    {
        var approvals = new InMemoryApprovalStore();
        var state = CreateState(approvals);
        var pending = Assert.IsType<PendingApprovalView>(
            state.GetPending(IncidentSimulator.DemoIncidentId));
        var identity = new DemoIdentity(
            "commander@example.test",
            new HashSet<string>(
                [DemoIdentity.IncidentCommanderRole],
                StringComparer.Ordinal));

        var result = state.Decide(
            pending.ApprovalRequestId,
            ApprovalDecision.Approved,
            identity,
            "Mitigate the verified degraded instance.");

        Assert.Equal(ApprovalDecision.Approved, result.Decision);
        Assert.Null(state.GetPending(IncidentSimulator.DemoIncidentId));
        var audit = state.GetAudit();
        Assert.True(audit.IntegrityValid);
        Assert.Equal(ExecutionState.Approved, Assert.Single(audit.Records).ExecutionState);
        var exactRequest = new ApprovalConsumptionRequest(
            pending.PlanId,
            pending.StepId,
            pending.ActionDigest,
            pending.ResourceId,
            pending.Environment,
            DemoIdentity.IncidentCommanderRole,
            "1.0",
            result.DecidedAt);
        var mutatedRequest = exactRequest with
        {
            ActionDigest = $"{pending.ActionDigest[..^1]}" +
                (pending.ActionDigest[^1] == '0' ? "1" : "0")
        };
        Assert.False(approvals.TryConsume(
            Assert.IsType<string>(result.ApprovalNonce),
            mutatedRequest,
            out _));
        Assert.True(approvals.TryConsume(
            result.ApprovalNonce!,
            exactRequest,
            out var artifact));
        Assert.Equal(pending.ActionDigest, artifact?.ActionDigest);
        Assert.Throws<KeyNotFoundException>(() => state.Decide(
            pending.ApprovalRequestId,
            ApprovalDecision.Approved,
            identity,
            "Replay."));
    }

    [Fact]
    public void ContainmentRecoveryRequiresOrderedEvidenceAndClosesAudit()
    {
        var state = CreateState();
        var operatorIdentity = new DemoIdentity(
            "operator@example.test",
            new HashSet<string>(
                [DemoIdentity.GovernanceOperatorRole],
                StringComparer.Ordinal));
        var commanderIdentity = new DemoIdentity(
            "commander@example.test",
            new HashSet<string>(
                [DemoIdentity.IncidentCommanderRole],
                StringComparer.Ordinal));

        Assert.Throws<ArgumentException>(() =>
            state.SetContainment(true, " ", operatorIdentity));
        var contained = state.SetContainment(
            true,
            "Pause local execution.",
            operatorIdentity);
        Assert.True(contained.KillSwitchActive);
        Assert.Equal(ContainmentMode.Contained, contained.Mode);
        Assert.Throws<InvalidOperationException>(() =>
            state.SetContainment(false, "Bypass recovery.", commanderIdentity));
        Assert.Throws<InvalidOperationException>(() =>
            state.RestoreOperational(
                new RecoveryMutation("Root cause.", "Restore."),
                commanderIdentity));

        var readOnly = state.BeginReadOnlyRecovery(
            new ReattestationMutation(
                new string('a', 64),
                "agent-image:sha256:known-good",
                "Verified identity, policy, and deployment."),
            operatorIdentity);
        Assert.Equal(ContainmentMode.ReadOnlyRecovery, readOnly.Mode);
        Assert.NotNull(readOnly.Reattestation);

        var restored = state.RestoreOperational(
            new RecoveryMutation(
                "Removed the untrusted integration and redeployed the known-good image.",
                "Incident commander approved staged restoration."),
            commanderIdentity);
        Assert.Equal(ContainmentMode.Operational, restored.Mode);
        Assert.False(restored.KillSwitchActive);
        Assert.NotNull(restored.SignOff);
        Assert.True(state.GetAudit().IntegrityValid);
        Assert.Equal(3, state.GetAudit().Records.Count);

        state.Reset();

        Assert.Equal(ContainmentMode.Operational, state.GetControls().Mode);
        Assert.NotNull(state.GetControls().Reattestation);
        Assert.NotNull(state.GetControls().SignOff);
        Assert.NotNull(state.GetPending(IncidentSimulator.DemoIncidentId));
    }

    [Fact]
    public void ResetCannotClearActiveContainment()
    {
        var state = CreateState();
        var identity = new DemoIdentity(
            "operator@example.test",
            new HashSet<string>(
                [DemoIdentity.GovernanceOperatorRole],
                StringComparer.Ordinal));
        state.SetContainment(true, "Pause local execution.", identity);

        state.Reset();

        Assert.Equal(ContainmentMode.Contained, state.GetControls().Mode);
    }

    [Fact]
    public void FailedRecoveryAuditLeavesWriteAuthorityDisabled()
    {
        var control = new InMemoryContainmentControl();
        var state = CreateState(
            audit: new FailingAuditChain(failOnAppend: 3),
            containment: control);
        var operatorIdentity = new DemoIdentity(
            "operator@example.test",
            new HashSet<string>(
                [DemoIdentity.GovernanceOperatorRole],
                StringComparer.Ordinal));
        var commanderIdentity = new DemoIdentity(
            "commander@example.test",
            new HashSet<string>(
                [DemoIdentity.IncidentCommanderRole],
                StringComparer.Ordinal));
        state.SetContainment(true, "Pause local execution.", operatorIdentity);
        state.BeginReadOnlyRecovery(
            new ReattestationMutation(
                new string('a', 64),
                "agent-image:sha256:known-good",
                "Verified identity, policy, and deployment."),
            operatorIdentity);

        Assert.Throws<InvalidOperationException>(() =>
            state.RestoreOperational(
                new RecoveryMutation(
                    "Removed the untrusted integration.",
                    "Approve restoration."),
                commanderIdentity));
        Assert.Equal(ContainmentMode.ReadOnlyRecovery, control.Mode);
        Assert.Null(control.SignOff);
    }

    private static ConsoleState CreateState(
        InMemoryApprovalStore? approvals = null,
        IAuditChain? audit = null,
        InMemoryContainmentControl? containment = null)
    {
        var time = new FixedTimeProvider(
            new DateTimeOffset(2026, 8, 12, 12, 47, 32, TimeSpan.Zero));
        var registry = new ToolRegistry();
        return new ConsoleState(
            new IncidentSimulator(time),
            new DemoWorkflowSnapshotProvider(
                time,
                registry,
                new ActionCanonicalizer(registry)),
            approvals ?? new InMemoryApprovalStore(),
            audit ?? new InMemoryAuditChain(),
            containment ?? new InMemoryContainmentControl(),
            ExecutionBudgetLimits.LocalDefault,
            time);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FailingAuditChain(int failOnAppend) : IAuditChain
    {
        private readonly InMemoryAuditChain _inner = new();
        private int _appendCount;

        public AuditRecord Append(AuditRecord record)
        {
            _appendCount++;
            if (_appendCount == failOnAppend)
            {
                throw new InvalidOperationException("Simulated audit persistence failure.");
            }

            return _inner.Append(record);
        }

        public IReadOnlyList<AuditRecord> ReadAll() => _inner.ReadAll();

        public bool VerifyIntegrity() => _inner.VerifyIntegrity();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "GovernedAgentDemo.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
