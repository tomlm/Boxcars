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
        Assert.Equal(2, state.RegionChoicePhase.Options.Count);
        Assert.Equal("NE", state.RegionChoicePhase.Options[0].RegionCode);
        Assert.Equal(2, state.RegionChoicePhase.Options[0].EligibleCityCount);
        Assert.Equal(1.0m, state.RegionChoicePhase.Options[0].AccessibleDestinationPercent);
        Assert.Equal(0m, state.RegionChoicePhase.Options[0].MonopolyDestinationPercent);
        Assert.Equal("SE", state.RegionChoicePhase.Options[1].RegionCode);
        Assert.Equal(1.0m, state.RegionChoicePhase.Options[1].AccessibleDestinationPercent);
    }

    [Fact]
    public void BuildTurnViewState_DedicatedBotAssignment_ProjectsAiControllerMode()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();
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
            ]),
            BotAssignmentsJson = BotAssignmentSerialization.Serialize(
            [
                new BotAssignment
                {
                    GameId = "game-1",
                    PlayerUserId = "alice@example.com",
                    ControllerMode = SeatControllerModes.AI,
                    BotDefinitionId = "bot-1",
                    Status = BotAssignmentStatuses.Active
                }
            ])
        };

        var state = mapper.BuildTurnViewState(game, engine.ToSnapshot(), "bob@example.com", engine.MapDefinition);

        Assert.Equal(SeatControllerModes.AI, state.ActivePlayerControllerMode);
        Assert.False(state.IsCurrentUserActivePlayer);
    }
}