using System.Text.Json;
using GovernedAgent.Core.Contracts;
using GovernedAgent.Governance;
using GovernedAgent.Host.Verification;
using GovernedAgent.Host.Workflow;
using GovernedAgent.Simulator;

namespace GovernedAgent.IntegrationTests;

public sealed class AgentWorkflowTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-12T10:00:00Z");

    [Fact]
    public async Task ProductionWriteSuspendsWithoutSideEffectsAndResumesExactly()
    {
        var harness = CreateHarness();

        var suspended = await harness.Workflow.ExecuteAsync(
            harness.Request,
            CancellationToken.None);

        Assert.Equal(AgentWorkflowStatus.ApprovalRequired, suspended.Status);
        Assert.NotNull(suspended.Suspension);
        Assert.Equal(
            ServiceHealth.Degraded,
            harness.Simulator.GetServiceHealth(IncidentSimulator.DemoServiceId).Health);
        Assert.Equal(
            suspended.Suspension!.PlanDigest,
            suspended.Envelope!.Verification.PlanDigest);
        Assert.Equal(
            suspended.Suspension.ActionDigest,
            suspended.Envelope.Action.ActionDigest);

        harness.Approvals.Add(CreateApproval(suspended.Suspension));
        var completed = await harness.Workflow.ResumeAsync(
            suspended.Suspension,
            "approval-nonce-1",
            CancellationToken.None);

        Assert.Equal(AgentWorkflowStatus.Completed, completed.Status);
        Assert.Equal("simulator_completion_satisfied", completed.ReasonCode);
        Assert.True(completed.SimulatorState!.IsComplete);
        Assert.Equal(ServiceHealth.Healthy, completed.SimulatorState.ServiceHealth);
        Assert.Equal(
            ServiceHealth.Healthy,
            harness.Simulator.GetServiceHealth(IncidentSimulator.DemoServiceId).Health);
    }

    [Fact]
    public async Task RejectedVerificationFailsClosedBeforeGateway()
    {
        var harness = CreateHarness(new StubVerifier(VerificationResult.Rejected));

        var result = await harness.Workflow.ExecuteAsync(
            harness.Request,
            CancellationToken.None);

        Assert.Equal(AgentWorkflowStatus.Failed, result.Status);
        Assert.Equal("unsafe-plan", result.ReasonCode);
        Assert.Equal(ErrorCategory.VerificationRejected, result.ErrorCategory);
        Assert.Equal(
            ServiceHealth.Degraded,
            harness.Simulator.GetServiceHealth(IncidentSimulator.DemoServiceId).Health);
        Assert.Empty(harness.Audit.ReadAll());
    }

    [Fact]
    public async Task UnavailableVerificationFailsClosedBeforeGateway()
    {
        var harness = CreateHarness(new StubVerifier(throwUnavailable: true));

        var result = await harness.Workflow.ExecuteAsync(
            harness.Request,
            CancellationToken.None);

        Assert.Equal(AgentWorkflowStatus.Failed, result.Status);
        Assert.Equal("verifier_unavailable", result.ReasonCode);
        Assert.Equal(ErrorCategory.VerificationUnavailable, result.ErrorCategory);
        Assert.Equal(
            ServiceHealth.Degraded,
            harness.Simulator.GetServiceHealth(IncidentSimulator.DemoServiceId).Health);
        Assert.Empty(harness.Audit.ReadAll());
    }

    [Fact]
    public async Task ForgedVerificationAttestationFailsClosed()
    {
        var harness = CreateHarness(new StubVerifier(useWrongDigest: true));

        var result = await harness.Workflow.ExecuteAsync(
            harness.Request,
            CancellationToken.None);

        Assert.Equal(AgentWorkflowStatus.Failed, result.Status);
        Assert.Equal("verification_attestation_mismatch", result.ReasonCode);
        Assert.Empty(harness.Audit.ReadAll());
    }

    [Fact]
    public async Task WrongApprovalDoesNotResumeOrChangeSimulator()
    {
        var harness = CreateHarness();
        var suspended = await harness.Workflow.ExecuteAsync(
            harness.Request,
            CancellationToken.None);
        var wrongApproval = CreateApproval(suspended.Suspension!) with
        {
            ActionDigest = new string('f', 64)
        };
        harness.Approvals.Add(wrongApproval);

        var result = await harness.Workflow.ResumeAsync(
            suspended.Suspension!,
            wrongApproval.Nonce,
            CancellationToken.None);

        Assert.Equal(AgentWorkflowStatus.Failed, result.Status);
        Assert.Equal("approval_invalid", result.ReasonCode);
        Assert.Equal(
            ServiceHealth.Degraded,
            harness.Simulator.GetServiceHealth(IncidentSimulator.DemoServiceId).Health);
    }

    [Fact]
    public async Task MutatedSuspensionCannotUseApproval()
    {
        var harness = CreateHarness();
        var suspended = await harness.Workflow.ExecuteAsync(
            harness.Request,
            CancellationToken.None);
        var approval = CreateApproval(suspended.Suspension!);
        harness.Approvals.Add(approval);
        var mutatedStep = harness.Request.Plan.Steps[0] with
        {
            Arguments = new Dictionary<string, JsonElement>
            {
                ["serviceId"] = JsonSerializer.SerializeToElement(
                    IncidentSimulator.DemoServiceId),
                ["instanceId"] = JsonSerializer.SerializeToElement("payments-api-02")
            }
        };
        var mutatedRequest = harness.Request with
        {
            Plan = harness.Request.Plan with { Steps = [mutatedStep] }
        };
        var mutatedSuspension = suspended.Suspension! with { Request = mutatedRequest };

        var result = await harness.Workflow.ResumeAsync(
            mutatedSuspension,
            approval.Nonce,
            CancellationToken.None);

        Assert.Equal(AgentWorkflowStatus.Failed, result.Status);
        Assert.Equal("suspension_binding_mismatch", result.ReasonCode);
        Assert.Equal(
            ServiceHealth.Degraded,
            harness.Simulator.GetServiceHealth(IncidentSimulator.DemoServiceId).Health);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MutatedEnvelopeIdentityOrVerificationCannotResume(
        bool mutateVerification)
    {
        var harness = CreateHarness();
        var suspended = await harness.Workflow.ExecuteAsync(
            harness.Request,
            CancellationToken.None);
        var approval = CreateApproval(suspended.Suspension!);
        harness.Approvals.Add(approval);
        var envelope = mutateVerification
            ? suspended.Envelope! with
            {
                Verification = suspended.Envelope!.Verification with
                {
                    Result = VerificationResult.Rejected,
                    VerifierVersion = "forged"
                }
            }
            : suspended.Envelope! with
            {
                Agent = suspended.Envelope!.Agent with { Identity = "forged-agent" }
            };
        var mutated = suspended.Suspension! with { Envelope = envelope };

        var rejected = await harness.Workflow.ResumeAsync(
            mutated,
            approval.Nonce,
            CancellationToken.None);
        var legitimate = await harness.Workflow.ResumeAsync(
            suspended.Suspension!,
            approval.Nonce,
            CancellationToken.None);

        Assert.Equal(AgentWorkflowStatus.Failed, rejected.Status);
        Assert.Equal("suspension_binding_mismatch", rejected.ReasonCode);
        Assert.Equal(AgentWorkflowStatus.Completed, legitimate.Status);
    }

    [Fact]
    public async Task ApprovalRemainsSingleUseAcrossResumeAttempts()
    {
        var harness = CreateHarness();
        var suspended = await harness.Workflow.ExecuteAsync(
            harness.Request,
            CancellationToken.None);
        var approval = CreateApproval(suspended.Suspension!);
        harness.Approvals.Add(approval);

        var first = await harness.Workflow.ResumeAsync(
            suspended.Suspension!,
            approval.Nonce,
            CancellationToken.None);
        var retrySuspension = suspended.Suspension! with
        {
            Request = suspended.Suspension.Request with { ExpectedResourceVersion = 2 }
        };
        var second = await harness.Workflow.ResumeAsync(
            retrySuspension,
            approval.Nonce,
            CancellationToken.None);

        Assert.Equal(AgentWorkflowStatus.Completed, first.Status);
        Assert.Equal(AgentWorkflowStatus.Failed, second.Status);
        Assert.Equal("approval_invalid", second.ReasonCode);
        var instance = harness.Simulator
            .GetServiceHealth(IncidentSimulator.DemoServiceId)
            .Instances.Single(item => item.InstanceId == "payments-api-03");
        Assert.Equal(1, instance.RestartCount);
    }

    [Fact]
    public async Task CompletionIsPendingWhenSimulatorGoalIsNotSatisfied()
    {
        var harness = CreateHarness(
            completionCriteria: new WorkflowCompletionCriteria(
                IncidentSimulator.DemoIncidentId,
                IncidentStatus.Resolved));
        var suspended = await harness.Workflow.ExecuteAsync(
            harness.Request,
            CancellationToken.None);
        var approval = CreateApproval(suspended.Suspension!);
        harness.Approvals.Add(approval);

        var result = await harness.Workflow.ResumeAsync(
            suspended.Suspension!,
            approval.Nonce,
            CancellationToken.None);

        Assert.Equal(AgentWorkflowStatus.InProgress, result.Status);
        Assert.Equal("simulator_completion_pending", result.ReasonCode);
        Assert.False(result.SimulatorState!.IsComplete);
        Assert.Equal(IncidentStatus.Open, result.SimulatorState.IncidentStatus);
    }

    [Fact]
    public async Task FreshApprovalAndSameIdempotencyKeyReplaysWithoutDuplicateWrite()
    {
        var harness = CreateHarness();
        var suspended = await harness.Workflow.ExecuteAsync(
            harness.Request,
            CancellationToken.None);
        harness.Approvals.Add(CreateApproval(suspended.Suspension!));
        var first = await harness.Workflow.ResumeAsync(
            suspended.Suspension!,
            "approval-nonce-1",
            CancellationToken.None);
        var retrySuspension = suspended.Suspension! with
        {
            Request = suspended.Suspension.Request with { ExpectedResourceVersion = 2 }
        };
        harness.Approvals.Add(CreateApproval(retrySuspension, "approval-nonce-2"));

        var replay = await harness.Workflow.ResumeAsync(
            retrySuspension,
            "approval-nonce-2",
            CancellationToken.None);

        Assert.Equal(AgentWorkflowStatus.Completed, first.Status);
        Assert.Equal(AgentWorkflowStatus.Completed, replay.Status);
        var instance = harness.Simulator
            .GetServiceHealth(IncidentSimulator.DemoServiceId)
            .Instances.Single(item => item.InstanceId == "payments-api-03");
        Assert.Equal(1, instance.RestartCount);
    }

    private static WorkflowHarness CreateHarness(
        StubVerifier? verifier = null,
        WorkflowCompletionCriteria? completionCriteria = null)
    {
        var step = new PlanStep(
            "step-1",
            "service.restart",
            "restart_service",
            new ResourceReference(
                "service",
                IncidentSimulator.DemoServiceId,
                TargetEnvironment.Production,
                DataClassification.Internal),
            [new DataSourceReference("payments-api-metrics", DataClassification.Internal)],
            new DestinationReference(
                IncidentSimulator.DemoServiceId,
                DataClassification.InternalTrusted),
            new Dictionary<string, JsonElement>
            {
                ["serviceId"] = JsonSerializer.SerializeToElement(
                    IncidentSimulator.DemoServiceId),
                ["instanceId"] = JsonSerializer.SerializeToElement("payments-api-03")
            },
            [],
            EffectKind.Write,
            ApprovalClass.IncidentCommander,
            null);
        var plan = new ActionPlan(
            "1.0",
            Guid.NewGuid(),
            IncidentSimulator.DemoIncidentId,
            "incident-agent",
            "1.0.0",
            Now.AddMinutes(-1),
            Now.AddMinutes(5),
            [step]);
        var registry = new ToolRegistry();
        var canonicalizer = new ActionCanonicalizer(registry);
        var simulator = new IncidentSimulator(new FixedTimeProvider(Now));
        var approvals = new InMemoryApprovalStore();
        var audit = new InMemoryAuditChain();
        var gateway = new GovernedToolGateway(
            registry,
            canonicalizer,
            new DefaultDenyPolicyEvaluator(),
            approvals,
            new InMemoryExecutionBudgetStore(
                new ExecutionBudgetLimits(12, TimeSpan.FromMinutes(3))),
            new InMemoryKillSwitch(),
            audit,
            new SimulatorGovernedToolExecutor(simulator),
            new FixedTimeProvider(Now));
        var workflow = new LocalDeterministicAgentWorkflow(
            verifier ?? new StubVerifier(),
            canonicalizer,
            gateway,
            new SimulatorWorkflowCompletionEvaluator(simulator),
            ["service.restart"],
            new Dictionary<string, VerifierToolMetadata>
            {
                ["restart_service"] = new(
                    "service.restart",
                    EffectKind.Write,
                    ApprovalClass.IncidentCommander,
                    "serviceId")
            },
            timeProvider: new FixedTimeProvider(Now));
        var request = new AgentWorkflowRequest(
            plan,
            step.StepId,
            new UserIdentity("operator-1", ["incident-operator"]),
            new AgentIdentity("incident-agent", "agent-identity", "1.0.0"),
            new SessionIdentity("session-1", IncidentSimulator.DemoIncidentId),
            "restart-key-1",
            ExpectedResourceVersion: 1,
            completionCriteria ?? new WorkflowCompletionCriteria(
                IncidentSimulator.DemoIncidentId,
                ServiceId: IncidentSimulator.DemoServiceId,
                ServiceHealth: GovernedAgent.Simulator.ServiceHealth.Healthy));

        return new WorkflowHarness(workflow, simulator, approvals, audit, request);
    }

    private static ApprovalArtifact CreateApproval(
        AgentWorkflowSuspension suspension,
        string nonce = "approval-nonce-1") =>
        new(
            Guid.NewGuid(),
            "commander-1",
            ["incident-commander"],
            suspension.Request.Plan.PlanId,
            suspension.Request.StepId,
            suspension.ActionDigest,
            IncidentSimulator.DemoServiceId,
            TargetEnvironment.Production,
            ApprovalDecision.Approved,
            Now.AddMinutes(-1),
            Now.AddMinutes(5),
            nonce,
            "1.0");

    private sealed record WorkflowHarness(
        LocalDeterministicAgentWorkflow Workflow,
        IncidentSimulator Simulator,
        InMemoryApprovalStore Approvals,
        InMemoryAuditChain Audit,
        AgentWorkflowRequest Request);

    private sealed class StubVerifier(
        VerificationResult result = VerificationResult.Verified,
        bool throwUnavailable = false,
        bool useWrongDigest = false) : IPlanVerifier
    {
        public ValueTask<PlanVerificationDecision> VerifyAsync(
            PlanVerificationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (throwUnavailable)
            {
                throw new GovernanceException(
                    ErrorCategory.VerificationUnavailable,
                    "verifier_unavailable",
                    "The verifier is unavailable.");
            }

            return ValueTask.FromResult(new PlanVerificationDecision(
                result,
                result == VerificationResult.Verified ? [] : ["unsafe-plan"],
                useWrongDigest ? new string('f', 64) : request.PlanDigest,
                request.SpecificationVersion,
                request.VerifierVersion));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
