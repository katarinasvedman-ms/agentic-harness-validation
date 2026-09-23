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
    public async Task ContainmentOverridesOtherwiseAllowedAction()
    {
        var context = CreatePolicyContext(hasApproval: true) with
        {
            ContainmentMode = ContainmentMode.Contained
        };
        var evaluator = new DefaultDenyPolicyEvaluator();

        var decision = await evaluator.EvaluateAsync(context, CancellationToken.None);

        Assert.Equal(GovernanceDecision.Deny, decision.Decision);
        Assert.Equal("containment_active", decision.ReasonCode);
    }

    [Fact]
    public async Task ReadOnlyRecoveryDeniesWrite()
    {
        var context = CreatePolicyContext(hasApproval: true) with
        {
            ContainmentMode = ContainmentMode.ReadOnlyRecovery
        };
        var evaluator = new DefaultDenyPolicyEvaluator();

        var decision = await evaluator.EvaluateAsync(context, CancellationToken.None);

        Assert.Equal(GovernanceDecision.Deny, decision.Decision);
        Assert.Equal("recovery_read_only", decision.ReasonCode);
    }

    [Fact]
    public async Task ReadOnlyRecoveryAllowsRegisteredRead()
    {
        var context = CreatePolicyContext(
            hasApproval: true,
            toolName: "get_service_health") with
        {
            ContainmentMode = ContainmentMode.ReadOnlyRecovery
        };
        var evaluator = new DefaultDenyPolicyEvaluator();

        var decision = await evaluator.EvaluateAsync(context, CancellationToken.None);

        Assert.Equal(GovernanceDecision.Allow, decision.Decision);
    }

    [Fact]
    public void RecoveryRequiresOrderedEvidence()
    {
        var control = new InMemoryContainmentControl();
        var artifact = new ReattestationArtifact(
            new string('a', 64),
            "agent-image:sha256:known-good",
            "governance-operator",
            Now);
        var signOff = new RecoverySignOff(
            "incident-commander",
            "Removed the untrusted integration and verified the known-good deployment.",
            Now.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(
            () => control.BeginReadOnlyRecovery(artifact));
        Assert.Throws<InvalidOperationException>(
            () => control.RestoreOperational(signOff));

        control.Contain();
        control.BeginReadOnlyRecovery(artifact);
        control.RestoreOperational(signOff);

        Assert.Equal(ContainmentMode.Operational, control.Mode);
        Assert.Equal(artifact, control.Reattestation);
        Assert.Equal(signOff, control.SignOff);
    }

    [Fact]
    public void CapabilityLeaseIsExactSingleUseAndCompletable()
    {
        var context = CreatePolicyContext(hasApproval: true);
        var store = new InMemoryCapabilityLeaseStore();
        var lease = store.Issue(new CapabilityLeaseIssueRequest(
            context.Envelope,
            context.Tool,
            "1.0",
            Now,
            TimeSpan.FromSeconds(90),
            MaximumUses: 1));
        var request = new CapabilityLeaseConsumptionRequest(
            context.Envelope,
            context.Tool,
            "1.0",
            Now.AddSeconds(1));

        Assert.Equal(CapabilityLeaseState.Issued, lease.State);
        Assert.Equal(IntentClass.Remediate, lease.Intent);
        Assert.True(store.TryConsume(lease.Nonce, request, out var consumed));
        Assert.Equal(CapabilityLeaseState.Consumed, consumed?.State);
        Assert.False(store.TryConsume(lease.Nonce, request, out _));
        Assert.True(store.TryComplete(
            lease.LeaseId,
            Now.AddSeconds(2),
            out var completed));
        Assert.Equal(CapabilityLeaseState.Completed, completed?.State);
    }

    [Fact]
    public void ExpiredCapabilityLeaseCannotBeConsumed()
    {
        var context = CreatePolicyContext(hasApproval: true);
        var store = new InMemoryCapabilityLeaseStore();
        var lease = store.Issue(new CapabilityLeaseIssueRequest(
            context.Envelope,
            context.Tool,
            "1.0",
            Now,
            TimeSpan.FromSeconds(30),
            MaximumUses: 1));

        Assert.False(store.TryConsume(
            lease.Nonce,
            new CapabilityLeaseConsumptionRequest(
                context.Envelope,
                context.Tool,
                "1.0",
                Now.AddSeconds(30)),
            out _));
        Assert.Equal(
            CapabilityLeaseState.Expired,
            Assert.Single(store.ReadAll(Now.AddSeconds(30))).State);
    }

    [Fact]
    public void CapabilityLeaseRejectsChangedResourceBinding()
    {
        var context = CreatePolicyContext(hasApproval: true);
        var store = new InMemoryCapabilityLeaseStore();
        var lease = store.Issue(new CapabilityLeaseIssueRequest(
            context.Envelope,
            context.Tool,
            "1.0",
            Now,
            TimeSpan.FromSeconds(90),
            MaximumUses: 1));
        var mutatedEnvelope = context.Envelope with
        {
            Action = context.Envelope.Action with
            {
                Resource = new ActionResource(
                    "different-service",
                    TargetEnvironment.Production)
            }
        };

        Assert.False(store.TryConsume(
            lease.Nonce,
            new CapabilityLeaseConsumptionRequest(
                mutatedEnvelope,
                context.Tool,
                "1.0",
                Now.AddSeconds(1)),
            out _));
    }

    [Fact]
    public async Task ConcurrentCapabilityLeaseConsumptionHasOneWinner()
    {
        var context = CreatePolicyContext(hasApproval: true);
        var store = new InMemoryCapabilityLeaseStore();
        var lease = store.Issue(new CapabilityLeaseIssueRequest(
            context.Envelope,
            context.Tool,
            "1.0",
            Now,
            TimeSpan.FromSeconds(90),
            MaximumUses: 1));
        var request = new CapabilityLeaseConsumptionRequest(
            context.Envelope,
            context.Tool,
            "1.0",
            Now.AddSeconds(1));

        var attempts = await Task.WhenAll(
            Enumerable.Range(0, 20)
                .Select(index => Task.Run(() =>
                    store.TryConsume(lease.Nonce, request, out _))));

        Assert.Equal(1, attempts.Count(consumed => consumed));
    }

    [Fact]
    public void CapabilityLeaseRejectsChangedTrustedBindings()
    {
        var context = CreatePolicyContext(hasApproval: true);
        var store = new InMemoryCapabilityLeaseStore();
        var lease = store.Issue(new CapabilityLeaseIssueRequest(
            context.Envelope,
            context.Tool,
            "1.0",
            Now,
            TimeSpan.FromSeconds(90),
            MaximumUses: 1));
        var changedSession = context.Envelope with
        {
            Session = context.Envelope.Session with { Id = "different-session" }
        };
        var changedIntent = context.Envelope with
        {
            Action = context.Envelope.Action with { Intent = IntentClass.Communicate }
        };

        Assert.False(store.TryConsume(
            lease.Nonce,
            new CapabilityLeaseConsumptionRequest(
                changedSession,
                context.Tool,
                "1.0",
                Now.AddSeconds(1)),
            out _));
        Assert.False(store.TryConsume(
            lease.Nonce,
            new CapabilityLeaseConsumptionRequest(
                changedIntent,
                context.Tool,
                "1.0",
                Now.AddSeconds(1)),
            out _));
        Assert.False(store.TryConsume(
            lease.Nonce,
            new CapabilityLeaseConsumptionRequest(
                context.Envelope,
                context.Tool,
                "2.0",
                Now.AddSeconds(1)),
            out _));
    }

    [Fact]
    public void AgentRevocationCoversIssuedAndConsumedLeases()
    {
        var context = CreatePolicyContext(hasApproval: true);
        var store = new InMemoryCapabilityLeaseStore();
        var issued = store.Issue(new CapabilityLeaseIssueRequest(
            context.Envelope,
            context.Tool,
            "1.0",
            Now,
            TimeSpan.FromSeconds(90),
            MaximumUses: 1));
        var secondEnvelope = context.Envelope with
        {
            RequestId = Guid.NewGuid(),
            Session = context.Envelope.Session with { Id = "session-2" }
        };
        var consumed = store.Issue(new CapabilityLeaseIssueRequest(
            secondEnvelope,
            context.Tool,
            "1.0",
            Now,
            TimeSpan.FromSeconds(90),
            MaximumUses: 1));
        Assert.True(store.TryConsume(
            consumed.Nonce,
            new CapabilityLeaseConsumptionRequest(
                secondEnvelope,
                context.Tool,
                "1.0",
                Now.AddSeconds(1)),
            out _));

        var revoked = store.RevokeAgent(
            context.Envelope.Agent.Id,
            "Contain the agent.");

        Assert.Equal(2, revoked.Count);
        Assert.All(revoked, lease =>
        {
            Assert.Equal(CapabilityLeaseState.Revoked, lease.State);
            Assert.Equal("Contain the agent.", lease.RevocationReason);
        });
        Assert.Contains(revoked, lease => lease.LeaseId == issued.LeaseId);
        Assert.Contains(revoked, lease => lease.LeaseId == consumed.LeaseId);
    }

    [Fact]
    public void UnverifiedPlanCannotIssueCapabilityLease()
    {
        var context = CreatePolicyContext(hasApproval: true);
        var envelope = context.Envelope with
        {
            Verification = context.Envelope.Verification with
            {
                Result = VerificationResult.Rejected
            }
        };
        var store = new InMemoryCapabilityLeaseStore();

        var error = Assert.Throws<GovernanceException>(() =>
            store.Issue(new CapabilityLeaseIssueRequest(
                envelope,
                context.Tool,
                "1.0",
                Now,
                TimeSpan.FromSeconds(90),
                MaximumUses: 1)));

        Assert.Equal("lease_plan_not_verified", error.Code);
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

    private static PolicyEvaluationContext CreatePolicyContext(
        bool hasApproval,
        string toolName = "restart_service")
    {
        var tool = new ToolRegistry().TryGet(toolName, out var metadata)
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
                tool.Intent,
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
            ContainmentMode.Operational,
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
