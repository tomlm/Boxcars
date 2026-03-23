using Boxcars.Engine.Domain;
using Boxcars.Engine.Persistence;
using Boxcars.Engine.Tests.Fixtures;
using Boxcars.Engine.Tests.TestDoubles;
using GE = Boxcars.Engine.Domain.GameEngine;

namespace Boxcars.Engine.Tests.Unit;

public class GameEngineSettingsStartupTests
{
    [Fact]
    public void Constructor_CustomSettings_StartsPlayersWithConfiguredCashAndEngine()
    {
        var map = GameEngineFixture.CreateTestMap();
        var random = GameEngineFixture.CreateDeterministicRandom();
        var settings = GameSettings.Default with
        {
            StartingCash = 35_000,
            StartEngine = LocomotiveType.Express
        };

        var engine = new GE(map, GameEngineFixture.DefaultPlayerNames, random, settings);

        Assert.All(engine.Players, player =>
        {
            Assert.Equal(35_000, player.Cash);
            Assert.Equal(LocomotiveType.Express, player.LocomotiveType);
        });
    }

    [Fact]
    public void GetUpgradeCost_UsesConfiguredGameSettings()
    {
        var settings = GameSettings.Default with
        {
            ExpressPrice = 6_000,
            SuperchiefPrice = 45_000
        };

        Assert.Equal(6_000, GE.GetUpgradeCost(LocomotiveType.Freight, LocomotiveType.Express, settings));
        Assert.Equal(45_000, GE.GetUpgradeCost(LocomotiveType.Freight, LocomotiveType.Superchief, settings));
        Assert.Equal(45_000, GE.GetUpgradeCost(LocomotiveType.Express, LocomotiveType.Superchief, settings));
    }

    [Fact]
    public void FromSnapshot_RestoresConfiguredUpgradePricing()
    {
        var map = GameEngineFixture.CreateTestMap();
        var random = new FixedRandomProvider();
        var settings = GameSettings.Default with
        {
            ExpressPrice = 6_000,
            SuperchiefPrice = 45_000
        };
        var engine = new GE(map, GameEngineFixture.DefaultPlayerNames, random, settings);

        var restored = GE.FromSnapshot(engine.ToSnapshot(), map, new FixedRandomProvider(), settings);

        Assert.Equal(6_000, GE.GetUpgradeCost(restored.Players[0].LocomotiveType, LocomotiveType.Express, restored.Settings));
        Assert.Equal(45_000, GE.GetUpgradeCost(LocomotiveType.Express, LocomotiveType.Superchief, restored.Settings));
    }
}