using Boxcars.Data;
using Boxcars.Engine.Domain;
using Boxcars.Engine.Tests.Fixtures;
using Boxcars.Services;
using Boxcars.Services.Maps;
using Microsoft.Extensions.Options;

namespace Boxcars.Engine.Tests.Unit;

public class RegionChoiceStateMapperTests
{
    [Fact]
    public void BuildTurnViewState_PendingRegionChoice_ProjectsRegionChoicePhase()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();

        random.QueueWeightedDraw(0);
        random.QueueWeightedDraw(1);
        engine.DrawDestination();

        var mapper = new GameBoardStateMapper(
            new NetworkCoverageService(),
            new MapAnalysisService(new MapRouteService()),
            new PurchaseRecommendationService(),
            Options.Create(new PurchaseRulesOptions()));

        var game = new GameEntity
        {
            PartitionKey = "game-1",
            GameId = "game-1",
            PlayersJson = GamePlayerSelectionSerialization.Serialize(
            [
                new GamePlayerSelection { UserId = "alice@example.com", DisplayName = "Alice", Color = "#111111" },
                new GamePlayerSelection { UserId = "bob@example.com", DisplayName = "Bob", Color = "#222222" }
            ])
        };

        var state = mapper.BuildTurnViewState(game, engine.ToSnapshot(), "alice@example.com", engine.MapDefinition);

        Assert.NotNull(state.RegionChoicePhase);
        Assert.Equal(TurnPhase.RegionChoice.ToString(), state.TurnPhase);
        Assert.Equal(0, state.RegionChoicePhase!.PlayerIndex);
        Assert.Equal("New York", state.RegionChoicePhase.CurrentCityName);
        Assert.Equal("NE", state.RegionChoicePhase.CurrentRegionCode);
        Assert.Equal("Northeast", state.RegionChoicePhase.CurrentRegionName);
        Assert.Single(state.RegionChoicePhase.Options);
        Assert.Equal("SE", state.RegionChoicePhase.Options[0].RegionCode);
        Assert.Equal(2, state.RegionChoicePhase.Options[0].EligibleCityCount);
        Assert.Equal(0m, state.RegionChoicePhase.Options[0].AccessibleDestinationPercent);
        Assert.Equal(0m, state.RegionChoicePhase.Options[0].MonopolyDestinationPercent);
    }
}