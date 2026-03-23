using Boxcars.Data;
using Boxcars.Engine.Domain;
using Boxcars.Engine.Persistence;
using Boxcars.Engine.Tests.Fixtures;
using Boxcars.Services;
using Boxcars.Services.Maps;

namespace Boxcars.Engine.Tests.Unit;

public class GameSettingsHomeSelectionFlowTests
{
    [Fact]
    public void BuildTurnViewState_HomeCityChoice_ProjectsSelectableHomeCityOptions()
    {
        var settings = GameSettings.Default with
        {
            HomeCityChoice = true,
            HomeSwapping = false
        };
        var map = GameEngineFixture.CreateTestMap();
        var random = GameEngineFixture.CreateDeterministicRandom();
        var engine = new global::Boxcars.Engine.Domain.GameEngine(map, GameEngineFixture.DefaultPlayerNames, random, settings);
        var mapper = CreateMapper();
        var playerStates = CreatePlayerStates();

        var state = mapper.BuildTurnViewState("game-1", playerStates, engine.ToSnapshot(), "alice@example.com", map);

        var phase = state.HomeCityChoicePhase;
        Assert.NotNull(phase);
        Assert.Equal(TurnPhase.HomeCityChoice.ToString(), state.TurnPhase);
        Assert.Null(state.HomeSwapPhase);
        Assert.Equal(0, phase.PlayerIndex);
        Assert.Equal("Alice", phase.PlayerName);
        Assert.Equal("NE", phase.RegionCode);
        Assert.Equal("New York", phase.CurrentHomeCityName);
        Assert.Equal(["Boston", "New York"], phase.Options.Select(option => option.CityName).ToArray());
        Assert.True(phase.Options.Single(option => option.CityName == "New York").IsCurrentSelection);
        Assert.True(phase.CanConfirm);
    }

    [Fact]
    public void BuildTurnViewState_HomeSwap_ProjectsInitialSwapDecision()
    {
        var settings = GameSettings.Default with
        {
            HomeCityChoice = false,
            HomeSwapping = true
        };
        var map = GameEngineFixture.CreateTestMap();
        var random = GameEngineFixture.CreateDeterministicRandom();
        var engine = new global::Boxcars.Engine.Domain.GameEngine(map, GameEngineFixture.DefaultPlayerNames, random, settings);
        var mapper = CreateMapper();
        var playerStates = CreatePlayerStates();

        random.QueueWeightedDraw(1);
        random.QueueWeightedDraw(1);
        engine.DrawDestination();

        var state = mapper.BuildTurnViewState("game-1", playerStates, engine.ToSnapshot(), "alice@example.com", map);

        var phase = state.HomeSwapPhase;
        Assert.NotNull(phase);
        Assert.Equal(TurnPhase.HomeSwap.ToString(), state.TurnPhase);
        Assert.Null(state.HomeCityChoicePhase);
        Assert.Equal(0, phase.PlayerIndex);
        Assert.Equal("Alice", phase.PlayerName);
        Assert.Equal("New York", phase.CurrentHomeCityName);
        Assert.Equal("Atlanta", phase.FirstDestinationCityName);
        Assert.True(phase.CanConfirm);
    }

    private static GameBoardStateMapper CreateMapper()
    {
        return new GameBoardStateMapper(
            new NetworkCoverageService(),
            new MapAnalysisService(new MapRouteService()),
            new PurchaseRecommendationService());
    }

    private static List<GamePlayerStateEntity> CreatePlayerStates()
    {
        return BotTurnServiceTestHarness.CreatePlayerStates(
        [
            new GamePlayerSelection { UserId = "alice@example.com", DisplayName = "Alice", Color = "#111111" },
            new GamePlayerSelection { UserId = "bob@example.com", DisplayName = "Bob", Color = "#222222" }
        ]);
    }
}