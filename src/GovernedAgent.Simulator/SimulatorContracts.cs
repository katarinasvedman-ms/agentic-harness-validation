namespace GovernedAgent.Simulator;

public enum IncidentStatus
{
    Open,
    Mitigating,
    Resolved
}

public enum ServiceHealth
{
    Healthy,
    Degraded,
    Restarting
}

public sealed record IncidentSnapshot(
    string IncidentId,
    string ServiceId,
    string Title,
    IncidentStatus Status,
    int Severity,
    long Version,
    DateTimeOffset UpdatedAt);

public sealed record MetricSample(
    string Name,
    double Value,
    string Unit,
    DateTimeOffset Timestamp);

public sealed record LogEntry(
    DateTimeOffset Timestamp,
    string Level,
    string Message,
    bool ContainsUntrustedContent);

public sealed record ServiceInstanceSnapshot(
    string InstanceId,
    ServiceHealth Health,
    long RestartCount,
    DateTimeOffset UpdatedAt);

public sealed record ServiceHealthSnapshot(
    string ServiceId,
    ServiceHealth Health,
    long Version,
    IReadOnlyList<ServiceInstanceSnapshot> Instances);

public sealed record SimulatorWriteResult<T>(
    T Value,
    bool Replayed,
    long Version);

public sealed record ServiceStateCheckpoint(
    string ServiceId,
    string InstanceId,
    ServiceHealth PreviousHealth,
    long SourceVersion);

public sealed class SimulatorConcurrencyException(
    long expectedVersion,
    long actualVersion) : Exception(
        $"Simulator version conflict: expected {expectedVersion}, actual {actualVersion}.")
{
    public long ExpectedVersion { get; } = expectedVersion;

    public long ActualVersion { get; } = actualVersion;
}

public interface IIncidentSimulator
{
    IncidentSnapshot GetIncident(string incidentId);

    IReadOnlyList<MetricSample> QueryMetrics(string serviceId);

    IReadOnlyList<LogEntry> QueryLogs(string serviceId);

    ServiceHealthSnapshot GetServiceHealth(string serviceId);

    SimulatorWriteResult<IncidentSnapshot> UpdateIncident(
        string incidentId,
        IncidentStatus status,
        long expectedVersion,
        string idempotencyKey);

    SimulatorWriteResult<ServiceStateCheckpoint> RestartService(
        string serviceId,
        string instanceId,
        long expectedVersion,
        string idempotencyKey);

    SimulatorWriteResult<ServiceHealthSnapshot> RestoreServiceState(
        ServiceStateCheckpoint checkpoint,
        long expectedVersion,
        string idempotencyKey);

    void Reset();
}
