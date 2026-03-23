using Boxcars.Engine.Domain;
using Boxcars.Engine.Persistence;
using Boxcars.Engine.Tests.Fixtures;
using Boxcars.Engine.Tests.TestDoubles;
using GE = Boxcars.Engine.Domain.GameEngine;

namespace Boxcars.Engine.Tests.Unit;

public class GameSettingsHomeSelectionTests
{
    [Fact]
    public void ChooseHomeCity_WhenEnabled_UsesEligibleCityAndAdvancesToDestinationDraw()
    {
        var settings = GameSettings.Default with
        {
            HomeCityChoice = true,
            HomeSwapping = false
        };
        var map = GameEngineFixture.CreateTestMap();
        var random = GameEngineFixture.CreateDeterministicRandom();
        var engine = new GE(map, GameEngineFixture.DefaultPlayerNames, random, settings);

        var pendingChoice = engine.CurrentTurn.PendingHomeCityChoice;
        Assert.NotNull(pendingChoice);

        Assert.Equal(TurnPhase.HomeCityChoice, engine.CurrentTurn.Phase);
        Assert.Equal(["Boston", "New York"], pendingChoice.EligibleCityNames);

        var chosenCity = engine.ChooseHomeCity("Boston");

        Assert.Equal("Boston", chosenCity.Name);
        Assert.Equal("Boston", engine.CurrentTurn.ActivePlayer.HomeCity.Name);
        Assert.Equal("Boston", engine.CurrentTurn.ActivePlayer.CurrentCity.Name);
        Assert.True(engine.CurrentTurn.ActivePlayer.HasResolvedHomeCityChoice);
        Assert.Null(engine.CurrentTurn.PendingHomeCityChoice);
        Assert.Equal(TurnPhase.DrawDestination, engine.CurrentTurn.Phase);
    }

    [Fact]
    public void ChooseHomeCity_NextPlayerOptions_ExcludeAlreadyClaimedHomeCity()
    {
        var settings = GameSettings.Default with
        {
            HomeCityChoice = true,
            HomeSwapping = false
        };
        var map = GameEngineFixture.CreateTestMap();
        var random = new FixedRandomProvider();
        random.QueueWeightedDraw(0);
        random.QueueWeightedDraw(0);
        random.QueueWeightedDraw(0);
        random.QueueWeightedDraw(0);
        var engine = new GE(map, GameEngineFixture.DefaultPlayerNames, random, settings);

        engine.ChooseHomeCity("Boston");
        engine.CurrentTurn.Phase = TurnPhase.EndTurn;

        engine.EndTurn();

        var pendingChoice = engine.CurrentTurn.PendingHomeCityChoice;
        Assert.NotNull(pendingChoice);
        Assert.Equal(1, pendingChoice.PlayerIndex);
        Assert.Equal(["New York"], pendingChoice.EligibleCityNames);
        Assert.DoesNotContain("Boston", pendingChoice.EligibleCityNames, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void DrawDestination_FirstTripWithHomeSwapping_PresentsSwapAndAllowsHomeDestinationSwap()
    {
        var settings = GameSettings.Default with
        {
            HomeCityChoice = false,
            HomeSwapping = true
        };
        var map = GameEngineFixture.CreateTestMap();
        var random = GameEngineFixture.CreateDeterministicRandom();
        var engine = new GE(map, GameEngineFixture.DefaultPlayerNames, random, settings);

        random.QueueWeightedDraw(1);
        random.QueueWeightedDraw(1);
        var firstDestination = engine.DrawDestination();

        Assert.Equal("Atlanta", firstDestination.Name);
        Assert.Equal(TurnPhase.HomeSwap, engine.CurrentTurn.Phase);
        Assert.NotNull(engine.CurrentTurn.PendingHomeSwap);

        engine.ResolveHomeSwap(true);

        var player = engine.CurrentTurn.ActivePlayer;
        Assert.True(player.HasResolvedHomeSwap);
        Assert.Equal("Atlanta", player.HomeCity.Name);
        Assert.Equal("Atlanta", player.CurrentCity.Name);
        Assert.Equal("New York", player.Destination!.Name);
        Assert.Null(engine.CurrentTurn.PendingHomeSwap);
        Assert.Equal(TurnPhase.Roll, engine.CurrentTurn.Phase);
    }
}