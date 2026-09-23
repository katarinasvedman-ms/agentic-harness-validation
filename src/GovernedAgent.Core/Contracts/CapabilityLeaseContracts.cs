using System.Text.Json.Serialization;

namespace GovernedAgent.Core.Contracts;

public sealed record CapabilityLeaseArtifact(
    [property: JsonPropertyName("leaseId")] Guid LeaseId,
    [property: JsonPropertyName("nonce")] string Nonce,
    [property: JsonPropertyName("agentId")] string AgentId,
    [property: JsonPropertyName("deploymentVersion")] string DeploymentVersion,
    [property: JsonPropertyName("userId")] string UserId,
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("incidentId")] string IncidentId,
    [property: JsonPropertyName("planId")] Guid PlanId,
    [property: JsonPropertyName("stepId")] string StepId,
    [property: JsonPropertyName("planDigest")] string PlanDigest,
    [property: JsonPropertyName("actionDigest")] string ActionDigest,
    [property: JsonPropertyName("intent")] IntentClass Intent,
    [property: JsonPropertyName("capability")] string Capability,
    [property: JsonPropertyName("tool")] string Tool,
    [property: JsonPropertyName("effect")] EffectKind Effect,
    [property: JsonPropertyName("resourceId")] string ResourceId,
    [property: JsonPropertyName("environment")] TargetEnvironment Environment,
    [property: JsonPropertyName("policyVersion")] string PolicyVersion,
    [property: JsonPropertyName("verifierVersion")] string VerifierVersion,
    [property: JsonPropertyName("issuedAt")] DateTimeOffset IssuedAt,
    [property: JsonPropertyName("expiresAt")] DateTimeOffset ExpiresAt,
    [property: JsonPropertyName("maximumUses")] int MaximumUses,
    [property: JsonPropertyName("consumedUses")] int ConsumedUses,
    [property: JsonPropertyName("state")] CapabilityLeaseState State,
    [property: JsonPropertyName("revocationReason")] string? RevocationReason);
