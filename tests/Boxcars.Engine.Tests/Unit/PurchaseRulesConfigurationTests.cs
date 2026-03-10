using Boxcars.Engine.Domain;
using RailBaronGameEngine = Boxcars.Engine.Domain.GameEngine;

namespace Boxcars.Engine.Tests.Unit;

/// <summary>
/// Regression coverage for configurable purchase rules (T029).
/// Validates that GetUpgradeCost respects configurable Superchief pricing.
/// </summary>
public class PurchaseRulesConfigurationTests
{
    [Theory]
    [InlineData(30_000)]
    [InlineData(40_000)]
    [InlineData(50_000)]
    [InlineData(100_000)]
    public void GetUpgradeCost_SuperchiefPrice_ReflectsConfiguration(int configuredPrice)
    {
        var cost = RailBaronGameEngine.GetUpgradeCost(LocomotiveType.Freight, LocomotiveType.Superchief, configuredPrice);
        Assert.Equal(configuredPrice, cost);
    }

    [Theory]
    [InlineData(30_000)]
    [InlineData(40_000)]
    [InlineData(50_000)]
    public void GetUpgradeCost_ExpressToSuperchief_ReflectsConfiguration(int configuredPrice)
    {
        var cost = RailBaronGameEngine.GetUpgradeCost(LocomotiveType.Express, LocomotiveType.Superchief, configuredPrice);
        Assert.Equal(configuredPrice, cost);
    }

    [Fact]
    public void GetUpgradeCost_FreightToExpress_IgnoresSuperchiefPrice()
    {
        // Express always costs $4,000 regardless of Superchief configuration
        var cost1 = RailBaronGameEngine.GetUpgradeCost(LocomotiveType.Freight, LocomotiveType.Express, 30_000);
        var cost2 = RailBaronGameEngine.GetUpgradeCost(LocomotiveType.Freight, LocomotiveType.Express, 100_000);

        Assert.Equal(4_000, cost1);
        Assert.Equal(4_000, cost2);
    }

    [Fact]
    public void GetUpgradeCost_InvalidPaths_ReturnNegativeOne()
    {
        Assert.Equal(-1, RailBaronGameEngine.GetUpgradeCost(LocomotiveType.Superchief, LocomotiveType.Freight, 40_000));
        Assert.Equal(-1, RailBaronGameEngine.GetUpgradeCost(LocomotiveType.Superchief, LocomotiveType.Express, 40_000));
        Assert.Equal(-1, RailBaronGameEngine.GetUpgradeCost(LocomotiveType.Superchief, LocomotiveType.Superchief, 40_000));
        Assert.Equal(-1, RailBaronGameEngine.GetUpgradeCost(LocomotiveType.Express, LocomotiveType.Express, 40_000));
        Assert.Equal(-1, RailBaronGameEngine.GetUpgradeCost(LocomotiveType.Freight, LocomotiveType.Freight, 40_000));
    }
}
