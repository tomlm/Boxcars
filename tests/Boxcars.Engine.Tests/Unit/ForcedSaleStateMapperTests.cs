using Boxcars.Data;
using Boxcars.Engine.Domain;
using Boxcars.Engine.Tests.Fixtures;
using Boxcars.Engine.Tests.TestDoubles;
using Boxcars.Services;
using Boxcars.Services.Maps;
using Microsoft.Extensions.Options;

namespace Boxcars.Engine.Tests.Unit;

public class ForcedSaleStateMapperTests
{
    [Fact]
    public void BuildTurnViewState_ForcedSaleSelection_ProjectsNetworkAfterSale()
    {
        var mapDefinition = GameEngineFixture.CreateTestMap();
        var random = GameEngineFixture.CreateDeterministicRandom(2);
        var engine = new Boxcars.Engine.Domain.GameEngine(mapDefinition, GameEngineFixture.DefaultPlayerNames, random);
        var player = engine.CurrentTurn.ActivePlayer;
        var firstRailroad = engine.Railroads[0];
        var secondRailroad = engine.Railroads[1];

        firstRailroad.Owner = player;
        secondRailroad.Owner = player;
        player.OwnedRailroads.Add(firstRailroad);
        player.OwnedRailroads.Add(secondRailroad);
        player.Cash = 500;
        engine.CurrentTurn.RailroadsRiddenThisTurn.Add(secondRailroad.Index);

        engine.CurrentTurn.Phase = TurnPhase.Purchase;
        engine.DeclinePurchase();

        var snapshot = engine.ToSnapshot();
        snapshot.Turn.SelectedRailroadForSaleIndex = firstRailroad.Index;

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
                new GamePlayerSelection { UserId = "alice@example.com", DisplayName = player.Name, Color = "#111111" },
                new GamePlayerSelection { UserId = "bob@example.com", DisplayName = engine.Players[1].Name, Color = "#222222" }
            ])
        };

        var state = mapper.BuildTurnViewState(game, snapshot, "alice@example.com", mapDefinition);

        Assert.NotNull(state.ForcedSalePhase);
        Assert.NotNull(state.ForcedSalePhase!.ProjectedNetworkAfterSale);
        Assert.True(state.ForcedSalePhase.ProjectedNetworkAfterSale!.AccessibleCityPercent < state.ForcedSalePhase.CurrentNetwork!.AccessibleCityPercent);
        Assert.NotNull(state.ForcedSalePhase.NetworkTab.SelectedRailroadImpact);
        Assert.Contains(state.ForcedSalePhase.NetworkTab.RailroadSummaries, summary => summary.RailroadIndex == firstRailroad.Index && summary.AccessPercentIfSold.HasValue);
    }
}