using GovernedAgent.Core.Contracts;

namespace GovernedAgent.Host.Verification;

public sealed record PlanVerificationRequest(
    ActionPlan Plan,
    DateTimeOffset CurrentTime,
    int MaximumSteps,
    IReadOnlyList<string> AgentCapabilities,
    IReadOnlyDictionary<string, VerifierToolMetadata> ToolRegistry,
    string PlanDigest,
    string SpecificationVersion,
    string VerifierVersion);

public sealed record VerifierToolMetadata(
    string Capability,
    EffectKind Effect,
    ApprovalClass ApprovalClass,
    string ResourceArgument);

public sealed record PlanVerificationDecision(
    VerificationResult Status,
    IReadOnlyList<string> ReasonCodes,
    string PlanDigest,
    string SpecificationVersion,
    string VerifierVersion);

public interface IPlanVerifier
{
    ValueTask<PlanVerificationDecision> VerifyAsync(
        PlanVerificationRequest request,
        CancellationToken cancellationToken);
}
