using GovernedAgent.Core.Contracts;

namespace GovernedAgent.SecurityTests;

public sealed class FailClosedContractTests
{
    [Theory]
    [InlineData(VerificationResult.Rejected)]
    [InlineData(VerificationResult.Indeterminate)]
    public void NonVerifiedResultsAreNeverVerified(VerificationResult result)
    {
        Assert.NotEqual(VerificationResult.Verified, result);
    }
}
