using Boxcars.Data;
using Boxcars.Engine.Domain;
using Boxcars.Engine.Tests.Fixtures;
using Boxcars.GameEngine;
using Boxcars.Services;

namespace Boxcars.Engine.Tests.Unit;

public class BotTurnResolutionTests
{
    [Fact]
    public async Task CreateBotActionAsync_RegionChoiceWithSingleEligibleRegion_ReturnsOnlyLegalChoiceAction()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();
        var presenceService = new GamePresenceService();
        BotTurnServiceTestHarness.ConfigureDelegatedControl(
            presenceService,
            BotTurnServiceTestHarness.ActivePlayerUserId,
            BotTurnServiceTestHarness.ControllerUserId);

        engine.CurrentTurn.Phase = TurnPhase.RegionChoice;
        engine.CurrentTurn.PendingRegionChoice = new PendingRegionChoice
        {
            PlayerIndex = engine.CurrentTurn.ActivePlayer.Index,
            CurrentCityName = engine.CurrentTurn.ActivePlayer.CurrentCity.Name,
            CurrentRegionCode = engine.CurrentTurn.ActivePlayer.CurrentCity.RegionCode,
            TriggeredByInitialRegionCode = engine.CurrentTurn.ActivePlayer.CurrentCity.RegionCode,
            EligibleRegionCodes = ["SE"],
            EligibleCityCountsByRegion = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["SE"] = 2
            }
        };

        var botDefinition = BotTurnServiceTestHarness.CreateBotDefinition();
        var service = BotTurnServiceTestHarness.CreateService(presenceService, botDefinition);
        var game = BotTurnServiceTestHarness.CreateAssignedGame(
            BotTurnServiceTestHarness.CreateSelections(
                BotTurnServiceTestHarness.ActivePlayerUserId,
                BotTurnServiceTestHarness.OtherPlayerUserId),
            BotTurnServiceTestHarness.ActivePlayerUserId,
            BotTurnServiceTestHarness.ControllerUserId,
            botDefinition.BotDefinitionId);

        var action = await service.CreateBotActionAsync(game, engine, GameEngineFixture.CreateTestMap(), CancellationToken.None);

        var regionAction = Assert.IsType<ChooseDestinationRegionAction>(action);
        Assert.Equal("SE", regionAction.SelectedRegionCode);
        Assert.Equal(BotTurnServiceTestHarness.ControllerUserId, regionAction.ActorUserId);
        Assert.NotNull(regionAction.BotMetadata);
        Assert.Equal("OnlyLegalChoice", regionAction.BotMetadata!.DecisionSource);
        Assert.Equal(botDefinition.Name, regionAction.BotMetadata.BotName);
    }

    [Fact]
    public async Task CreateBotActionAsync_PurchaseWithOnlyDeclineChoice_ReturnsDeclinePurchaseAction()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();
        engine.CurrentTurn.Phase = TurnPhase.Purchase;
        engine.CurrentTurn.PendingRegionChoice = null;
        engine.CurrentTurn.ActivePlayer.Cash = 0;

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

        var action = await service.CreateBotActionAsync(game, engine, GameEngineFixture.CreateTestMap(), CancellationToken.None);

        var declineAction = Assert.IsType<DeclinePurchaseAction>(action);
        Assert.Equal(BotTurnServiceTestHarness.ControllerUserId, declineAction.ActorUserId);
        Assert.NotNull(declineAction.BotMetadata);
        Assert.Equal("OnlyLegalChoice", declineAction.BotMetadata!.DecisionSource);
    }

    [Fact]
    public async Task CreateBotActionAsync_MovePhase_UsesSavedRouteSegments()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Move);

        var activeRoute = engine.CurrentTurn.ActivePlayer.ActiveRoute;
        Assert.NotNull(activeRoute);
        var expectedPoints = activeRoute.NodeIds
            .Take(Math.Min(engine.CurrentTurn.MovementRemaining, activeRoute.Segments.Count) + 1)
            .ToList();

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

        var action = await service.CreateBotActionAsync(game, engine, GameEngineFixture.CreateTestMap(), CancellationToken.None);

        var moveAction = Assert.IsType<MoveAction>(action);
        Assert.Equal(expectedPoints, moveAction.PointsTaken);
        Assert.NotEmpty(moveAction.SelectedSegmentKeys);
        Assert.NotNull(moveAction.BotMetadata);
        Assert.Equal("SuggestedRoute", moveAction.BotMetadata!.DecisionSource);
    }

    [Fact]
    public async Task CreateBotActionAsync_AuctionWithOnlyPassChoice_ReturnsAuctionPassAction()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine(GameEngineFixture.ThreePlayerNames, 3);
        engine.CurrentTurn.Phase = TurnPhase.Purchase;

        var seller = engine.CurrentTurn.ActivePlayer;
        var railroad = engine.Railroads[0];
        railroad.Owner = seller;
        seller.OwnedRailroads.Add(railroad);
        engine.AuctionRailroad(railroad);

        var bidderIndex = engine.CurrentTurn.AuctionState?.CurrentBidderPlayerIndex;
        Assert.True(bidderIndex.HasValue);
        engine.Players[bidderIndex!.Value].Cash = 0;

        var presenceService = new GamePresenceService();
        BotTurnServiceTestHarness.ConfigureDelegatedControl(
            presenceService,
            "bob@example.com",
            BotTurnServiceTestHarness.ControllerUserId);

        var botDefinition = BotTurnServiceTestHarness.CreateBotDefinition();
        var service = BotTurnServiceTestHarness.CreateService(presenceService, botDefinition);
        var game = BotTurnServiceTestHarness.CreateAssignedGame(
            BotTurnServiceTestHarness.CreateSelections(
                "seller@example.com",
                "bob@example.com",
                "charlie@example.com"),
            "bob@example.com",
            BotTurnServiceTestHarness.ControllerUserId,
            botDefinition.BotDefinitionId);

        var action = await service.CreateBotActionAsync(game, engine, GameEngineFixture.CreateTestMap(), CancellationToken.None);

        var passAction = Assert.IsType<AuctionPassAction>(action);
        Assert.Equal(BotTurnServiceTestHarness.ControllerUserId, passAction.ActorUserId);
        Assert.Equal(railroad.Index, passAction.RailroadIndex);
        Assert.NotNull(passAction.BotMetadata);
        Assert.Equal("OnlyLegalChoice", passAction.BotMetadata!.DecisionSource);
    }
}