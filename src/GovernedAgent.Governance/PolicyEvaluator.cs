using GovernedAgent.Core.Contracts;

namespace GovernedAgent.Governance;

public sealed record PolicyEvaluationContext(
    TrustedActionEnvelope Envelope,
    ToolMetadata Tool,
    bool KillSwitchActive,
    bool BudgetAvailable,
    bool HasValidApproval);

public interface IPolicyEvaluator
{
    ValueTask<PolicyDecision> EvaluateAsync(
        PolicyEvaluationContext context,
        CancellationToken cancellationToken);
}

public sealed class DefaultDenyPolicyEvaluator(
    TimeProvider? timeProvider = null) : IPolicyEvaluator
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public ValueTask<PolicyDecision> EvaluateAsync(
        PolicyEvaluationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var decision = Evaluate(context);
        return ValueTask.FromResult(decision);
    }

    private PolicyDecision Evaluate(PolicyEvaluationContext context)
    {
        if (context.KillSwitchActive)
        {
            return Deny("kill_switch_active", "POL-001");
        }

        if (!context.BudgetAvailable)
        {
            return Deny("budget_exhausted", "POL-002");
        }

        if (context.Envelope.Verification.Result != VerificationResult.Verified)
        {
            return Deny("plan_not_verified", "POL-003");
        }

        var action = context.Envelope.Action;
        var tool = context.Tool;
        if (!string.Equals(action.Tool, tool.Name, StringComparison.Ordinal) ||
            !string.Equals(action.Capability, tool.Capability, StringComparison.Ordinal) ||
            action.Effect != tool.Effect)
        {
            return Deny("trusted_metadata_mismatch", "POL-004");
        }

        if (action.Resource.Environment == TargetEnvironment.Production &&
            action.Effect == EffectKind.Delete)
        {
            return Deny("production_delete_prohibited", "POL-005");
        }

        if (action.Resource.Environment == TargetEnvironment.Production &&
            action.Effect == EffectKind.Write &&
            !context.HasValidApproval)
        {
            return new PolicyDecision(
                GovernanceDecision.RequireApproval,
                "exact_approval_required",
                "POL-006",
                "1.0",
                _timeProvider.GetUtcNow());
        }

        return new PolicyDecision(
            GovernanceDecision.Allow,
            "policy_satisfied",
            "POL-007",
            "1.0",
            _timeProvider.GetUtcNow());
    }

    private PolicyDecision Deny(string reason, string rule) =>
        new(
            GovernanceDecision.Deny,
            reason,
            rule,
            "1.0",
            _timeProvider.GetUtcNow());
}
