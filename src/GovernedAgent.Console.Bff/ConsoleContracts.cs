using System.Text.Json;
using GovernedAgent.Core.Contracts;
using GovernedAgent.Governance;
using GovernedAgent.Simulator;

namespace GovernedAgent.Console.Bff;

public sealed record IncidentConsoleState(
    IncidentSnapshot Incident,
    ServiceHealthSnapshot ServiceHealth);

public sealed record TimelineEntry(
    string Id,
    DateTimeOffset Timestamp,
    string Kind,
    string Summary);

public sealed record EvidenceItem(
    string Id,
    string Kind,
    DateTimeOffset Timestamp,
    string Summary,
    bool ContainsUntrustedContent);

public sealed record VerificationView(
    ActionPlan Plan,
    VerificationResult Result,
    string SpecificationVersion,
    string VerifierVersion,
    string PlanDigest,
    IReadOnlyList<string> Findings);

public sealed record PendingApprovalView(
    Guid ApprovalRequestId,
    Guid PlanId,
    string StepId,
    string ActionDigest,
    string ResourceId,
    TargetEnvironment Environment,
    string RequiredRole,
    string PolicyVersion,
    DateTimeOffset ExpiresAt);

public sealed record ApprovalMutation(string Reason);

public sealed record ApprovalMutationResult(
    Guid ApprovalRequestId,
    ApprovalDecision Decision,
    string ActorId,
    DateTimeOffset DecidedAt,
    string? ApprovalNonce);

public sealed record ExecutionMutation(string ApprovalNonce);

public sealed record CapabilityLeaseView(
    Guid LeaseId,
    string AgentId,
    string SessionId,
    Guid PlanId,
    string StepId,
    string ActionDigest,
    IntentClass Intent,
    string Capability,
    string Tool,
    EffectKind Effect,
    string ResourceId,
    TargetEnvironment Environment,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    int MaximumUses,
    int ConsumedUses,
    CapabilityLeaseState State,
    string? RevocationReason);

public sealed record CapabilityLeasesView(
    string IntentSource,
    string PromptAssessmentRole,
    IReadOnlyList<CapabilityLeaseView> Leases);

public sealed record GovernedExecutionView(
    GatewayOutcome Outcome,
    GovernanceDecision Decision,
    string ActionDigest,
    JsonElement? ToolResult,
    CapabilityLeaseView CapabilityLease);

public sealed record ContainmentMutation(bool Active, string Reason);

public sealed record ReattestationMutation(
    string ArtifactDigest,
    string KnownGoodVersion,
    string Reason);

public sealed record RecoveryMutation(
    string RootCause,
    string Reason);

public sealed record ControlsView(
    ContainmentMode Mode,
    bool KillSwitchActive,
    int MaximumToolCalls,
    int MaximumDurationSeconds,
    ReattestationArtifact? Reattestation,
    RecoverySignOff? SignOff);

public sealed record AuditView(
    bool IntegrityValid,
    IReadOnlyList<AuditRecord> Records);
