using System.Collections.Concurrent;
using System.Security.Cryptography;
using GovernedAgent.Host.Workflow;

namespace GovernedAgent.Host.Hosted;

public interface IWorkflowSuspensionStore
{
    string Store(string userId, AgentWorkflowSuspension suspension);

    ValueTask<WorkflowSuspensionLease?> AcquireAsync(
        string resumeToken,
        string userId,
        CancellationToken cancellationToken);
}

public sealed class InMemoryWorkflowSuspensionStore : IWorkflowSuspensionStore
{
    private readonly ConcurrentDictionary<string, Entry> _entries =
        new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private readonly TimeProvider _timeProvider;
    private readonly int _capacity;
    private readonly TimeSpan _maximumTtl;

    public InMemoryWorkflowSuspensionStore(
        TimeProvider? timeProvider = null,
        int capacity = 1_000,
        TimeSpan? maximumTtl = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _capacity = capacity;
        _maximumTtl = maximumTtl ?? TimeSpan.FromMinutes(15);
        if (_maximumTtl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumTtl));
        }
    }

    public string Store(string userId, AgentWorkflowSuspension suspension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(suspension);

        var now = _timeProvider.GetUtcNow();
        var expiresAt = suspension.Request.Plan.ExpiresAt < now + _maximumTtl
            ? suspension.Request.Plan.ExpiresAt
            : now + _maximumTtl;
        if (expiresAt <= now)
        {
            throw new WorkflowSuspensionExpiredException();
        }

        lock (_sync)
        {
            CleanupExpired(now);
            if (_entries.Count >= _capacity)
            {
                throw new WorkflowSuspensionCapacityException();
            }

            while (true)
            {
                var resumeToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                if (_entries.TryAdd(
                        resumeToken,
                        new Entry(userId, suspension, expiresAt)))
                {
                    return resumeToken;
                }
            }
        }
    }

    public async ValueTask<WorkflowSuspensionLease?> AcquireAsync(
        string resumeToken,
        string userId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resumeToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        Entry? entry;
        lock (_sync)
        {
            CleanupExpired(_timeProvider.GetUtcNow());
            if (!_entries.TryGetValue(resumeToken, out entry))
            {
                return null;
            }
        }

        await entry.Gate.WaitAsync(cancellationToken);
        if (!_entries.TryGetValue(resumeToken, out var current) ||
            !ReferenceEquals(entry, current) ||
            entry.ExpiresAt <= _timeProvider.GetUtcNow() ||
            !string.Equals(entry.UserId, userId, StringComparison.Ordinal))
        {
            if (entry.ExpiresAt <= _timeProvider.GetUtcNow())
            {
                _entries.TryRemove(new KeyValuePair<string, Entry>(resumeToken, entry));
            }
            entry.Gate.Release();
            return null;
        }

        return new WorkflowSuspensionLease(
            entry.Suspension,
            replacement =>
            {
                if (replacement is null)
                {
                    _entries.TryRemove(
                        new KeyValuePair<string, Entry>(resumeToken, entry));
                }
                else
                {
                    entry.Suspension = replacement;
                }

                entry.Gate.Release();
            });
    }

    private void CleanupExpired(DateTimeOffset now)
    {
        foreach (var pair in _entries)
        {
            if (pair.Value.ExpiresAt <= now)
            {
                _entries.TryRemove(pair);
            }
        }
    }

    private sealed class Entry(
        string userId,
        AgentWorkflowSuspension suspension,
        DateTimeOffset expiresAt)
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public string UserId { get; } = userId;

        public DateTimeOffset ExpiresAt { get; } = expiresAt;

        public AgentWorkflowSuspension Suspension { get; set; } = suspension;
    }
}

public sealed class WorkflowSuspensionCapacityException : Exception;

public sealed class WorkflowSuspensionExpiredException : Exception;

public sealed class WorkflowSuspensionLease : IAsyncDisposable
{
    private readonly Action<AgentWorkflowSuspension?> _release;
    private AgentWorkflowSuspension? _replacement;
    private int _disposed;

    internal WorkflowSuspensionLease(
        AgentWorkflowSuspension suspension,
        Action<AgentWorkflowSuspension?> release)
    {
        Suspension = suspension;
        _release = release;
    }

    public AgentWorkflowSuspension Suspension { get; }

    public void Replace(AgentWorkflowSuspension suspension) =>
        _replacement = suspension;

    public void Retain() => _replacement = Suspension;

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _release(_replacement);
        }

        return ValueTask.CompletedTask;
    }
}
