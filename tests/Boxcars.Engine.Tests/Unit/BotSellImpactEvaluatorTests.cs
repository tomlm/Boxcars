using Boxcars.Data;
using Boxcars.Engine.Domain;
using Boxcars.Engine.Tests.Fixtures;
using Boxcars.GameEngine;
using Boxcars.Services;

namespace Boxcars.Engine.Tests.Unit;

public class BotSellImpactEvaluatorTests
{
    [Fact]
    public async Task CreateBotActionAsync_ForcedSale_SelectsLeastDamagingRailroad()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();
        var player = engine.CurrentTurn.ActivePlayer;
        engine.Players[1].Cash = 0;

        var firstRailroad = engine.Railroads[0];
        var secondRailroad = engine.Railroads[1];
        firstRailroad.Owner = player;
        secondRailroad.Owner = player;
        player.OwnedRailroads.Add(firstRailroad);
        player.OwnedRailroads.Add(secondRailroad);

        engine.CurrentTurn.Phase = TurnPhase.UseFees;
        engine.CurrentTurn.PendingFeeAmount = 6_000;
        engine.CurrentTurn.ForcedSaleState = new ForcedSaleState
        {
            AmountOwed = 6_000,
            CashBeforeFees = player.Cash,
            CashAfterLastSale = player.Cash,
            SalesCompletedCount = 0,
            CanPayNow = false,
            EliminationTriggered = false
        };

        var presenceService = new GamePresenceService();
        BotTurnServiceTestHarness.ConfigureDelegatedControl(
            presenceService,
            BotTurnServiceTestHarness.ActivePlayerUserId,
            BotTurnServiceTestHarness.ControllerUserId);

        var botDefinition = BotTurnServiceTestHarness.CreateBotDefinition();
        var service = BotTurnServiceTestHarness.CreateService(presenceService, botDefinition);
        var game = BotTurnServiceTestHarness.CreateAssignedGame(
            BotTurnServiceTestHarness.CreateSelections(
                BotTurnServiceTestHarness.ActivePlayerUserId,
                BotTurnServiceTestHarness.OtherPlayerUserId),
            BotTurnServiceTestHarness.ActivePlayerUserId,
            BotTurnServiceTestHarness.ControllerUserId,
            botDefinition.BotDefinitionId);

        var mapDefinition = GameEngineFixture.CreateTestMap();
        var coverageService = new NetworkCoverageService();
        var currentCoverage = coverageService.BuildSnapshot(mapDefinition, player.OwnedRailroads.Select(railroad => railroad.Index));
        var expectedRailroad = player.OwnedRailroads
            .Select(railroad => new
            {
                Railroad = railroad,
                Projected = coverageService.BuildProjectedSnapshotAfterSale(mapDefinition, player.OwnedRailroads.Select(owned => owned.Index), railroad.Index)
            })
            .OrderByDescending(candidate => (int)Math.Round((candidate.Projected.AccessibleDestinationPercent - currentCoverage.AccessibleDestinationPercent) * 10m, MidpointRounding.AwayFromZero))
            .ThenByDescending(candidate => (int)Math.Round((candidate.Projected.MonopolyDestinationPercent - currentCoverage.MonopolyDestinationPercent) * 10m, MidpointRounding.AwayFromZero))
            .ThenBy(candidate => candidate.Railroad.Name, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Railroad)
            .First();

        var action = await service.CreateBotActionAsync(game, engine, mapDefinition, CancellationToken.None);

        var sellAction = Assert.IsType<SellRailroadAction>(action);
        Assert.Equal(expectedRailroad.Index, sellAction.RailroadIndex);
        Assert.Equal(expectedRailroad.PurchasePrice / 2, sellAction.AmountReceived);
        Assert.NotNull(sellAction.BotMetadata);
        Assert.Equal("DeterministicSell", sellAction.BotMetadata!.DecisionSource);
        Assert.Equal(botDefinition.Name, sellAction.BotMetadata.BotName);
    }

    [Fact]
    public async Task CreateBotActionAsync_ForcedSale_WithEligibleBidder_StartsAuctionForLeastDamagingRailroad()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();
        var player = engine.CurrentTurn.ActivePlayer;

        var firstRailroad = engine.Railroads[0];
        var secondRailroad = engine.Railroads[1];
        firstRailroad.Owner = player;
        secondRailroad.Owner = player;
        player.OwnedRailroads.Add(firstRailroad);
        player.OwnedRailroads.Add(secondRailroad);

        engine.CurrentTurn.Phase = TurnPhase.UseFees;
        engine.CurrentTurn.PendingFeeAmount = 6_000;
        engine.CurrentTurn.ForcedSaleState = new ForcedSaleState
        {
            AmountOwed = 6_000,
            CashBeforeFees = player.Cash,
            CashAfterLastSale = player.Cash,
            SalesCompletedCount = 0,
            CanPayNow = false,
            EliminationTriggered = false
        };

        var presenceService = new GamePresenceService();
        BotTurnServiceTestHarness.ConfigureDelegatedControl(
            presenceService,
            BotTurnServiceTestHarness.ActivePlayerUserId,
            BotTurnServiceTestHarness.ControllerUserId);

        var botDefinition = BotTurnServiceTestHarness.CreateBotDefinition();
        var service = BotTurnServiceTestHarness.CreateService(presenceService, botDefinition);
        var game = BotTurnServiceTestHarness.CreateAssignedGame(
            BotTurnServiceTestHarness.CreateSelections(
                BotTurnServiceTestHarness.ActivePlayerUserId,
                BotTurnServiceTestHarness.OtherPlayerUserId),
            BotTurnServiceTestHarness.ActivePlayerUserId,
            BotTurnServiceTestHarness.ControllerUserId,
            botDefinition.BotDefinitionId);

        var mapDefinition = GameEngineFixture.CreateTestMap();
        var coverageService = new NetworkCoverageService();
        var currentCoverage = coverageService.BuildSnapshot(mapDefinition, player.OwnedRailroads.Select(railroad => railroad.Index));
        var expectedRailroad = player.OwnedRailroads
            .Select(railroad => new
            {
                Railroad = railroad,
                Projected = coverageService.BuildProjectedSnapshotAfterSale(mapDefinition, player.OwnedRailroads.Select(owned => owned.Index), railroad.Index)
            })
            .OrderByDescending(candidate => (int)Math.Round((candidate.Projected.AccessibleDestinationPercent - currentCoverage.AccessibleDestinationPercent) * 10m, MidpointRounding.AwayFromZero))
            .ThenByDescending(candidate => (int)Math.Round((candidate.Projected.MonopolyDestinationPercent - currentCoverage.MonopolyDestinationPercent) * 10m, MidpointRounding.AwayFromZero))
            .ThenBy(candidate => candidate.Railroad.Name, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Railroad)
            .First();

        var action = await service.CreateBotActionAsync(game, engine, mapDefinition, CancellationToken.None);

        var auctionAction = Assert.IsType<StartAuctionAction>(action);
        Assert.Equal(expectedRailroad.Index, auctionAction.RailroadIndex);
        Assert.NotNull(auctionAction.BotMetadata);
        Assert.Equal("DeterministicAuction", auctionAction.BotMetadata!.DecisionSource);
        Assert.Equal(botDefinition.Name, auctionAction.BotMetadata.BotName);
    }
}