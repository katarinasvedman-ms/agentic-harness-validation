using System.Security.Cryptography;
using GovernedAgent.Core.Contracts;

namespace GovernedAgent.Governance;

public sealed record CapabilityLeaseIssueRequest(
    TrustedActionEnvelope Envelope,
    ToolMetadata Tool,
    string PolicyVersion,
    DateTimeOffset IssuedAt,
    TimeSpan Lifetime,
    int MaximumUses);

public sealed record CapabilityLeaseConsumptionRequest(
    TrustedActionEnvelope Envelope,
    ToolMetadata Tool,
    string PolicyVersion,
    DateTimeOffset Now);

public interface ICapabilityLeaseStore
{
    CapabilityLeaseArtifact Issue(CapabilityLeaseIssueRequest request);

    bool TryConsume(
        string nonce,
        CapabilityLeaseConsumptionRequest request,
        out CapabilityLeaseArtifact? lease);

    bool TryComplete(
        Guid leaseId,
        DateTimeOffset completedAt,
        out CapabilityLeaseArtifact? lease);

    bool Revoke(
        Guid leaseId,
        string reason,
        out CapabilityLeaseArtifact? lease);

    IReadOnlyList<CapabilityLeaseArtifact> RevokeSession(
        string sessionId,
        string reason);

    IReadOnlyList<CapabilityLeaseArtifact> RevokeAgent(
        string agentId,
        string reason);

    IReadOnlyList<CapabilityLeaseArtifact> ReadAll(DateTimeOffset now);
}

public sealed class InMemoryCapabilityLeaseStore : ICapabilityLeaseStore
{
    private static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(5);
    private readonly object _sync = new();
    private readonly Dictionary<Guid, CapabilityLeaseArtifact> _leases = [];
    private readonly Dictionary<string, Guid> _leasesByNonce = new(StringComparer.Ordinal);

