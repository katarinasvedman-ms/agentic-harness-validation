using System.Security.Cryptography;
using System.Text;
using GovernedAgent.Core.Contracts;
using GovernedAgent.Governance;
using GovernedAgent.Simulator;

namespace GovernedAgent.Console.Bff;

public sealed class ConsoleState(
    IIncidentSimulator simulator,
    IConsoleWorkflowSnapshotProvider workflow,
    IApprovalStore approvals,
    IAuditChain audit,
    ICapabilityLeaseStore capabilityLeases,
    IContainmentControl containmentControl,
    IToolRegistry toolRegistry,
    GovernedToolGateway gateway,
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
        new(
            containmentControl.Mode,
            containmentControl.Mode == ContainmentMode.Contained,
            limits.MaximumToolCalls,
            (int)limits.MaximumDuration.TotalSeconds,
            containmentControl.Reattestation,
            containmentControl.SignOff);

    public CapabilityLeasesView GetCapabilityLeases() =>
        new(
            "Structured verified plan",
            "Advisory only; prompts cannot grant access",
            capabilityLeases.ReadAll(timeProvider.GetUtcNow())
                .Select(ToView)
                .ToArray());

    public async ValueTask<GovernedExecutionView> ExecuteApprovedAsync(
        string incidentId,
        ExecutionMutation request,
        DemoIdentity identity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(identity);
        ValidateRequired(request.ApprovalNonce, nameof(request.ApprovalNonce), 200);

        var snapshot = workflow.GetSnapshot(incidentId);
        var plan = snapshot.Verification.Plan;
        var step = plan.Steps.Single();
        if (!toolRegistry.TryGet(step.Tool, out var tool))
        {
            throw new InvalidOperationException(
                $"Required console workflow tool '{step.Tool}' is not registered.");
        }

        var envelope = new TrustedActionEnvelope(
            "1.0",
            Guid.NewGuid(),
            timeProvider.GetUtcNow(),
            new UserIdentity(identity.Id, identity.Roles.Order(StringComparer.Ordinal).ToArray()),
            new AgentIdentity(plan.AgentId, "local-demo-agent-identity", plan.DeploymentVersion),
            new SessionIdentity($"console:{incidentId}", incidentId),
            new GovernedAction(
                plan.PlanId,
                step.StepId,
                tool.Name,
                tool.Intent,
                tool.Capability,
                tool.Effect,
                new ActionResource(step.Resource.Id, step.Resource.Environment),
                snapshot.ActionDigest),
            new VerificationAttestation(
                snapshot.Verification.Result,
                snapshot.Verification.SpecificationVersion,
                snapshot.Verification.VerifierVersion,
                snapshot.Verification.PlanDigest));
        var expectedVersion = simulator.GetServiceHealth(step.Resource.Id).Version;
        var result = await gateway.ExecuteAsync(
            new GovernedToolRequest(
                plan,
                step.StepId,
                envelope,
                request.ApprovalNonce,
                $"console:{incidentId}:{request.ApprovalNonce}",
                expectedVersion),
            cancellationToken);
        var lease = result.CapabilityLease ??
            throw new InvalidOperationException(
                "Executed gateway result did not include a capability lease.");
        return new GovernedExecutionView(
            result.Outcome,
            result.PolicyDecision.Decision,
            result.ActionDigest,
            result.ToolResult,
            ToView(lease));
    }

    public ControlsView SetContainment(
        bool active,
        string reason,
        DemoIdentity identity)
    {
        ValidateReason(reason);
        ArgumentNullException.ThrowIfNull(identity);
        if (!active)
        {
            throw new InvalidOperationException(
                "Containment can be cleared only through the staged recovery workflow.");
        }

        lock (_sync)
        {
            containmentControl.Contain();
            var snapshot = workflow.GetSnapshot(IncidentSimulator.DemoIncidentId);
            var revokedLeases = capabilityLeases.RevokeAgent(
                snapshot.Verification.Plan.AgentId,
                reason);
            foreach (var lease in revokedLeases)
            {
                AppendControlAudit(
                    "lease.revoke",
                    identity,
                    $"{lease.LeaseId}:{reason}",
                    GovernanceDecision.Deny,
                    ExecutionState.Denied,
                    VerificationResult.Verified,
                    lease);
            }

            AppendControlAudit(
                "containment.activate",
                identity,
                reason,
                GovernanceDecision.Deny,
                ExecutionState.Completed,
                VerificationResult.Indeterminate);
        }
        return GetControls();
    }

    public ControlsView BeginReadOnlyRecovery(
        ReattestationMutation request,
        DemoIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(identity);
        ValidateReason(request.Reason);
        ValidateDigest(request.ArtifactDigest);
        ValidateRequired(request.KnownGoodVersion, nameof(request.KnownGoodVersion), 200);

        lock (_sync)
        {
            if (containmentControl.Mode != ContainmentMode.Contained)
            {
                throw new InvalidOperationException(
                    "Read-only recovery can begin only from the contained state.");
            }

            var now = timeProvider.GetUtcNow();
            AppendControlAudit(
                "recovery.reattest",
                identity,
                $"{request.ArtifactDigest}:{request.KnownGoodVersion}:{request.Reason}",
                GovernanceDecision.Allow,
                ExecutionState.Verified,
                VerificationResult.Verified);
            containmentControl.BeginReadOnlyRecovery(new ReattestationArtifact(
                request.ArtifactDigest,
                request.KnownGoodVersion,
                identity.Id,
                now));
        }
        return GetControls();
    }

    public ControlsView RestoreOperational(
        RecoveryMutation request,
        DemoIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(identity);
        ValidateReason(request.Reason);
        ValidateRequired(request.RootCause, nameof(request.RootCause), 500);

        lock (_sync)
        {
            if (containmentControl.Mode != ContainmentMode.ReadOnlyRecovery ||
                containmentControl.Reattestation is null)
            {
                throw new InvalidOperationException(
                    "Operational access can be restored only after re-attestation.");
            }

            var signOff = new RecoverySignOff(
                identity.Id,
                request.RootCause,
                timeProvider.GetUtcNow());
            AppendControlAudit(
                "recovery.restore",
                identity,
                $"{request.RootCause}:{request.Reason}",
                GovernanceDecision.Allow,
                ExecutionState.Completed,
                VerificationResult.Verified);
            containmentControl.RestoreOperational(signOff);
        }
        return GetControls();
    }

    public AuditView GetAudit() => new(audit.VerifyIntegrity(), audit.ReadAll());

    public void Reset()
    {
        simulator.Reset();
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

    private void AppendControlAudit(
        string stepId,
        DemoIdentity identity,
        string evidence,
        GovernanceDecision decision,
        ExecutionState state,
        VerificationResult verification)
        => AppendControlAudit(
            stepId,
            identity,
            evidence,
            decision,
            state,
            verification,
            capabilityLease: null);

    private void AppendControlAudit(
        string stepId,
        DemoIdentity identity,
        string evidence,
        GovernanceDecision decision,
        ExecutionState state,
        VerificationResult verification,
        CapabilityLeaseArtifact? capabilityLease)
    {
        var snapshot = workflow.GetSnapshot(IncidentSimulator.DemoIncidentId);
        var digest = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(evidence)));
        audit.Append(new AuditRecord(
            Guid.NewGuid(),
            Guid.NewGuid(),
            $"control:{identity.Id}",
            IncidentSimulator.DemoIncidentId,
            snapshot.Verification.Plan.PlanId,
            stepId,
            digest,
            decision,
            snapshot.PolicyVersion,
            verification,
            state,
            timeProvider.GetUtcNow(),
            null,
            string.Empty,
            capabilityLease?.LeaseId,
            capabilityLease?.State));
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

    private static CapabilityLeaseView ToView(CapabilityLeaseArtifact lease) =>
        new(
            lease.LeaseId,
            lease.AgentId,
            lease.SessionId,
            lease.PlanId,
            lease.StepId,
            lease.ActionDigest,
            lease.Intent,
            lease.Capability,
            lease.Tool,
            lease.Effect,
            lease.ResourceId,
            lease.Environment,
            lease.IssuedAt,
            lease.ExpiresAt,
            lease.MaximumUses,
            lease.ConsumedUses,
            lease.State,
            lease.RevocationReason);

    private static void ValidateReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
        {
            throw new ArgumentException("Reason must contain between 1 and 500 characters.");
        }
    }

    private static void ValidateDigest(string digest)
    {
        if (string.IsNullOrEmpty(digest) ||
            digest.Length != 64 ||
            digest.Any(character =>
                !char.IsAsciiDigit(character) &&
                character is not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException(
                "Artifact digest must be 64 lowercase hexadecimal characters.",
                nameof(digest));
        }
    }

    private static void ValidateRequired(
        string value,
        string parameterName,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
        {
            throw new ArgumentException(
                $"Value must contain between 1 and {maximumLength} characters.",
                parameterName);
        }
    }
}
