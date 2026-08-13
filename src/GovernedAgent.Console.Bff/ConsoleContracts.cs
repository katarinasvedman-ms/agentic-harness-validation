using GovernedAgent.Core.Contracts;
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

public sealed record KillSwitchMutation(bool Active, string Reason);

public sealed record ControlsView(
    bool KillSwitchActive,
    int MaximumToolCalls,
    int MaximumDurationSeconds);

public sealed record AuditView(
    bool IntegrityValid,
    IReadOnlyList<AuditRecord> Records);
