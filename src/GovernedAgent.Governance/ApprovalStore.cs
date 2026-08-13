using System.Collections.Concurrent;
using GovernedAgent.Core.Contracts;

namespace GovernedAgent.Governance;

public sealed record ApprovalConsumptionRequest(
    Guid PlanId,
    string StepId,
    string ActionDigest,
    string ResourceId,
    TargetEnvironment Environment,
    string RequiredRole,
    string PolicyVersion,
    DateTimeOffset Now);

public interface IApprovalStore
{
    void Add(ApprovalArtifact approval);

    bool IsValid(
        string nonce,
        ApprovalConsumptionRequest request);

    bool TryConsume(
        string nonce,
        ApprovalConsumptionRequest request,
        out ApprovalArtifact? approval);

    bool Revoke(string nonce);
}

public sealed class InMemoryApprovalStore : IApprovalStore
{
    private readonly ConcurrentDictionary<string, ApprovalState> _approvals =
        new(StringComparer.Ordinal);

    public void Add(ApprovalArtifact approval)
    {
        ArgumentNullException.ThrowIfNull(approval);
        if (!_approvals.TryAdd(approval.Nonce, new ApprovalState(approval)))
        {
            throw new GovernanceException(
                ErrorCategory.Conflict,
                "duplicate_approval_nonce",
                "Approval nonces must be unique.");
        }
    }

    public bool TryConsume(
        string nonce,
        ApprovalConsumptionRequest request,
        out ApprovalArtifact? approval)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);
        ArgumentNullException.ThrowIfNull(request);
        approval = null;

        if (!_approvals.TryGetValue(nonce, out var state))
        {
            return false;
        }

        lock (state)
        {
            var candidate = state.Approval;
            if (!IsValid(state, request))
            {
                return false;
            }

            state.Consumed = true;
            approval = candidate;
            return true;
        }
    }

    public bool IsValid(
        string nonce,
        ApprovalConsumptionRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);
        ArgumentNullException.ThrowIfNull(request);
        if (!_approvals.TryGetValue(nonce, out var state))
        {
            return false;
        }

        lock (state)
        {
            return IsValid(state, request);
        }
    }

    public bool Revoke(string nonce)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);
        if (!_approvals.TryGetValue(nonce, out var state))
        {
            return false;
        }

        lock (state)
        {
            if (state.Consumed)
            {
                return false;
            }

            state.Revoked = true;
            return true;
        }
    }

    private sealed class ApprovalState(ApprovalArtifact approval)
    {
        public ApprovalArtifact Approval { get; } = approval;

        public bool Consumed { get; set; }

        public bool Revoked { get; set; }
    }

    private static bool IsValid(
        ApprovalState state,
        ApprovalConsumptionRequest request)
    {
        var candidate = state.Approval;
        return !state.Consumed &&
            !state.Revoked &&
            candidate.Decision == ApprovalDecision.Approved &&
            candidate.ExpiresAt > request.Now &&
            candidate.IssuedAt <= request.Now &&
            candidate.PlanId == request.PlanId &&
            string.Equals(candidate.StepId, request.StepId, StringComparison.Ordinal) &&
            string.Equals(
                candidate.ActionDigest,
                request.ActionDigest,
                StringComparison.Ordinal) &&
            string.Equals(candidate.ResourceId, request.ResourceId, StringComparison.Ordinal) &&
            candidate.Environment == request.Environment &&
            candidate.ApproverRoles.Contains(request.RequiredRole, StringComparer.Ordinal) &&
            string.Equals(
                candidate.PolicyVersion,
                request.PolicyVersion,
                StringComparison.Ordinal);
    }
}
