using GovernedAgent.Core.Contracts;
using GovernedAgent.Governance;
using GovernedAgent.Simulator;

namespace GovernedAgent.Host.Workflow;

public enum AgentWorkflowStatus
{
    InProgress,
    ApprovalRequired,
    Completed,
    Failed
}

public sealed record WorkflowCompletionCriteria(
    string IncidentId,
    IncidentStatus? IncidentStatus = null,
    string? ServiceId = null,
    ServiceHealth? ServiceHealth = null);

public sealed record WorkflowSimulatorState(
    IncidentStatus IncidentStatus,
    long IncidentVersion,
    ServiceHealth? ServiceHealth,
    long? ServiceVersion,
    bool IsComplete);

public sealed record AgentWorkflowRequest(
    ActionPlan Plan,
    string StepId,
    UserIdentity User,
    AgentIdentity Agent,
    SessionIdentity Session,
    string IdempotencyKey,
    long ExpectedResourceVersion,
    WorkflowCompletionCriteria CompletionCriteria);

public sealed record AgentWorkflowSuspension(
    AgentWorkflowRequest Request,
    TrustedActionEnvelope Envelope,
    string PlanDigest,
    string ActionDigest);

public sealed record AgentWorkflowResult(
    AgentWorkflowStatus Status,
    string ReasonCode,
    TrustedActionEnvelope? Envelope,
    GatewayResult? GatewayResult,
    WorkflowSimulatorState? SimulatorState,
    AgentWorkflowSuspension? Suspension,
    ErrorCategory? ErrorCategory);

public interface IAgentWorkflow
{
    ValueTask<AgentWorkflowResult> ExecuteAsync(
        AgentWorkflowRequest request,
        CancellationToken cancellationToken);

    ValueTask<AgentWorkflowResult> ResumeAsync(
        AgentWorkflowSuspension suspension,
        string approvalNonce,
        CancellationToken cancellationToken);
}

public interface IWorkflowCompletionEvaluator
{
    WorkflowSimulatorState Evaluate(WorkflowCompletionCriteria criteria);
}

public sealed class SimulatorWorkflowCompletionEvaluator(
    IIncidentSimulator simulator) : IWorkflowCompletionEvaluator
{
    public WorkflowSimulatorState Evaluate(WorkflowCompletionCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentException.ThrowIfNullOrWhiteSpace(criteria.IncidentId);
        if (criteria.IncidentStatus is null && criteria.ServiceHealth is null)
        {
            throw new ArgumentException(
                "At least one simulator completion condition is required.",
                nameof(criteria));
        }

        if (criteria.ServiceHealth is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(criteria.ServiceId);
        }

        var incident = simulator.GetIncident(criteria.IncidentId);
        var service = criteria.ServiceId is null
            ? null
            : simulator.GetServiceHealth(criteria.ServiceId);
        var incidentComplete = criteria.IncidentStatus is null ||
            incident.Status == criteria.IncidentStatus;
        var serviceComplete = criteria.ServiceHealth is null ||
            service?.Health == criteria.ServiceHealth;

        return new WorkflowSimulatorState(
            incident.Status,
            incident.Version,
            service?.Health,
            service?.Version,
            incidentComplete && serviceComplete);
    }
}
