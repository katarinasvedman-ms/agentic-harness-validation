using System.Text.Json;
using GovernedAgent.Core.Contracts;
using GovernedAgent.Governance;

namespace GovernedAgent.UnitTests;

public sealed class GovernanceKernelTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-12T10:00:00Z");

    [Fact]
    public void CanonicalDigestIsIndependentOfArgumentInsertionOrder()
    {
        var firstArguments = new Dictionary<string, JsonElement>
        {
            ["instance"] = JsonSerializer.SerializeToElement("payments-api-03"),
            ["reason"] = JsonSerializer.SerializeToElement("error-rate")
        };
        var secondArguments = new Dictionary<string, JsonElement>
        {
            ["reason"] = JsonSerializer.SerializeToElement("error-rate"),
            ["instance"] = JsonSerializer.SerializeToElement("payments-api-03")
        };
        var canonicalizer = new ActionCanonicalizer(new ToolRegistry());

        var first = CreatePlan(firstArguments);
        var second = CreatePlan(secondArguments, first.PlanId);

        var firstDigest = canonicalizer.CreateDigest(first, first.Steps[0]);
        var secondDigest = canonicalizer.CreateDigest(second, second.Steps[0]);

        Assert.Equal(firstDigest.Value, secondDigest.Value);
        Assert.Equal(firstDigest.CanonicalJson, secondDigest.CanonicalJson);
    }

    [Fact]
    public void CanonicalDigestChangesWhenAnArgumentChanges()
    {
        var canonicalizer = new ActionCanonicalizer(new ToolRegistry());
        var first = CreatePlan(Arguments("payments-api-03"));
        var second = CreatePlan(Arguments("payments-api-04"), first.PlanId);

        Assert.NotEqual(
            canonicalizer.CreateDigest(first, first.Steps[0]).Value,
            canonicalizer.CreateDigest(second, second.Steps[0]).Value);
    }

    [Fact]
    public void CanonicalizerRejectsModelSuppliedMetadataThatDisagreesWithRegistry()
    {
        var plan = CreatePlan(Arguments("payments-api-03"));
        var mutatedStep = plan.Steps[0] with { Capability = "subscription.delete" };
        var canonicalizer = new ActionCanonicalizer(new ToolRegistry());

        var error = Assert.Throws<GovernanceException>(
            () => canonicalizer.CreateDigest(plan, mutatedStep));

        Assert.Equal("tool_metadata_mismatch", error.Code);
    }

    [Fact]
    public void ApprovalIsExactAndSingleUse()
    {
        var store = new InMemoryApprovalStore();
        var approval = CreateApproval();
        var request = CreateConsumptionRequest();
        store.Add(approval);

        Assert.True(store.TryConsume(approval.Nonce, request, out var consumed));
        Assert.Equal(approval.ApprovalId, consumed?.ApprovalId);
        Assert.False(store.TryConsume(approval.Nonce, request, out _));
    }

    [Fact]
    public void ApprovalCannotAuthorizeMutatedAction()
    {
        var store = new InMemoryApprovalStore();
        var approval = CreateApproval();
        store.Add(approval);

        var mutated = CreateConsumptionRequest() with
        {
            ActionDigest = new string('b', 64)
        };

        Assert.False(store.TryConsume(approval.Nonce, mutated, out _));
        Assert.True(store.TryConsume(
            approval.Nonce,
            CreateConsumptionRequest(),
            out _));
    }

    [Fact]
    public void RevokedApprovalCannotBeConsumed()
    {
        var store = new InMemoryApprovalStore();
        var approval = CreateApproval();
        store.Add(approval);

        Assert.True(store.Revoke(approval.Nonce));
        Assert.False(store.TryConsume(
            approval.Nonce,
            CreateConsumptionRequest(),
            out _));
    }

    [Fact]
    public async Task PolicyRequiresExactApprovalForProductionWrite()
    {
        var context = CreatePolicyContext(hasApproval: false);
        var evaluator = new DefaultDenyPolicyEvaluator();

        var decision = await evaluator.EvaluateAsync(context, CancellationToken.None);

        Assert.Equal(GovernanceDecision.RequireApproval, decision.Decision);
    }

    [Fact]
    public async Task KillSwitchOverridesOtherwiseAllowedAction()
    {
        var context = CreatePolicyContext(hasApproval: true) with
        {
            KillSwitchActive = true
        };
        var evaluator = new DefaultDenyPolicyEvaluator();

        var decision = await evaluator.EvaluateAsync(context, CancellationToken.None);

        Assert.Equal(GovernanceDecision.Deny, decision.Decision);
        Assert.Equal("kill_switch_active", decision.ReasonCode);
    }

    [Fact]
    public void BudgetFailsClosedAfterConfiguredToolCalls()
    {
        var store = new InMemoryExecutionBudgetStore(
            new ExecutionBudgetLimits(2, TimeSpan.FromMinutes(1)));

        Assert.True(store.TryConsumeToolCall("session-1", Now));
        Assert.True(store.TryConsumeToolCall("session-1", Now.AddSeconds(1)));
        Assert.False(store.TryConsumeToolCall("session-1", Now.AddSeconds(2)));
    }

    [Fact]
    public void AuditRecordsAreLinkedAndVerifiable()
    {
        var chain = new InMemoryAuditChain();

        var first = chain.Append(CreateAuditRecord(Guid.NewGuid()));
        var second = chain.Append(CreateAuditRecord(Guid.NewGuid()));

        Assert.Null(first.PreviousRecordHash);
        Assert.Equal(first.RecordHash, second.PreviousRecordHash);
        Assert.True(chain.VerifyIntegrity());
    }

    private static Dictionary<string, JsonElement> Arguments(string instance) =>
        new()
        {
            ["instance"] = JsonSerializer.SerializeToElement(instance)
        };

    private static ActionPlan CreatePlan(
        IReadOnlyDictionary<string, JsonElement> arguments,
        Guid? planId = null)
    {
        var step = new PlanStep(
            "step-1",
            "service.restart",
            "restart_service",
            new ResourceReference(
                "service",
                "payments-api",
                TargetEnvironment.Production,
                DataClassification.Internal),
            [new DataSourceReference("payments-api-metrics", DataClassification.Internal)],
            new DestinationReference(
                "payments-api",
                DataClassification.InternalTrusted),
            arguments,
            [],
            EffectKind.Write,
            ApprovalClass.IncidentCommander,
            null);

        return new ActionPlan(
            "1.0",
            planId ?? Guid.NewGuid(),
            "INC-1042",
            "incident-agent",
            "1.0.0",
            Now,
            Now.AddMinutes(5),
            [step]);
    }

    private static ApprovalArtifact CreateApproval() =>
        new(
            Guid.NewGuid(),
            "commander-1",
            ["incident-commander"],
            Guid.Parse("6e33af3f-f4eb-44da-ae50-936f15c868c4"),
            "step-1",
            new string('a', 64),
            "payments-api",
            TargetEnvironment.Production,
            ApprovalDecision.Approved,
            Now,
            Now.AddMinutes(5),
            "nonce-1",
            "1.0");

    private static ApprovalConsumptionRequest CreateConsumptionRequest() =>
        new(
            Guid.Parse("6e33af3f-f4eb-44da-ae50-936f15c868c4"),
            "step-1",
            new string('a', 64),
            "payments-api",
            TargetEnvironment.Production,
            "incident-commander",
            "1.0",
            Now.AddMinutes(1));

    private static PolicyEvaluationContext CreatePolicyContext(bool hasApproval)
    {
        var tool = new ToolRegistry().TryGet("restart_service", out var metadata)
            ? metadata
            : throw new InvalidOperationException("Default tool registry is incomplete.");
        var envelope = new TrustedActionEnvelope(
            "1.0",
            Guid.NewGuid(),
            Now,
            new UserIdentity("operator-1", ["incident-operator"]),
            new AgentIdentity("incident-agent", "agent-identity", "1.0.0"),
            new SessionIdentity("session-1", "INC-1042"),
            new GovernedAction(
                Guid.NewGuid(),
                "step-1",
                tool.Name,
                tool.Capability,
                tool.Effect,
                new ActionResource("payments-api", TargetEnvironment.Production),
                new string('a', 64)),
            new VerificationAttestation(
                VerificationResult.Verified,
                "1.0",
                "1.0",
                new string('c', 64)));

        return new PolicyEvaluationContext(
            envelope,
            tool,
            KillSwitchActive: false,
            BudgetAvailable: true,
            HasValidApproval: hasApproval);
    }

    private static AuditRecord CreateAuditRecord(Guid recordId) =>
        new(
            recordId,
            Guid.NewGuid(),
            "correlation-1",
            "INC-1042",
            Guid.NewGuid(),
            "step-1",
            new string('a', 64),
            GovernanceDecision.Allow,
            "1.0",
            VerificationResult.Verified,
            ExecutionState.Executing,
            Now,
            PreviousRecordHash: null,
            RecordHash: string.Empty);
}
