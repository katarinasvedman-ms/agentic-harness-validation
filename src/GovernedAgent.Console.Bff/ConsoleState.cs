using GovernedAgent.Core.Contracts;
using GovernedAgent.Governance;
using GovernedAgent.Simulator;

namespace GovernedAgent.Console.Bff;

public sealed class ConsoleState(
    IIncidentSimulator simulator,
    IConsoleWorkflowSnapshotProvider workflow,
    IApprovalStore approvals,
    IAuditChain audit,
    IKillSwitch killSwitch,
    ExecutionBudgetLimits limits,
    TimeProvider timeProvider)
{
    private readonly object _sync = new();
    private PendingApprovalView? _pending = CreatePending(workflow, timeProvider);

    public IncidentConsoleState GetIncident(string incidentId)
    {
        var incident = simulator.GetIncident(incidentId);
        return new(incident, simulator.GetServiceHealth(incident.ServiceId));
    }

    public IReadOnlyList<EvidenceItem> GetEvidence(string incidentId)
    {
        var incident = simulator.GetIncident(incidentId);
        var metrics = simulator.QueryMetrics(incident.ServiceId)
            .Select((metric, index) => new EvidenceItem(
                $"metric-{index + 1}",
                "metric",
                metric.Timestamp,
                $"{metric.Name}: {metric.Value} {metric.Unit}",
                false));
        var logs = simulator.QueryLogs(incident.ServiceId)
            .Select((log, index) => new EvidenceItem(
                $"log-{index + 1}",
                "log",
                log.Timestamp,
                log.Message,
                log.ContainsUntrustedContent));
        return metrics.Concat(logs).OrderBy(item => item.Timestamp).ToArray();
    }

    public IReadOnlyList<TimelineEntry> GetTimeline(string incidentId)
    {
        var incident = simulator.GetIncident(incidentId);
        var entries = new List<TimelineEntry>
        {
            new("incident", incident.UpdatedAt, "incident", $"Incident is {incident.Status}.")
        };
        entries.AddRange(audit.ReadAll()
            .Where(record => string.Equals(record.IncidentId, incidentId, StringComparison.Ordinal))
            .Select(record => new TimelineEntry(
                record.RecordId.ToString(),
                record.Timestamp,
                "governance",
                $"{record.Decision}: {record.ExecutionState}")));
        return entries.OrderBy(entry => entry.Timestamp).ToArray();
    }

    public VerificationView GetVerification(string incidentId) =>
        workflow.GetSnapshot(incidentId).Verification;

    public PendingApprovalView? GetPending(string incidentId)
    {
        simulator.GetIncident(incidentId);
        lock (_sync)
        {
            return _pending is { } pending && pending.ExpiresAt > timeProvider.GetUtcNow()
                ? pending
                : null;
        }
    }

    public ApprovalMutationResult Decide(
        Guid requestId,
        ApprovalDecision decision,
        DemoIdentity identity,
        string reason)
    {
        ValidateReason(reason);
        lock (_sync)
        {
            var pending = _pending;
            if (pending is null || pending.ApprovalRequestId != requestId)
            {
                throw new KeyNotFoundException("The pending approval request does not exist.");
            }

            var now = timeProvider.GetUtcNow();
            if (pending.ExpiresAt <= now)
            {
                throw new InvalidOperationException("The pending approval request has expired.");
            }

            if (!identity.IsInRole(pending.RequiredRole))
            {
                throw new UnauthorizedAccessException("The exact required approval role is missing.");
            }

            string? nonce = null;
            if (decision == ApprovalDecision.Approved)
            {
                nonce = Convert.ToHexStringLower(Guid.NewGuid().ToByteArray());
                approvals.Add(new ApprovalArtifact(
                    Guid.NewGuid(),
                    identity.Id,
                    identity.Roles.Order(StringComparer.Ordinal).ToArray(),
                    pending.PlanId,
                    pending.StepId,
                    pending.ActionDigest,
                    pending.ResourceId,
                    pending.Environment,
                    decision,
                    now,
                    pending.ExpiresAt,
                    nonce,
                    pending.PolicyVersion));
            }

            AppendAudit(pending, decision, now);
            _pending = null;
            return new(requestId, decision, identity.Id, now, nonce);
        }
    }

    public ControlsView GetControls() =>
        new(killSwitch.IsActive, limits.MaximumToolCalls, (int)limits.MaximumDuration.TotalSeconds);

    public ControlsView SetKillSwitch(bool active, string reason)
    {
        ValidateReason(reason);
        if (active)
        {
            killSwitch.Activate();
        }
        else
        {
            killSwitch.Deactivate();
        }

        return GetControls();
    }

    public AuditView GetAudit() => new(audit.VerifyIntegrity(), audit.ReadAll());

    public void Reset()
    {
        simulator.Reset();
        killSwitch.Deactivate();
        lock (_sync)
        {
            _pending = CreatePending(workflow, timeProvider);
        }
    }

    private void AppendAudit(
        PendingApprovalView pending,
        ApprovalDecision decision,
        DateTimeOffset timestamp)
    {
        audit.Append(new AuditRecord(
            Guid.NewGuid(),
            Guid.NewGuid(),
            pending.ApprovalRequestId.ToString(),
            IncidentSimulator.DemoIncidentId,
            pending.PlanId,
            pending.StepId,
            pending.ActionDigest,
            decision == ApprovalDecision.Approved
                ? GovernanceDecision.Allow
                : GovernanceDecision.Deny,
            pending.PolicyVersion,
            VerificationResult.Verified,
            decision == ApprovalDecision.Approved
                ? ExecutionState.Approved
                : ExecutionState.Denied,
            timestamp,
            null,
            string.Empty));
    }

    private static PendingApprovalView CreatePending(
        IConsoleWorkflowSnapshotProvider workflow,
        TimeProvider timeProvider)
    {
        var snapshot = workflow.GetSnapshot(IncidentSimulator.DemoIncidentId);
        var step = snapshot.Verification.Plan.Steps.Single();
        return new(
            Guid.NewGuid(),
            snapshot.Verification.Plan.PlanId,
            step.StepId,
            snapshot.ActionDigest,
            step.Resource.Id,
            step.Resource.Environment,
            snapshot.RequiredRole,
            snapshot.PolicyVersion,
            timeProvider.GetUtcNow().AddMinutes(15));
    }

    private static void ValidateReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
        {
            throw new ArgumentException("Reason must contain between 1 and 500 characters.");
        }
    }
}
