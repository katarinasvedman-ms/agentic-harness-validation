using GovernedAgent.Core.Contracts;

namespace GovernedAgent.ConformanceTests;

public sealed class ContractConformanceTests
{
    [Fact]
    public void ProductionDeleteIsRepresentableForExplicitDenial()
    {
        Assert.Equal("Delete", EffectKind.Delete.ToString());
        Assert.Equal("Production", TargetEnvironment.Production.ToString());
    }
}
