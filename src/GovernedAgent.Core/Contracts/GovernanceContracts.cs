using System.Text.Json.Serialization;

namespace GovernedAgent.Core.Contracts;

public sealed record UserIdentity(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("roles")] IReadOnlyList<string> Roles);

public sealed record AgentIdentity(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("identity")] string Identity,
    [property: JsonPropertyName("deploymentVersion")] string DeploymentVersion);

public sealed record SessionIdentity(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("incidentId")] string IncidentId);

public sealed record ActionResource(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("environment")] TargetEnvironment Environment);

public sealed record GovernedAction(
    [property: JsonPropertyName("planId")] Guid PlanId,
    [property: JsonPropertyName("stepId")] string StepId,
    [property: JsonPropertyName("tool")] string Tool,
    [property: JsonPropertyName("capability")] string Capability,
    [property: JsonPropertyName("effect")] EffectKind Effect,
    [property: JsonPropertyName("resource")] ActionResource Resource,
    [property: JsonPropertyName("actionDigest")] string ActionDigest);

public sealed record VerificationAttestation(
    [property: JsonPropertyName("result")] VerificationResult Result,
    [property: JsonPropertyName("specificationVersion")] string SpecificationVersion,
    [property: JsonPropertyName("verifierVersion")] string VerifierVersion,
    [property: JsonPropertyName("planDigest")] string PlanDigest);

public sealed record TrustedActionEnvelope(
    [property: JsonPropertyName("envelopeVersion")] string EnvelopeVersion,
    [property: JsonPropertyName("requestId")] Guid RequestId,
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp,
    [property: JsonPropertyName("user")] UserIdentity User,
    [property: JsonPropertyName("agent")] AgentIdentity Agent,
    [property: JsonPropertyName("session")] SessionIdentity Session,
    [property: JsonPropertyName("action")] GovernedAction Action,
    [property: JsonPropertyName("verification")] VerificationAttestation Verification);

public sealed record ApprovalArtifact(
    [property: JsonPropertyName("approvalId")] Guid ApprovalId,
    [property: JsonPropertyName("approverId")] string ApproverId,
    [property: JsonPropertyName("approverRoles")] IReadOnlyList<string> ApproverRoles,
    [property: JsonPropertyName("planId")] Guid PlanId,
    [property: JsonPropertyName("stepId")] string StepId,
    [property: JsonPropertyName("actionDigest")] string ActionDigest,
    [property: JsonPropertyName("resourceId")] string ResourceId,
    [property: JsonPropertyName("environment")] TargetEnvironment Environment,
    [property: JsonPropertyName("decision")] ApprovalDecision Decision,
    [property: JsonPropertyName("issuedAt")] DateTimeOffset IssuedAt,
    [property: JsonPropertyName("expiresAt")] DateTimeOffset ExpiresAt,
    [property: JsonPropertyName("nonce")] string Nonce,
    [property: JsonPropertyName("policyVersion")] string PolicyVersion);

public sealed record PolicyDecision(
    [property: JsonPropertyName("decision")] GovernanceDecision Decision,
    [property: JsonPropertyName("reasonCode")] string ReasonCode,
    [property: JsonPropertyName("ruleId")] string RuleId,
    [property: JsonPropertyName("policyVersion")] string PolicyVersion,
    [property: JsonPropertyName("evaluatedAt")] DateTimeOffset EvaluatedAt);

public sealed record ToolMetadata(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("capability")] string Capability,
    [property: JsonPropertyName("effect")] EffectKind Effect,
    [property: JsonPropertyName("environments")] IReadOnlyList<TargetEnvironment> Environments,
    [property: JsonPropertyName("maximumInputClassification")] DataClassification MaximumInputClassification,
    [property: JsonPropertyName("approvalClass")] ApprovalClass ApprovalClass,
    [property: JsonPropertyName("inputSchemaDigest")] string InputSchemaDigest,
    [property: JsonPropertyName("outputSchemaDigest")] string OutputSchemaDigest);

public sealed record AuditRecord(
    [property: JsonPropertyName("recordId")] Guid RecordId,
    [property: JsonPropertyName("requestId")] Guid RequestId,
    [property: JsonPropertyName("correlationId")] string CorrelationId,
    [property: JsonPropertyName("incidentId")] string IncidentId,
    [property: JsonPropertyName("planId")] Guid PlanId,
    [property: JsonPropertyName("stepId")] string StepId,
    [property: JsonPropertyName("actionDigest")] string ActionDigest,
    [property: JsonPropertyName("decision")] GovernanceDecision Decision,
    [property: JsonPropertyName("policyVersion")] string PolicyVersion,
    [property: JsonPropertyName("verificationResult")] VerificationResult VerificationResult,
    [property: JsonPropertyName("executionState")] ExecutionState ExecutionState,
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp,
    [property: JsonPropertyName("previousRecordHash")] string? PreviousRecordHash,
    [property: JsonPropertyName("recordHash")] string RecordHash);

public sealed record GovernedError(
    [property: JsonPropertyName("category")] ErrorCategory Category,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("retryable")] bool Retryable);
