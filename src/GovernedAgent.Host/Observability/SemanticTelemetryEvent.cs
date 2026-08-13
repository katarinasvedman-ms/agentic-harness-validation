using System.Diagnostics;
using System.Text.Json;

namespace GovernedAgent.Host.Observability;

public static class SemanticEventNames
{
    public const string Request = "governed_agent.request";
    public const string AssistantTurnStart = "governed_agent.assistant.turn_start";
    public const string AssistantTurnEnd = "governed_agent.assistant.turn_end";
    public const string PlanVerification = "governed_agent.plan.verification";
    public const string PolicyDecision = "governed_agent.policy.decision";
    public const string ApprovalDecision = "governed_agent.approval.decision";
    public const string ToolExecution = "governed_agent.tool.execution";
    public const string OutcomeVerification = "governed_agent.outcome.verification";
    public const string SessionIdle = "governed_agent.session.idle";
    public const string Evaluation = "governed_agent.evaluation";
}

public sealed record SemanticTelemetryEvent(
    string Name,
    DateTimeOffset Timestamp,
    string CorrelationId,
    string? TraceId,
    string? SpanId,
    IReadOnlyDictionary<string, object?> Attributes)
{
    public const string SchemaVersion = "1.0";

    public IEnumerable<KeyValuePair<string, object?>> ToOpenTelemetryTags()
    {
        yield return new("event.name", Name);
        yield return new("event.schema_version", SchemaVersion);
        yield return new("event.timestamp", Timestamp.ToString("O"));
        yield return new("governed_agent.correlation_id", CorrelationId);

        if (!string.IsNullOrWhiteSpace(TraceId))
        {
            yield return new("trace.id", TraceId);
        }

        if (!string.IsNullOrWhiteSpace(SpanId))
        {
            yield return new("span.id", SpanId);
        }

        foreach (var attribute in Attributes.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            yield return new(attribute.Key, ToOpenTelemetryValue(attribute.Value));
        }
    }

    public static SemanticTelemetryEvent Create(
        string name,
        string correlationId,
        IReadOnlyDictionary<string, object?> metadata,
        DateTimeOffset? timestamp = null,
        Activity? activity = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentNullException.ThrowIfNull(metadata);

        var current = activity ?? Activity.Current;
        return new SemanticTelemetryEvent(
            name,
            timestamp ?? DateTimeOffset.UtcNow,
            correlationId,
            current?.TraceId.ToHexString(),
            current?.SpanId.ToHexString(),
            TelemetryRedactor.RedactMetadata(metadata));
    }

    private static object? ToOpenTelemetryValue(object? value) =>
        value switch
        {
            null or string or bool or byte or sbyte or short or ushort or int or uint or
                long or ulong or float or double or decimal => value,
            string[] or bool[] or int[] or long[] or double[] => value,
            _ => JsonSerializer.Serialize(value)
        };
}
