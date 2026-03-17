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
        Assert.Equal(BotTurnServiceTestHarness.ServerActorUserId, regionAction.ActorUserId);
        Assert.NotNull(regionAction.BotMetadata);
        Assert.Equal("OnlyLegalChoice", regionAction.BotMetadata!.DecisionSource);
        Assert.Equal(botDefinition.Name, regionAction.BotMetadata.BotName);
    }

    [Fact]
    public async Task CreateBotActionAsync_DedicatedBotSeatWithoutDelegatedControl_ReturnsServerAuthoredAction()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();
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

        var presenceService = new GamePresenceService();
        var botDefinition = BotTurnServiceTestHarness.CreateBotDefinition(
            botDefinitionId: BotTurnServiceTestHarness.ActivePlayerUserId,
            name: "Dedicated Bot");
        var service = BotTurnServiceTestHarness.CreateService(presenceService, botDefinition);
        var game = BotTurnServiceTestHarness.CreateDedicatedBotSeatGame(
            BotTurnServiceTestHarness.CreateSelections(
                BotTurnServiceTestHarness.ActivePlayerUserId,
                BotTurnServiceTestHarness.OtherPlayerUserId),
            BotTurnServiceTestHarness.ActivePlayerUserId,
            botDefinition.BotDefinitionId);

        var action = await service.CreateBotActionAsync(game, engine, GameEngineFixture.CreateTestMap(), CancellationToken.None);

        var regionAction = Assert.IsType<ChooseDestinationRegionAction>(action);
        Assert.Equal(BotTurnServiceTestHarness.ServerActorUserId, regionAction.ActorUserId);
        Assert.Equal("OnlyLegalChoice", regionAction.BotMetadata!.DecisionSource);
    }

    [Fact]
    public async Task EnsureBotSeatAssignmentsAsync_LegacyBotSeatWithoutAssignment_CreatesDedicatedAssignmentOnce()
    {
        var presenceService = new GamePresenceService();
        var botDefinition = BotTurnServiceTestHarness.CreateBotDefinition(
            botDefinitionId: BotTurnServiceTestHarness.ActivePlayerUserId,
            name: "Dedicated Bot");
        var service = BotTurnServiceTestHarness.CreateService(presenceService, botDefinition);
        var game = new GameEntity
        {
            PartitionKey = BotTurnServiceTestHarness.GameId,
            RowKey = "GAME",
            GameId = BotTurnServiceTestHarness.GameId,
            PlayersJson = GamePlayerSelectionSerialization.Serialize(
                BotTurnServiceTestHarness.CreateSelections(
                    BotTurnServiceTestHarness.ActivePlayerUserId,
                    BotTurnServiceTestHarness.OtherPlayerUserId))
        };

        await service.EnsureBotSeatAssignmentsAsync(
            game,
            GamePlayerSelectionSerialization.Deserialize(game.PlayersJson),
            BotTurnServiceTestHarness.ControllerUserId,
            CancellationToken.None);
        await service.EnsureBotSeatAssignmentsAsync(
            game,
            GamePlayerSelectionSerialization.Deserialize(game.PlayersJson),
            BotTurnServiceTestHarness.ControllerUserId,
            CancellationToken.None);

        var assignments = service.GetAssignments(game)
            .Where(assignment => string.Equals(assignment.PlayerUserId, BotTurnServiceTestHarness.ActivePlayerUserId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var activeAssignment = Assert.Single(assignments.Where(assignment => string.Equals(assignment.Status, BotAssignmentStatuses.Active, StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(SeatControllerModes.AiBotSeat, activeAssignment.ControllerMode);
        Assert.Equal(BotTurnServiceTestHarness.ActivePlayerUserId, activeAssignment.BotDefinitionId);
        Assert.Single(assignments);
    }

    [Fact]
    public async Task CreateBotActionAsync_DedicatedBotSeat_UsesAssignedBotDefinitionMetadata()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();
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

        var presenceService = new GamePresenceService();
        var assignedBot = BotTurnServiceTestHarness.CreateBotDefinition("shared-bot", "Shared Library Bot");
        var seatScopedBot = BotTurnServiceTestHarness.CreateBotDefinition(
            BotTurnServiceTestHarness.ActivePlayerUserId,
            "Seat Scoped Bot");
        var service = BotTurnServiceTestHarness.CreateService(presenceService, assignedBot, seatScopedBot);
        var game = BotTurnServiceTestHarness.CreateDedicatedBotSeatGame(
            BotTurnServiceTestHarness.CreateSelections(
                BotTurnServiceTestHarness.ActivePlayerUserId,
                BotTurnServiceTestHarness.OtherPlayerUserId),
            BotTurnServiceTestHarness.ActivePlayerUserId,
            assignedBot.BotDefinitionId);

        var action = await service.CreateBotActionAsync(game, engine, GameEngineFixture.CreateTestMap(), CancellationToken.None);

        var regionAction = Assert.IsType<ChooseDestinationRegionAction>(action);
        Assert.NotNull(regionAction.BotMetadata);
        Assert.Equal("Shared Library Bot", regionAction.BotMetadata!.BotName);
        Assert.NotEqual("Seat Scoped Bot", regionAction.BotMetadata.BotName);
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
        Assert.Equal(BotTurnServiceTestHarness.ServerActorUserId, declineAction.ActorUserId);
        Assert.NotNull(declineAction.BotMetadata);
        Assert.Equal("OnlyLegalChoice", declineAction.BotMetadata!.DecisionSource);
    }

    [Fact]
    public async Task CreateBotActionAsync_PurchaseOpenAiFailure_FallsBackToDeclinePurchase()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();
        engine.CurrentTurn.Phase = TurnPhase.Purchase;
        engine.CurrentTurn.PendingRegionChoice = null;

        var presenceService = new GamePresenceService();
        var botDefinition = BotTurnServiceTestHarness.CreateBotDefinition(
            botDefinitionId: BotTurnServiceTestHarness.ActivePlayerUserId,
            name: "Fallback Bot");
        var service = BotTurnServiceTestHarness.CreateService(presenceService, botDefinition);
        var game = BotTurnServiceTestHarness.CreateDedicatedBotSeatGame(
            BotTurnServiceTestHarness.CreateSelections(
                BotTurnServiceTestHarness.ActivePlayerUserId,
                BotTurnServiceTestHarness.OtherPlayerUserId),
            BotTurnServiceTestHarness.ActivePlayerUserId,
            botDefinition.BotDefinitionId);

        var action = await service.CreateBotActionAsync(game, engine, GameEngineFixture.CreateTestMap(), CancellationToken.None);

        var declineAction = Assert.IsType<DeclinePurchaseAction>(action);
        Assert.Equal(BotTurnServiceTestHarness.ServerActorUserId, declineAction.ActorUserId);
        Assert.NotNull(declineAction.BotMetadata);
        Assert.Equal("Fallback", declineAction.BotMetadata!.DecisionSource);
        Assert.Equal("OpenAI request failed with status 500.", declineAction.BotMetadata.FallbackReason);
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
    public async Task CreateBotActionAsync_GhostAssignmentWithoutDelegatedControl_ReturnsNull()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();
        engine.CurrentTurn.Phase = TurnPhase.Purchase;

        var presenceService = new GamePresenceService();
        var botDefinition = BotTurnServiceTestHarness.CreateBotDefinition(
            botDefinitionId: BotTurnServiceTestHarness.ActivePlayerUserId,
            name: "Ghost Bot");
        var service = BotTurnServiceTestHarness.CreateService(presenceService, botDefinition);
        var game = BotTurnServiceTestHarness.CreateGhostControlledGame(
            BotTurnServiceTestHarness.CreateSelections(
                BotTurnServiceTestHarness.ActivePlayerUserId,
                BotTurnServiceTestHarness.OtherPlayerUserId),
            BotTurnServiceTestHarness.ActivePlayerUserId,
            BotTurnServiceTestHarness.ControllerUserId,
            botDefinition.BotDefinitionId);

        var action = await service.CreateBotActionAsync(game, engine, GameEngineFixture.CreateTestMap(), CancellationToken.None);

        Assert.Null(action);
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
        Assert.Equal(BotTurnServiceTestHarness.ServerActorUserId, passAction.ActorUserId);
        Assert.Equal(railroad.Index, passAction.RailroadIndex);
        Assert.NotNull(passAction.BotMetadata);
        Assert.Equal("OnlyLegalChoice", passAction.BotMetadata!.DecisionSource);
    }
}