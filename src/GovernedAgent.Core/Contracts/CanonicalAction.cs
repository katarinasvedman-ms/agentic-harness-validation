using System.Text.Json;
using System.Text.Json.Serialization;

namespace GovernedAgent.Core.Contracts;

public sealed record CanonicalAction(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("planId")] Guid PlanId,
    [property: JsonPropertyName("stepId")] string StepId,
    [property: JsonPropertyName("tool")] string Tool,
    [property: JsonPropertyName("toolVersion")] string ToolVersion,
    [property: JsonPropertyName("capability")] string Capability,
    [property: JsonPropertyName("effect")] EffectKind Effect,
    [property: JsonPropertyName("resource")] ResourceReference Resource,
    [property: JsonPropertyName("dataSources")] IReadOnlyList<DataSourceReference> DataSources,
    [property: JsonPropertyName("destination")] DestinationReference Destination,
    [property: JsonPropertyName("arguments")] IReadOnlyDictionary<string, JsonElement> Arguments,
    [property: JsonPropertyName("approvalClass")] ApprovalClass ApprovalClass);

public sealed record ActionDigestResult(
    [property: JsonPropertyName("algorithm")] string Algorithm,
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("canonicalJson")] string CanonicalJson);
