namespace GovernedAgent.Host.CopilotSpike;

public sealed class CopilotSpikeToolState
{
    private int _diagnosticCalls;
    private int _writeNoOpCalls;

    public int DiagnosticCalls => Volatile.Read(ref _diagnosticCalls);

    public int WriteNoOpCalls => Volatile.Read(ref _writeNoOpCalls);

    public object GetDiagnostic(string incidentId)
    {
        Interlocked.Increment(ref _diagnosticCalls);
        return new
        {
            incidentId,
            service = "payments-api",
            status = "degraded",
            evidence = "Error rate exceeded the local demonstration threshold."
        };
    }

    public object RestartServiceNoOp(string serviceId)
    {
        Interlocked.Increment(ref _writeNoOpCalls);
        return new
        {
            serviceId,
            status = "noop",
            message = "This spike tool must never be entered because policy denies it."
        };
    }
}
