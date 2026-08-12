using System.Text.Json;
using GovernedAgent.Core.Contracts;
using GovernedAgent.Governance;
using GovernedAgent.Simulator;

namespace GovernedAgent.IntegrationTests;

public sealed class GovernedGatewayTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-12T10:00:00Z");

    [Fact]
    public async Task ProductionWriteWaitsForExactApprovalWithoutSideEffect()
    {
        var harness = CreateHarness(maximumToolCalls: 1);

        var result = await harness.Gateway.ExecuteAsync(
            CreateRequest(harness.Plan, harness.Envelope, approvalNonce: null),
            CancellationToken.None);

        Assert.Equal(GatewayOutcome.ApprovalRequired, result.Outcome);
        Assert.Equal(
            ServiceHealth.Degraded,
            harness.Simulator.GetServiceHealth(IncidentSimulator.DemoServiceId).Health);
        Assert.Single(harness.Audit.ReadAll());

        var approval = CreateApproval(harness.Plan, harness.Envelope.Action.ActionDigest);
        harness.Approvals.Add(approval);
        var executed = await harness.Gateway.ExecuteAsync(
            CreateRequest(harness.Plan, harness.Envelope, approval.Nonce),
            CancellationToken.None);
        Assert.Equal(GatewayOutcome.Executed, executed.Outcome);
    }

    [Fact]
    public async Task ExactApprovalExecutesOnlyThroughGateway()
    {
        var harness = CreateHarness();
        var approval = CreateApproval(harness.Plan, harness.Envelope.Action.ActionDigest);
        harness.Approvals.Add(approval);

        var result = await harness.Gateway.ExecuteAsync(
            CreateRequest(harness.Plan, harness.Envelope, approval.Nonce),
            CancellationToken.None);

        Assert.Equal(GatewayOutcome.Executed, result.Outcome);
        Assert.Equal(
            ServiceHealth.Healthy,
            harness.Simulator.GetServiceHealth(IncidentSimulator.DemoServiceId).Health);
        Assert.Equal(2, harness.Audit.ReadAll().Count);
        Assert.True(harness.Audit.VerifyIntegrity());
    }

    [Fact]
    public async Task ConsumedApprovalCannotBeReplayed()
    {
        var harness = CreateHarness();
        var approval = CreateApproval(harness.Plan, harness.Envelope.Action.ActionDigest);
        harness.Approvals.Add(approval);
        var request = CreateRequest(harness.Plan, harness.Envelope, approval.Nonce);

        await harness.Gateway.ExecuteAsync(request, CancellationToken.None);
        var versionAfterExecution = harness.Simulator
            .GetServiceHealth(IncidentSimulator.DemoServiceId)
            .Version;

        var error = await Assert.ThrowsAsync<GovernanceException>(async () =>
            await harness.Gateway.ExecuteAsync(
                request with { ExpectedResourceVersion = versionAfterExecution },
                CancellationToken.None));

        Assert.Equal("approval_invalid", error.Code);
        Assert.Equal(
            versionAfterExecution,
            harness.Simulator.GetServiceHealth(IncidentSimulator.DemoServiceId).Version);
    }

    [Fact]
    public async Task GatewayDeniesWriteEvenWhenHookLayerIsBypassed()
    {
        var harness = CreateHarness();
        harness.KillSwitch.Activate();
        var approval = CreateApproval(harness.Plan, harness.Envelope.Action.ActionDigest);
        harness.Approvals.Add(approval);

        var error = await Assert.ThrowsAsync<GovernanceException>(async () =>
            await harness.Gateway.ExecuteAsync(
                CreateRequest(harness.Plan, harness.Envelope, approval.Nonce),
                CancellationToken.None));

        Assert.Equal("kill_switch_active", error.Code);
        Assert.Equal(
            ServiceHealth.Degraded,
            harness.Simulator.GetServiceHealth(IncidentSimulator.DemoServiceId).Health);
        Assert.Single(harness.Audit.ReadAll());
        Assert.True(harness.Approvals.TryConsume(
            approval.Nonce,
            CreateConsumptionRequest(harness, Now),
            out _));
    }

    [Fact]
    public async Task BudgetDenialDoesNotConsumeApproval()
    {
        var harness = CreateHarness(maximumToolCalls: 0);
        var approval = CreateApproval(harness.Plan, harness.Envelope.Action.ActionDigest);
        harness.Approvals.Add(approval);

        var error = await Assert.ThrowsAsync<GovernanceException>(async () =>
            await harness.Gateway.ExecuteAsync(
                CreateRequest(harness.Plan, harness.Envelope, approval.Nonce),
                CancellationToken.None));

        Assert.Equal("budget_exhausted", error.Code);
        Assert.True(harness.Approvals.TryConsume(
            approval.Nonce,
            CreateConsumptionRequest(harness, Now),
            out _));
    }

    [Fact]
    public async Task UnknownArgumentsAreDeniedBeforeApprovalOrSideEffect()
    {
        var harness = CreateHarness(includeUnknownArgument: true);
        var approval = CreateApproval(harness.Plan, harness.Envelope.Action.ActionDigest);
        harness.Approvals.Add(approval);

        var error = await Assert.ThrowsAsync<GovernanceException>(async () =>
            await harness.Gateway.ExecuteAsync(
                CreateRequest(harness.Plan, harness.Envelope, approval.Nonce),
                CancellationToken.None));

        Assert.Equal("invalid_tool_arguments", error.Code);
        Assert.Equal(
            ServiceHealth.Degraded,
            harness.Simulator.GetServiceHealth(IncidentSimulator.DemoServiceId).Health);

        var validRequest = CreateConsumptionRequest(harness, Now);
        Assert.True(harness.Approvals.TryConsume(
            approval.Nonce,
            validRequest,
            out _));
    }

    [Fact]
    public async Task StaleVersionIsDeniedBeforeApprovalConsumption()
    {
        var harness = CreateHarness();
        var approval = CreateApproval(harness.Plan, harness.Envelope.Action.ActionDigest);
        harness.Approvals.Add(approval);

        var error = await Assert.ThrowsAsync<GovernanceException>(async () =>
            await harness.Gateway.ExecuteAsync(
                CreateRequest(harness.Plan, harness.Envelope, approval.Nonce) with
                {
                    ExpectedResourceVersion = 999
                },
                CancellationToken.None));

        Assert.Equal("stale_resource_version", error.Code);
        Assert.Equal(
            ServiceHealth.Degraded,
            harness.Simulator.GetServiceHealth(IncidentSimulator.DemoServiceId).Health);
        Assert.True(harness.Approvals.TryConsume(
            approval.Nonce,
            CreateConsumptionRequest(harness, Now),
            out _));
    }

    [Fact]
    public async Task InvalidIdempotencyKeyIsDeniedBeforeApprovalConsumption()
    {
        var harness = CreateHarness();
        var approval = CreateApproval(harness.Plan, harness.Envelope.Action.ActionDigest);
        harness.Approvals.Add(approval);

        var error = await Assert.ThrowsAsync<GovernanceException>(async () =>
            await harness.Gateway.ExecuteAsync(
                CreateRequest(harness.Plan, harness.Envelope, approval.Nonce) with
                {
                    IdempotencyKey = " "
                },
                CancellationToken.None));

        Assert.Equal("invalid_idempotency_key", error.Code);
        Assert.True(harness.Approvals.TryConsume(
            approval.Nonce,
            CreateConsumptionRequest(harness, Now),
            out _));
    }

    [Fact]
    public async Task DigestMutationIsDeniedBeforeSideEffect()
    {
        var harness = CreateHarness();
        var mutatedEnvelope = harness.Envelope with
        {
            Action = harness.Envelope.Action with
            {
                ActionDigest = new string('f', 64)
            }
        };

        var error = await Assert.ThrowsAsync<GovernanceException>(async () =>
            await harness.Gateway.ExecuteAsync(
                CreateRequest(harness.Plan, mutatedEnvelope, approvalNonce: null),
                CancellationToken.None));

        Assert.Equal("trusted_envelope_mismatch", error.Code);
        Assert.Equal(
            ServiceHealth.Degraded,
            harness.Simulator.GetServiceHealth(IncidentSimulator.DemoServiceId).Health);
    }

    private static GatewayHarness CreateHarness(
        bool includeUnknownArgument = false,
        int maximumToolCalls = 12)
    {
        var arguments = new Dictionary<string, JsonElement>
        {
            ["serviceId"] = JsonSerializer.SerializeToElement(
                IncidentSimulator.DemoServiceId),
            ["instanceId"] = JsonSerializer.SerializeToElement("payments-api-03")
        };
        if (includeUnknownArgument)
        {
            arguments["credential"] = JsonSerializer.SerializeToElement("forbidden");
        }

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
            arguments,
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
        var digest = canonicalizer.CreateDigest(plan, step);
        var envelope = new TrustedActionEnvelope(
            "1.0",
            Guid.NewGuid(),
            Now,
            new UserIdentity("operator-1", ["incident-operator"]),
            new AgentIdentity("incident-agent", "agent-identity", "1.0.0"),
            new SessionIdentity("session-1", IncidentSimulator.DemoIncidentId),
            new GovernedAction(
                plan.PlanId,
                step.StepId,
                step.Tool,
                step.Capability,
                step.Effect,
                new ActionResource(step.Resource.Id, step.Resource.Environment),
                digest.Value),
            new VerificationAttestation(
                VerificationResult.Verified,
                "1.0",
                "1.0",
                new string('c', 64)));
        var simulator = new IncidentSimulator();
        var approvals = new InMemoryApprovalStore();
        var killSwitch = new InMemoryKillSwitch();
        var audit = new InMemoryAuditChain();
        var gateway = new GovernedToolGateway(
            registry,
            canonicalizer,
            new DefaultDenyPolicyEvaluator(),
            approvals,
            new InMemoryExecutionBudgetStore(
                new ExecutionBudgetLimits(maximumToolCalls, TimeSpan.FromMinutes(3))),
            killSwitch,
            audit,
            new SimulatorGovernedToolExecutor(simulator),
            new FixedTimeProvider(Now));

        return new GatewayHarness(
            gateway,
            simulator,
            approvals,
            killSwitch,
            audit,
            plan,
            envelope);
    }

    private static GovernedToolRequest CreateRequest(
        ActionPlan plan,
        TrustedActionEnvelope envelope,
        string? approvalNonce) =>
        new(
            plan,
            "step-1",
            envelope,
            approvalNonce,
            "restart-key-1",
            ExpectedResourceVersion: 1);

    private static ApprovalArtifact CreateApproval(
        ActionPlan plan,
        string actionDigest) =>
        new(
            Guid.NewGuid(),
            "commander-1",
            ["incident-commander"],
            plan.PlanId,
            "step-1",
            actionDigest,
            IncidentSimulator.DemoServiceId,
            TargetEnvironment.Production,
            ApprovalDecision.Approved,
            Now.AddMinutes(-1),
            Now.AddMinutes(5),
            "approval-nonce-1",
            "1.0");

    private static ApprovalConsumptionRequest CreateConsumptionRequest(
        GatewayHarness harness,
        DateTimeOffset now) =>
        new(
            harness.Plan.PlanId,
            "step-1",
            harness.Envelope.Action.ActionDigest,
            IncidentSimulator.DemoServiceId,
            TargetEnvironment.Production,
            "incident-commander",
            "1.0",
            now);

    private sealed record GatewayHarness(
        GovernedToolGateway Gateway,
        IncidentSimulator Simulator,
        InMemoryApprovalStore Approvals,
        InMemoryKillSwitch KillSwitch,
        InMemoryAuditChain Audit,
        ActionPlan Plan,
        TrustedActionEnvelope Envelope);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
