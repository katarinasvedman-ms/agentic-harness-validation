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
    public void KillSwitchValidatesReasonAndResetRestoresDemoState()
    {
        var state = CreateState();

        Assert.Throws<ArgumentException>(() => state.SetKillSwitch(true, " "));
        Assert.True(state.SetKillSwitch(true, "Pause local execution.").KillSwitchActive);

        state.Reset();

        Assert.False(state.GetControls().KillSwitchActive);
        Assert.NotNull(state.GetPending(IncidentSimulator.DemoIncidentId));
    }

    private static ConsoleState CreateState(InMemoryApprovalStore? approvals = null)
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
            new InMemoryAuditChain(),
            new InMemoryKillSwitch(),
            ExecutionBudgetLimits.LocalDefault,
            time);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
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
