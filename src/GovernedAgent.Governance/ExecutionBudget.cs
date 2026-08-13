using System.Collections.Concurrent;

namespace GovernedAgent.Governance;

public sealed record ExecutionBudgetLimits(
    int MaximumToolCalls,
    TimeSpan MaximumDuration)
{
    public static ExecutionBudgetLimits LocalDefault { get; } =
        new(MaximumToolCalls: 12, MaximumDuration: TimeSpan.FromMinutes(3));
}

public interface IExecutionBudgetStore
{
    bool TryConsumeToolCall(string sessionId, DateTimeOffset now);
}

public sealed class InMemoryExecutionBudgetStore(
    ExecutionBudgetLimits limits) : IExecutionBudgetStore
{
    private readonly ConcurrentDictionary<string, BudgetState> _states =
        new(StringComparer.Ordinal);

    public bool TryConsumeToolCall(string sessionId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var state = _states.GetOrAdd(sessionId, _ => new BudgetState(now));
        lock (state)
        {
            if (now - state.StartedAt > limits.MaximumDuration ||
                state.ToolCalls >= limits.MaximumToolCalls)
            {
                return false;
            }

            state.ToolCalls++;
            return true;
        }
    }

    private sealed class BudgetState(DateTimeOffset startedAt)
    {
        public DateTimeOffset StartedAt { get; } = startedAt;

        public int ToolCalls { get; set; }
    }
}
