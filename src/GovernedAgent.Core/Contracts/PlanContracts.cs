using System.Text.Json;
using System.Text.Json.Serialization;

namespace GovernedAgent.Core.Contracts;

public sealed record ResourceReference(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("environment")] TargetEnvironment Environment,
    [property: JsonPropertyName("classification")] DataClassification Classification);

public sealed record DataSourceReference(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("classification")] DataClassification Classification);

public sealed record DestinationReference(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("classification")] DataClassification Classification);

public sealed record CompensationAction(
    [property: JsonPropertyName("tool")] string Tool,
    [property: JsonPropertyName("arguments")] IReadOnlyDictionary<string, JsonElement> Arguments);

public sealed record PlanStep(
    [property: JsonPropertyName("stepId")] string StepId,
    [property: JsonPropertyName("capability")] string Capability,
    [property: JsonPropertyName("tool")] string Tool,
    [property: JsonPropertyName("resource")] ResourceReference Resource,
    [property: JsonPropertyName("dataSources")] IReadOnlyList<DataSourceReference> DataSources,
    [property: JsonPropertyName("destination")] DestinationReference Destination,
    [property: JsonPropertyName("arguments")] IReadOnlyDictionary<string, JsonElement> Arguments,
    [property: JsonPropertyName("dependsOn")] IReadOnlyList<string> DependsOn,
    [property: JsonPropertyName("effect")] EffectKind Effect,
    [property: JsonPropertyName("approvalClass")] ApprovalClass ApprovalClass,
    [property: JsonPropertyName("compensation")] CompensationAction? Compensation);

public sealed record ActionPlan(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("planId")] Guid PlanId,
    [property: JsonPropertyName("incidentId")] string IncidentId,
    [property: JsonPropertyName("agentId")] string AgentId,
    [property: JsonPropertyName("deploymentVersion")] string DeploymentVersion,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("expiresAt")] DateTimeOffset ExpiresAt,
    [property: JsonPropertyName("steps")] IReadOnlyList<PlanStep> Steps);
