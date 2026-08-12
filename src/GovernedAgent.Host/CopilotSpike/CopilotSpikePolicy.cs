using GitHub.Copilot;

namespace GovernedAgent.Host.CopilotSpike;

public static class CopilotSpikePolicy
{
    public static PreToolUseHookOutput Decide(string? toolName)
    {
        var allowed = string.Equals(
            toolName,
            CopilotSpikeConstants.DiagnosticTool,
            StringComparison.Ordinal);

        return new PreToolUseHookOutput
        {
            PermissionDecision = allowed ? "allow" : "deny",
            PermissionDecisionReason = allowed
                ? "Read-only application tool allowed by the local spike policy."
                : $"Tool '{toolName ?? "<missing>"}' is not allowed by the local spike policy."
        };
    }
}
