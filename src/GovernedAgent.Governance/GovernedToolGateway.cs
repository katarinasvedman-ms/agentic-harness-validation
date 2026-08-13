using System.Text.Json;
using GovernedAgent.Core.Contracts;

namespace GovernedAgent.Governance;

public enum GatewayOutcome
{
    Executed,
    ApprovalRequired
}

public sealed record GovernedToolRequest(
    ActionPlan Plan,
    string StepId,
    TrustedActionEnvelope Envelope,
    string? ApprovalNonce,
    string IdempotencyKey,
    long ExpectedResourceVersion);

public sealed record GatewayResult(
    GatewayOutcome Outcome,
    PolicyDecision PolicyDecision,
    JsonElement? ToolResult,
    string ActionDigest);

public interface IGovernedToolExecutor
{
    void Validate(PlanStep step, long expectedResourceVersion);

    ValueTask<JsonElement> ExecuteAsync(
        PlanStep step,
        string idempotencyKey,
        long expectedResourceVersion,
        CancellationToken cancellationToken);
}

public sealed class GovernedToolGateway(
    IToolRegistry toolRegistry,
    ActionCanonicalizer canonicalizer,
    IPolicyEvaluator policyEvaluator,
    IApprovalStore approvalStore,
    IExecutionBudgetStore budgetStore,
    IKillSwitch killSwitch,
    IAuditChain auditChain,
    IGovernedToolExecutor executor,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async ValueTask<GatewayResult> ExecuteAsync(
        GovernedToolRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = _timeProvider.GetUtcNow();
        var actionDigest = request.Envelope.Action.ActionDigest;
        PolicyDecision? evaluatedPolicy = null;
        var denialAudited = false;

        try
        {
            var step = ResolveStep(request.Plan, request.StepId);
            ValidatePlan(request.Plan, now);
            var digest = canonicalizer.CreateDigest(request.Plan, step);
            actionDigest = digest.Value;
            ValidateEnvelope(request, step, digest.Value);
            ValidateExecutionMetadata(request, step);
            executor.Validate(step, request.ExpectedResourceVersion);

            if (!toolRegistry.TryGet(step.Tool, out var tool))
            {
                throw Error(
                    ErrorCategory.UnknownTool,
                    "unknown_tool",
                    $"Tool '{step.Tool}' is not registered.");
            }

            var approvalRequired = RequiresApproval(step);
            var approvalRequest = CreateApprovalRequest(request, step, digest.Value, now);
            var hasApproval = !approvalRequired;
            if (killSwitch.IsActive)
            {
                var killSwitchPolicy = await EvaluatePolicyAsync(
                    request,
                    tool,
                    budgetAvailable: true,
                    hasApproval: false,
                    cancellationToken);
                evaluatedPolicy = killSwitchPolicy;
                AppendAudit(
                    request,
                    digest.Value,
                    killSwitchPolicy,
                    ExecutionState.Denied,
                    now);
                denialAudited = true;
                throw Error(
                    ErrorCategory.PolicyDenied,
                    killSwitchPolicy.ReasonCode,
                    "The governed gateway denied the action.");
            }

            if (approvalRequired)
            {
                if (string.IsNullOrWhiteSpace(request.ApprovalNonce))
                {
                    var approvalPolicy = await EvaluatePolicyAsync(
                        request,
                        tool,
                        budgetAvailable: true,
                        hasApproval: false,
                        cancellationToken);
                    evaluatedPolicy = approvalPolicy;
                    AppendAudit(
                        request,
                        digest.Value,
                        approvalPolicy,
                        ExecutionState.AwaitingApproval,
                        now);
                    return new GatewayResult(
                        GatewayOutcome.ApprovalRequired,
                        approvalPolicy,
                        ToolResult: null,
                        digest.Value);
                }

                hasApproval = approvalStore.IsValid(
                    request.ApprovalNonce,
                    approvalRequest);
                if (!hasApproval)
                {
                    throw Error(
                        ErrorCategory.ApprovalInvalid,
                        "approval_invalid",
                        "The exact approval is invalid, expired, revoked, or already consumed.");
                }
            }

            var budgetAvailable = budgetStore.TryConsumeToolCall(
                request.Envelope.Session.Id,
                now);
            var policy = await policyEvaluator.EvaluateAsync(
                new PolicyEvaluationContext(
                    request.Envelope,
                    tool,
                    killSwitch.IsActive,
                    budgetAvailable,
                    hasApproval),
                cancellationToken);
            evaluatedPolicy = policy;

            if (policy.Decision == GovernanceDecision.RequireApproval)
            {
                AppendAudit(
                    request,
                    digest.Value,
                    policy,
                    ExecutionState.AwaitingApproval,
                    now);
                return new GatewayResult(
                    GatewayOutcome.ApprovalRequired,
                    policy,
                    ToolResult: null,
                    digest.Value);
            }

            if (policy.Decision != GovernanceDecision.Allow)
            {
                AppendAudit(
                    request,
                    digest.Value,
                    policy,
                    ExecutionState.Denied,
                    now);
                denialAudited = true;
                throw Error(
                    ErrorCategory.PolicyDenied,
                    policy.ReasonCode,
                    "The governed gateway denied the action.");
            }

            if (approvalRequired &&
                !approvalStore.TryConsume(
                    request.ApprovalNonce!,
                    approvalRequest,
                    out _))
            {
                throw Error(
                    ErrorCategory.ApprovalInvalid,
                    "approval_invalid",
                    "The exact approval changed before it could be consumed.");
            }

            AppendAudit(
                request,
                digest.Value,
                policy,
                ExecutionState.Executing,
                now);
            var result = await executor.ExecuteAsync(
                step,
                request.IdempotencyKey,
                request.ExpectedResourceVersion,
                cancellationToken);
            AppendAudit(
                request,
                digest.Value,
                policy,
                ExecutionState.Completed,
                _timeProvider.GetUtcNow());

            return new GatewayResult(
                GatewayOutcome.Executed,
                policy,
                result,
                digest.Value);
        }
        catch (GovernanceException exception)
        {
            if (!denialAudited)
            {
                var denial = evaluatedPolicy ?? new PolicyDecision(
                    GovernanceDecision.Deny,
                    exception.Code,
                    "GATEWAY",
                    "1.0",
                    _timeProvider.GetUtcNow());
                AppendAudit(
                    request,
                    actionDigest,
                    denial,
                    ExecutionState.Denied,
                    _timeProvider.GetUtcNow());
            }

            throw;
        }
    }

    private ValueTask<PolicyDecision> EvaluatePolicyAsync(
        GovernedToolRequest request,
        ToolMetadata tool,
        bool budgetAvailable,
        bool hasApproval,
        CancellationToken cancellationToken) =>
        policyEvaluator.EvaluateAsync(
            new PolicyEvaluationContext(
                request.Envelope,
                tool,
                killSwitch.IsActive,
                budgetAvailable,
                hasApproval),
            cancellationToken);

    private static bool RequiresApproval(PlanStep step) =>
        step.Resource.Environment == TargetEnvironment.Production &&
        step.Effect == EffectKind.Write;

    private static ApprovalConsumptionRequest CreateApprovalRequest(
        GovernedToolRequest request,
        PlanStep step,
        string actionDigest,
        DateTimeOffset now) =>
        new(
            request.Plan.PlanId,
            step.StepId,
            actionDigest,
            step.Resource.Id,
            step.Resource.Environment,
            "incident-commander",
            "1.0",
            now);

    private static PlanStep ResolveStep(ActionPlan plan, string stepId)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(stepId);

        var matches = plan.Steps
            .Where(step => string.Equals(step.StepId, stepId, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw Error(
                ErrorCategory.Validation,
                "step_not_unique",
                "The requested step must exist exactly once in the plan.");
    }

    private static void ValidatePlan(ActionPlan plan, DateTimeOffset now)
    {
        if (!string.Equals(plan.SchemaVersion, "1.0", StringComparison.Ordinal))
        {
            throw Error(
                ErrorCategory.Validation,
                "unsupported_plan_schema",
                $"Plan schema '{plan.SchemaVersion}' is not supported.");
        }

        if (plan.ExpiresAt <= now || plan.CreatedAt > now)
        {
            throw Error(
                ErrorCategory.Validation,
                "plan_not_current",
                "The plan is expired or not yet valid.");
        }
    }

    private static void ValidateEnvelope(
        GovernedToolRequest request,
        PlanStep step,
        string digest)
    {
        var action = request.Envelope.Action;
        if (request.Envelope.Verification.Result != VerificationResult.Verified ||
            action.PlanId != request.Plan.PlanId ||
            !string.Equals(action.StepId, step.StepId, StringComparison.Ordinal) ||
            !string.Equals(action.Tool, step.Tool, StringComparison.Ordinal) ||
            !string.Equals(action.Capability, step.Capability, StringComparison.Ordinal) ||
            action.Effect != step.Effect ||
            !string.Equals(action.Resource.Id, step.Resource.Id, StringComparison.Ordinal) ||
            action.Resource.Environment != step.Resource.Environment ||
            !string.Equals(action.ActionDigest, digest, StringComparison.Ordinal))
        {
            throw Error(
                ErrorCategory.Validation,
                "trusted_envelope_mismatch",
                "The trusted action envelope does not match the canonical plan step.");
        }
    }

    private static void ValidateExecutionMetadata(
        GovernedToolRequest request,
        PlanStep step)
    {
        if (step.Effect is not EffectKind.Write and not EffectKind.Delete)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw Error(
                ErrorCategory.Validation,
                "invalid_idempotency_key",
                "Write actions require a non-empty idempotency key.");
        }

        if (request.ExpectedResourceVersion < 0)
        {
            throw Error(
                ErrorCategory.Validation,
                "invalid_expected_resource_version",
                "Write actions require a non-negative expected resource version.");
        }
    }

    private void AppendAudit(
        GovernedToolRequest request,
        string actionDigest,
        PolicyDecision policy,
        ExecutionState executionState,
        DateTimeOffset timestamp)
    {
        auditChain.Append(new AuditRecord(
            Guid.NewGuid(),
            request.Envelope.RequestId,
            request.Envelope.RequestId.ToString("D"),
            request.Envelope.Session.IncidentId,
            request.Plan.PlanId,
            request.StepId,
            actionDigest,
            policy.Decision,
            policy.PolicyVersion,
            request.Envelope.Verification.Result,
            executionState,
            timestamp,
            PreviousRecordHash: null,
            RecordHash: string.Empty));
    }

    private static GovernanceException Error(
        ErrorCategory category,
        string code,
        string message) =>
        new(category, code, message);
}
