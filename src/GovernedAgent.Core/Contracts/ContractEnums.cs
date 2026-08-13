namespace GovernedAgent.Core.Contracts;

public enum EffectKind
{
    Read,
    Write,
    Delete
}

public enum TargetEnvironment
{
    Development,
    Test,
    Production
}

public enum DataClassification
{
    Public,
    Internal,
    InternalTrusted,
    Confidential,
    Restricted
}

public enum ApprovalClass
{
    None,
    PolicyDependent,
    IncidentCommander
}

public enum GovernanceDecision
{
    Allow,
    Deny,
    RequireApproval
}

public enum ApprovalDecision
{
    Approved,
    Rejected
}

public enum VerificationResult
{
    Verified,
    Rejected,
    Indeterminate
}

public enum ExecutionState
{
    Draft,
    Verified,
    AwaitingApproval,
    Approved,
    Executing,
    Completed,
    Denied,
    Failed,
    Compensating,
    Compensated
}

public enum ErrorCategory
{
    Validation,
    UnknownTool,
    PolicyDenied,
    PolicyUnavailable,
    VerificationRejected,
    VerificationUnavailable,
    ApprovalRequired,
    ApprovalInvalid,
    BudgetExceeded,
    KillSwitchActive,
    AuditUnavailable,
    Conflict,
    DependencyFailure,
    ToolFailure,
    Timeout,
    Cancelled
}
