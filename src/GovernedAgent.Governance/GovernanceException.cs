using GovernedAgent.Core.Contracts;

namespace GovernedAgent.Governance;

public sealed class GovernanceException(
    ErrorCategory category,
    string code,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public ErrorCategory Category { get; } = category;

    public string Code { get; } = code;
}
