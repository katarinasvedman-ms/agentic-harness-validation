using GovernedAgent.Core.Contracts;
using GovernedAgent.Core.Serialization;

namespace GovernedAgent.Governance;

public sealed class ActionCanonicalizer(IToolRegistry toolRegistry)
{
    public ActionDigestResult CreateDigest(ActionPlan plan, PlanStep step)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(step);

        if (!toolRegistry.TryGet(step.Tool, out var trustedTool))
        {
            throw new GovernanceException(
                ErrorCategory.UnknownTool,
                "unknown_tool",
                $"Tool '{step.Tool}' is not registered.");
        }

        if (!string.Equals(
                trustedTool.Capability,
                step.Capability,
                StringComparison.Ordinal) ||
            trustedTool.Effect != step.Effect ||
            trustedTool.ApprovalClass != step.ApprovalClass)
        {
            throw new GovernanceException(
                ErrorCategory.Validation,
                "tool_metadata_mismatch",
                "Plan metadata does not match the trusted tool registry.");
        }

        if (!trustedTool.Environments.Contains(step.Resource.Environment))
        {
            throw new GovernanceException(
                ErrorCategory.PolicyDenied,
                "environment_not_allowed",
                $"Tool '{step.Tool}' is not registered for '{step.Resource.Environment}'.");
        }

        return CanonicalJson.Digest(new CanonicalAction(
            SchemaVersion: plan.SchemaVersion,
            PlanId: plan.PlanId,
            StepId: step.StepId,
            Tool: trustedTool.Name,
            ToolVersion: trustedTool.Version,
            Capability: trustedTool.Capability,
            Effect: trustedTool.Effect,
            Resource: step.Resource,
            DataSources: step.DataSources,
            Destination: step.Destination,
            Arguments: step.Arguments,
            ApprovalClass: trustedTool.ApprovalClass));
    }
}
