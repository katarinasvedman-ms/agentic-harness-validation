using GovernedAgent.Core.Contracts;

namespace GovernedAgent.Governance;

public sealed class GovernanceException(
    ErrorCategory category,
    string code,
    string message) : Exception(message)
{
    public ErrorCategory Category { get; } = category;

    public string Code { get; } = code;
}
