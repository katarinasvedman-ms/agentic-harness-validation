using System.Security.Cryptography;
using System.Text;
using GovernedAgent.Core.Contracts;
using GovernedAgent.Core.Serialization;
using GovernedAgent.Governance;
using GovernedAgent.Host.Verification;

namespace GovernedAgent.Host.Workflow;

public sealed class LocalDeterministicAgentWorkflow(
    IPlanVerifier planVerifier,
    ActionCanonicalizer canonicalizer,
    GovernedToolGateway gateway,
    IWorkflowCompletionEvaluator completionEvaluator,
    IReadOnlyList<string> agentCapabilities,
    IReadOnlyDictionary<string, VerifierToolMetadata> verifierToolRegistry,
    int maximumSteps = 8,
    string specificationVersion = "1.0",
    string verifierVersion = "0.1.0",
    TimeProvider? timeProvider = null) : IAgentWorkflow
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async ValueTask<AgentWorkflowResult> ExecuteAsync(
        AgentWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string planDigest;
        try
        {
            planDigest = CreatePlanDigest(request.Plan);
        }
        catch (ArgumentException)
        {
            return Failure("invalid_workflow_request", ErrorCategory.Validation);
        }

        PlanVerificationDecision decision;
        try
        {
            decision = await planVerifier.VerifyAsync(
                new PlanVerificationRequest(
                    request.Plan,
                    _timeProvider.GetUtcNow(),
                    maximumSteps,
                    agentCapabilities,
                    verifierToolRegistry,
                    planDigest,
                    specificationVersion,
                    verifierVersion),
                cancellationToken);
        }
        catch (GovernanceException exception)
        {
            return Failure(exception.Code, exception.Category);
        }

        if (decision.Status != VerificationResult.Verified)
        {
            return Failure(
                decision.ReasonCodes.FirstOrDefault() ?? "plan_verification_rejected",
                ErrorCategory.VerificationRejected);
        }

        if (!string.Equals(decision.PlanDigest, planDigest, StringComparison.Ordinal) ||
            !string.Equals(
                decision.SpecificationVersion,
                specificationVersion,
                StringComparison.Ordinal) ||
            !string.Equals(decision.VerifierVersion, verifierVersion, StringComparison.Ordinal))
        {
            return Failure("verification_attestation_mismatch", ErrorCategory.VerificationRejected);
        }

        try
        {
            var step = ResolveStep(request.Plan, request.StepId);
            ValidateIdentityBinding(request);
            var actionDigest = canonicalizer.CreateDigest(request.Plan, step).Value;
            var envelope = CreateEnvelope(request, step, actionDigest, decision);
            return await ExecuteGatewayAsync(
                request,
                envelope,
                planDigest,
                actionDigest,
                approvalNonce: null,
                cancellationToken);
        }
        catch (GovernanceException exception)
        {
            return Failure(exception.Code, exception.Category);
        }
        catch (ArgumentException)
        {
            return Failure("invalid_workflow_request", ErrorCategory.Validation);
        }
    }

    public async ValueTask<AgentWorkflowResult> ResumeAsync(
        AgentWorkflowSuspension suspension,
        string approvalNonce,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(suspension);
        if (string.IsNullOrWhiteSpace(approvalNonce))
        {
            return Failure("approval_invalid", ErrorCategory.ApprovalInvalid);
        }

        try
        {
            var step = ResolveStep(suspension.Request.Plan, suspension.Request.StepId);
            var planDigest = CreatePlanDigest(suspension.Request.Plan);
            var actionDigest = canonicalizer.CreateDigest(suspension.Request.Plan, step).Value;
            ValidateResumeIdentityBinding(suspension, planDigest);
            if (!string.Equals(planDigest, suspension.PlanDigest, StringComparison.Ordinal) ||
                !string.Equals(actionDigest, suspension.ActionDigest, StringComparison.Ordinal) ||
                !string.Equals(
                    suspension.Envelope.Action.ActionDigest,
                    actionDigest,
                    StringComparison.Ordinal))
            {
                return Failure("suspension_binding_mismatch", ErrorCategory.Validation);
            }

            return await ExecuteGatewayAsync(
                suspension.Request,
                suspension.Envelope,
                planDigest,
                actionDigest,
                approvalNonce,
                cancellationToken);
        }
        catch (GovernanceException exception)
        {
            return Failure(exception.Code, exception.Category);
        }
        catch (ArgumentException)
        {
            return Failure("invalid_workflow_request", ErrorCategory.Validation);
        }
    }

    private async ValueTask<AgentWorkflowResult> ExecuteGatewayAsync(
        AgentWorkflowRequest request,
        TrustedActionEnvelope envelope,
        string planDigest,
        string actionDigest,
        string? approvalNonce,
        CancellationToken cancellationToken)
    {
        try
        {
            var gatewayResult = await gateway.ExecuteAsync(
                new GovernedToolRequest(
                    request.Plan,
                    request.StepId,
                    envelope,
                    approvalNonce,
                    request.IdempotencyKey,
                    request.ExpectedResourceVersion),
                cancellationToken);
            if (gatewayResult.Outcome == GatewayOutcome.ApprovalRequired)
            {
                return new AgentWorkflowResult(
                    AgentWorkflowStatus.ApprovalRequired,
                    gatewayResult.PolicyDecision.ReasonCode,
                    envelope,
                    gatewayResult,
                    completionEvaluator.Evaluate(request.CompletionCriteria),
                    new AgentWorkflowSuspension(
                        request,
                        envelope,
                        planDigest,
                        actionDigest),
                    null);
            }

            var simulatorState = completionEvaluator.Evaluate(request.CompletionCriteria);
            return new AgentWorkflowResult(
                simulatorState.IsComplete
                    ? AgentWorkflowStatus.Completed
                    : AgentWorkflowStatus.InProgress,
                simulatorState.IsComplete
                    ? "simulator_completion_satisfied"
                    : "simulator_completion_pending",
                envelope,
                gatewayResult,
                simulatorState,
                null,
                null);
        }
        catch (GovernanceException exception)
        {
            return Failure(exception.Code, exception.Category);
        }
    }

    private TrustedActionEnvelope CreateEnvelope(
        AgentWorkflowRequest request,
        PlanStep step,
        string actionDigest,
        PlanVerificationDecision decision) =>
        new(
            "1.0",
            Guid.NewGuid(),
            _timeProvider.GetUtcNow(),
            request.User,
            request.Agent,
            request.Session,
            new GovernedAction(
                request.Plan.PlanId,
                step.StepId,
                step.Tool,
                step.Capability,
                step.Effect,
                new ActionResource(step.Resource.Id, step.Resource.Environment),
                actionDigest),
            new VerificationAttestation(
                decision.Status,
                decision.SpecificationVersion,
                decision.VerifierVersion,
                decision.PlanDigest));

    private static void ValidateIdentityBinding(AgentWorkflowRequest request)
    {
        if (!string.Equals(request.Plan.AgentId, request.Agent.Id, StringComparison.Ordinal) ||
            !string.Equals(
                request.Plan.DeploymentVersion,
                request.Agent.DeploymentVersion,
                StringComparison.Ordinal) ||
            !string.Equals(
                request.Plan.IncidentId,
                request.Session.IncidentId,
                StringComparison.Ordinal) ||
            !string.Equals(
                request.Plan.IncidentId,
                request.CompletionCriteria.IncidentId,
                StringComparison.Ordinal))
        {
            throw new GovernanceException(
                ErrorCategory.Validation,
                "workflow_identity_mismatch",
                "The plan, agent, session, and completion criteria must have matching identities.");
        }
    }

    private void ValidateResumeIdentityBinding(
        AgentWorkflowSuspension suspension,
        string planDigest)
    {
        var request = suspension.Request;
        var envelope = suspension.Envelope;
        ValidateIdentityBinding(request);

        if (!string.Equals(envelope.User.Id, request.User.Id, StringComparison.Ordinal) ||
            !envelope.User.Roles.SequenceEqual(request.User.Roles, StringComparer.Ordinal) ||
            !string.Equals(envelope.Agent.Id, request.Agent.Id, StringComparison.Ordinal) ||
            !string.Equals(
                envelope.Agent.Identity,
                request.Agent.Identity,
                StringComparison.Ordinal) ||
            !string.Equals(
                envelope.Agent.DeploymentVersion,
                request.Agent.DeploymentVersion,
                StringComparison.Ordinal) ||
            !string.Equals(envelope.Session.Id, request.Session.Id, StringComparison.Ordinal) ||
            !string.Equals(
                envelope.Session.IncidentId,
                request.Session.IncidentId,
                StringComparison.Ordinal) ||
            envelope.Verification.Result != VerificationResult.Verified ||
            !string.Equals(
                envelope.Verification.SpecificationVersion,
                specificationVersion,
                StringComparison.Ordinal) ||
            !string.Equals(
                envelope.Verification.VerifierVersion,
                verifierVersion,
                StringComparison.Ordinal) ||
            !string.Equals(
                envelope.Verification.PlanDigest,
                planDigest,
                StringComparison.Ordinal))
        {
            throw new GovernanceException(
                ErrorCategory.Validation,
                "suspension_binding_mismatch",
                "The suspended trusted identity or verification binding was changed.");
        }
    }

    private static PlanStep ResolveStep(ActionPlan plan, string stepId)
    {
        var matches = plan.Steps
            .Where(step => string.Equals(step.StepId, stepId, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (matches.Length != 1)
        {
            throw new GovernanceException(
                ErrorCategory.Validation,
                "step_not_unique",
                "The workflow step must exist exactly once.");
        }

        return matches[0];
    }

    private static string CreatePlanDigest(ActionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var canonicalPlan = CanonicalJson.Serialize(plan);
        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPlan)));
    }

    private static AgentWorkflowResult Failure(
        string reasonCode,
        ErrorCategory errorCategory) =>
        new(
            AgentWorkflowStatus.Failed,
            reasonCode,
            null,
            null,
            null,
            null,
            errorCategory);
}
