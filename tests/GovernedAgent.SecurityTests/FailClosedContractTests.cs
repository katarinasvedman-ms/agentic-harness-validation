using GovernedAgent.Core.Contracts;
using GovernedAgent.Host.CopilotSpike;

namespace GovernedAgent.SecurityTests;

public sealed class FailClosedContractTests
{
    [Theory]
    [InlineData(VerificationResult.Rejected)]
    [InlineData(VerificationResult.Indeterminate)]
    public void NonVerifiedResultsAreNeverVerified(VerificationResult result)
    {
        Assert.NotEqual(VerificationResult.Verified, result);
    }

    [Theory]
    [InlineData(CopilotSpikeConstants.WriteNoOpTool)]
    [InlineData("shell")]
    [InlineData("read_file")]
    [InlineData("mcp-exfiltrate")]
    [InlineData(null)]
    public void CopilotPreToolPolicyDeniesEverythingExceptTheDiagnosticTool(string? toolName)
    {
        var decision = CopilotSpikePolicy.Decide(toolName);

        Assert.Equal("deny", decision.PermissionDecision);
    }

    [Fact]
    public void CopilotPreToolPolicyAllowsOnlyTheReadDiagnosticTool()
    {
        var decision = CopilotSpikePolicy.Decide(CopilotSpikeConstants.DiagnosticTool);

        Assert.Equal("allow", decision.PermissionDecision);
    }

    [Fact]
    public void DeniedWriteDoesNotEnterItsHandler()
    {
        var state = new CopilotSpikeToolState();
        var decision = CopilotSpikePolicy.Decide(CopilotSpikeConstants.WriteNoOpTool);

        if (decision.PermissionDecision == "allow")
        {
            state.RestartServiceNoOp("payments-api");
        }

        Assert.Equal(0, state.WriteNoOpCalls);
    }

    [Fact]
    public void CopilotSessionExposesOnlyApplicationOwnedTools()
    {
        var config = CopilotSpikeConfiguration.CreateSession(new CopilotSpikeToolState());

        Assert.Equal(
            [
                $"custom:{CopilotSpikeConstants.DiagnosticTool}",
                $"custom:{CopilotSpikeConstants.WriteNoOpTool}"
            ],
            config.AvailableTools);
        var excludedTools = config.ExcludedTools
            ?? throw new Xunit.Sdk.XunitException("Excluded tools must be configured.");
        Assert.Contains("builtin:*", excludedTools);
        Assert.Contains("mcp:*", excludedTools);
        Assert.False(config.EnableConfigDiscovery);
        Assert.False(config.EnableFileHooks);
        Assert.False(config.EnableHostGitOperations);
        Assert.False(config.EnableSessionStore);
        Assert.False(config.EnableSkills);
        Assert.NotNull(config.Hooks?.OnPreToolUse);
        Assert.NotNull(config.OnPermissionRequest);
    }
}
