namespace GovernedAgent.IntegrationTests;

public sealed class AssemblySmokeTests
{
    [Fact]
    public void TestAssemblyLoads()
    {
        Assert.NotNull(typeof(AssemblySmokeTests).Assembly);
    }
}