    public CapabilityLeaseArtifact Issue(CapabilityLeaseIssueRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateIssueRequest(request);

        lock (_sync)
        {
            var lease = new CapabilityLeaseArtifact(
                Guid.NewGuid(),
                Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)),
                request.Envelope.Agent.Id,
                request.Envelope.Agent.DeploymentVersion,
                request.Envelope.User.Id,
                request.Envelope.Session.Id,
                request.Envelope.Session.IncidentId,
                request.Envelope.Action.PlanId,
                request.Envelope.Action.StepId,
                request.Envelope.Verification.PlanDigest,
                request.Envelope.Action.ActionDigest,
                request.Tool.Intent,
                request.Tool.Capability,
                request.Tool.Name,
                request.Tool.Effect,
                request.Envelope.Action.Resource.Id,
                request.Envelope.Action.Resource.Environment,
                request.PolicyVersion,
                request.Envelope.Verification.VerifierVersion,
                request.IssuedAt,
                request.IssuedAt.Add(request.Lifetime),
                request.MaximumUses,
                ConsumedUses: 0,
                CapabilityLeaseState.Issued,
                RevocationReason: null);
            _leases.Add(lease.LeaseId, lease);
            _leasesByNonce.Add(lease.Nonce, lease.LeaseId);
            return lease;
        }
    }

    public bool TryConsume(
        string nonce,
        CapabilityLeaseConsumptionRequest request,
        out CapabilityLeaseArtifact? lease)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);
        ArgumentNullException.ThrowIfNull(request);

        lock (_sync)
        {
            lease = null;
            if (!_leasesByNonce.TryGetValue(nonce, out var leaseId) ||
                !_leases.TryGetValue(leaseId, out var candidate))
            {
                return false;
            }

            candidate = ExpireIfNeeded(candidate, request.Now);
            _leases[leaseId] = candidate;
            if (candidate.State != CapabilityLeaseState.Issued ||
                !Matches(candidate, request))
            {
                return false;
            }

            var consumed = candidate with
            {
                ConsumedUses = candidate.ConsumedUses + 1,
                State = CapabilityLeaseState.Consumed
            };
            _leases[leaseId] = consumed;
            lease = consumed;
            return true;
        }
    }

    public bool TryComplete(
        Guid leaseId,
        DateTimeOffset completedAt,
        out CapabilityLeaseArtifact? lease)
    {
        lock (_sync)
        {
            lease = null;
            if (!_leases.TryGetValue(leaseId, out var candidate))
            {
                return false;
            }

            candidate = ExpireIfNeeded(candidate, completedAt);
            if (candidate.State != CapabilityLeaseState.Consumed)
            {
                _leases[leaseId] = candidate;
                return false;
            }

            lease = candidate with { State = CapabilityLeaseState.Completed };
            _leases[leaseId] = lease;
            return true;
        }
    }

    public bool Revoke(
        Guid leaseId,
        string reason,
        out CapabilityLeaseArtifact? lease)
    {
        ValidateReason(reason);
        lock (_sync)
        {
            lease = null;
            if (!_leases.TryGetValue(leaseId, out var candidate) ||
                candidate.State is CapabilityLeaseState.Completed
                    or CapabilityLeaseState.Revoked
                    or CapabilityLeaseState.Expired)
            {
                return false;
            }

            lease = candidate with
            {
                State = CapabilityLeaseState.Revoked,
                RevocationReason = reason
            };
            _leases[leaseId] = lease;
            return true;
        }
    }

    public IReadOnlyList<CapabilityLeaseArtifact> RevokeSession(
        string sessionId,
        string reason) =>
        RevokeMatching(
            lease => string.Equals(lease.SessionId, sessionId, StringComparison.Ordinal),
            reason);

    public IReadOnlyList<CapabilityLeaseArtifact> RevokeAgent(
        string agentId,
        string reason) =>
        RevokeMatching(
            lease => string.Equals(lease.AgentId, agentId, StringComparison.Ordinal),
            reason);

    public IReadOnlyList<CapabilityLeaseArtifact> ReadAll(DateTimeOffset now)
    {
        lock (_sync)
        {
            foreach (var (leaseId, lease) in _leases.ToArray())
            {
                _leases[leaseId] = ExpireIfNeeded(lease, now);
            }

            return _leases.Values
                .OrderBy(lease => lease.IssuedAt)
                .ToArray();
        }
    }

    private IReadOnlyList<CapabilityLeaseArtifact> RevokeMatching(
        Func<CapabilityLeaseArtifact, bool> predicate,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ValidateReason(reason);
        lock (_sync)
        {
            var revoked = new List<CapabilityLeaseArtifact>();
            foreach (var (leaseId, lease) in _leases.ToArray())
            {
                if (!predicate(lease) ||
                    lease.State is not (
                        CapabilityLeaseState.Issued or
                        CapabilityLeaseState.Consumed))
                {
                    continue;
                }

                var revokedLease = lease with
                {
                    State = CapabilityLeaseState.Revoked,
                    RevocationReason = reason
                };
                _leases[leaseId] = revokedLease;
                revoked.Add(revokedLease);
            }

            return revoked;
        }
    }

    private static CapabilityLeaseArtifact ExpireIfNeeded(
        CapabilityLeaseArtifact lease,
        DateTimeOffset now) =>
        lease.State == CapabilityLeaseState.Issued && lease.ExpiresAt <= now
            ? lease with
            {
                State = CapabilityLeaseState.Expired,
                RevocationReason = "Lease lifetime elapsed."
            }
            : lease;

    private static bool Matches(
        CapabilityLeaseArtifact lease,
        CapabilityLeaseConsumptionRequest request)
    {
        var envelope = request.Envelope;
        var action = envelope.Action;
        return lease.IssuedAt <= request.Now &&
            lease.ExpiresAt > request.Now &&
            lease.ConsumedUses < lease.MaximumUses &&
            string.Equals(lease.AgentId, envelope.Agent.Id, StringComparison.Ordinal) &&
            string.Equals(
                lease.DeploymentVersion,
                envelope.Agent.DeploymentVersion,
                StringComparison.Ordinal) &&
            string.Equals(lease.UserId, envelope.User.Id, StringComparison.Ordinal) &&
            string.Equals(lease.SessionId, envelope.Session.Id, StringComparison.Ordinal) &&
            string.Equals(
                lease.IncidentId,
                envelope.Session.IncidentId,
                StringComparison.Ordinal) &&
            lease.PlanId == action.PlanId &&
            string.Equals(lease.StepId, action.StepId, StringComparison.Ordinal) &&
            string.Equals(
                lease.PlanDigest,
                envelope.Verification.PlanDigest,
                StringComparison.Ordinal) &&
            string.Equals(lease.ActionDigest, action.ActionDigest, StringComparison.Ordinal) &&
            lease.Intent == request.Tool.Intent &&
            lease.Intent == action.Intent &&
            string.Equals(lease.Capability, request.Tool.Capability, StringComparison.Ordinal) &&
            string.Equals(lease.Capability, action.Capability, StringComparison.Ordinal) &&
            string.Equals(lease.Tool, request.Tool.Name, StringComparison.Ordinal) &&
            string.Equals(lease.Tool, action.Tool, StringComparison.Ordinal) &&
            lease.Effect == request.Tool.Effect &&
            lease.Effect == action.Effect &&
            string.Equals(lease.ResourceId, action.Resource.Id, StringComparison.Ordinal) &&
            lease.Environment == action.Resource.Environment &&
            string.Equals(lease.PolicyVersion, request.PolicyVersion, StringComparison.Ordinal) &&
            string.Equals(
                lease.VerifierVersion,
                envelope.Verification.VerifierVersion,
                StringComparison.Ordinal);
    }

    private static void ValidateIssueRequest(CapabilityLeaseIssueRequest request)
    {
        if (request.Lifetime <= TimeSpan.Zero || request.Lifetime > MaximumLifetime)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Capability lease lifetime must be between zero and five minutes.");
        }

        if (request.MaximumUses != 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "The local demo permits exactly one lease use.");
        }

        if (request.Envelope.Verification.Result != VerificationResult.Verified)
        {
            throw new GovernanceException(
                ErrorCategory.VerificationRejected,
                "lease_plan_not_verified",
                "A capability lease requires a verified plan.");
        }

        if (request.Envelope.Action.Intent != request.Tool.Intent ||
            !string.Equals(
                request.Envelope.Action.Capability,
                request.Tool.Capability,
                StringComparison.Ordinal) ||
            !string.Equals(
                request.Envelope.Action.Tool,
                request.Tool.Name,
                StringComparison.Ordinal) ||
            request.Envelope.Action.Effect != request.Tool.Effect)
        {
            throw new GovernanceException(
                ErrorCategory.CapabilityLeaseInvalid,
                "lease_trusted_metadata_mismatch",
                "Capability lease metadata must match the trusted action and tool.");
        }
    }

    private static void ValidateReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
        {
            throw new ArgumentException(
                "Revocation reason must contain between 1 and 500 characters.",
                nameof(reason));
        }
    }
}
