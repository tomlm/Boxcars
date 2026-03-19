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
    public async Task CreateBotActionAsync_DisconnectedHumanWithoutAssignment_UsesPlayerStrategyProfile()
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
        presenceService.SetMockConnectionState(BotTurnServiceTestHarness.GameId, BotTurnServiceTestHarness.ActivePlayerUserId, isConnected: false);
        var playerProfile = BotTurnServiceTestHarness.CreateUser(
            BotTurnServiceTestHarness.ActivePlayerUserId,
            name: "Alice",
            strategyText: "Prefer the only legal region.");
        var service = BotTurnServiceTestHarness.CreateServiceWithUsers(presenceService, [playerProfile]);
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

        var action = await service.CreateBotActionAsync(game, engine, GameEngineFixture.CreateTestMap(), CancellationToken.None);

        var regionAction = Assert.IsType<ChooseDestinationRegionAction>(action);
        Assert.Equal("SE", regionAction.SelectedRegionCode);
        Assert.Equal(BotTurnServiceTestHarness.ServerActorUserId, regionAction.ActorUserId);
        Assert.NotNull(regionAction.BotMetadata);
        Assert.Equal("OnlyLegalChoice", regionAction.BotMetadata!.DecisionSource);
        Assert.Equal("Alice", regionAction.BotMetadata.BotName);
    }

    [Fact]
    public async Task CreateBotActionAsync_GhostAssignmentWithoutDelegatedControl_UsesPlayerStrategyProfile()
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
        presenceService.SetMockConnectionState(BotTurnServiceTestHarness.GameId, BotTurnServiceTestHarness.ActivePlayerUserId, isConnected: false);
        var playerProfile = BotTurnServiceTestHarness.CreateUser(
            BotTurnServiceTestHarness.ActivePlayerUserId,
            name: "Alice",
            strategyText: "Prefer the only legal region.");
        var service = BotTurnServiceTestHarness.CreateServiceWithUsers(presenceService, [playerProfile]);
        var game = BotTurnServiceTestHarness.CreateGhostControlledGame(
            BotTurnServiceTestHarness.CreateSelections(
                BotTurnServiceTestHarness.ActivePlayerUserId,
                BotTurnServiceTestHarness.OtherPlayerUserId),
            BotTurnServiceTestHarness.ActivePlayerUserId,
            BotTurnServiceTestHarness.ControllerUserId,
            "legacy-ghost-definition");

        var action = await service.CreateBotActionAsync(game, engine, GameEngineFixture.CreateTestMap(), CancellationToken.None);

        var regionAction = Assert.IsType<ChooseDestinationRegionAction>(action);
        Assert.Equal("SE", regionAction.SelectedRegionCode);
        Assert.Equal(BotTurnServiceTestHarness.ServerActorUserId, regionAction.ActorUserId);
        Assert.NotNull(regionAction.BotMetadata);
        Assert.Equal("OnlyLegalChoice", regionAction.BotMetadata!.DecisionSource);
        Assert.Equal("Alice", regionAction.BotMetadata.BotName);
    }

    [Fact]
    public async Task CreateBotActionAsync_AuctionWithoutAffordableBid_ReturnsAuctionDropOutAction()
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

        var botDefinition = BotTurnServiceTestHarness.CreateBotDefinition(
            botDefinitionId: "bob@example.com",
            name: "Ghost Strategy");
        var service = BotTurnServiceTestHarness.CreateService(presenceService, botDefinition);
        var game = BotTurnServiceTestHarness.CreateGhostControlledGame(
            BotTurnServiceTestHarness.CreateSelections(
                "seller@example.com",
                "bob@example.com",
                "charlie@example.com"),
            "bob@example.com",
            BotTurnServiceTestHarness.ControllerUserId,
            botDefinition.BotDefinitionId);

        var action = await service.CreateBotActionAsync(game, engine, GameEngineFixture.CreateTestMap(), CancellationToken.None);

        var dropOutAction = Assert.IsType<AuctionDropOutAction>(action);
        Assert.Equal(BotTurnServiceTestHarness.ServerActorUserId, dropOutAction.ActorUserId);
        Assert.Equal(railroad.Index, dropOutAction.RailroadIndex);
        Assert.NotNull(dropOutAction.BotMetadata);
        Assert.Equal("OnlyLegalChoice", dropOutAction.BotMetadata!.DecisionSource);
    }

    [Fact]
    public async Task CreateBotActionAsync_AuctionWithAffordableBid_FallbackPrefersMinimumBid()
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

        var bidder = engine.Players[bidderIndex!.Value];
        bidder.Cash = railroad.PurchasePrice;

        var presenceService = new GamePresenceService();
        BotTurnServiceTestHarness.ConfigureDelegatedControl(
            presenceService,
            "bob@example.com",
            BotTurnServiceTestHarness.ControllerUserId);

        var botDefinition = BotTurnServiceTestHarness.CreateBotDefinition(
            botDefinitionId: "bob@example.com",
            name: "Ghost Strategy");
        var service = BotTurnServiceTestHarness.CreateService(presenceService, botDefinition);
        var game = BotTurnServiceTestHarness.CreateGhostControlledGame(
            BotTurnServiceTestHarness.CreateSelections(
                "seller@example.com",
                "bob@example.com",
                "charlie@example.com"),
            "bob@example.com",
            BotTurnServiceTestHarness.ControllerUserId,
            botDefinition.BotDefinitionId);

        var action = await service.CreateBotActionAsync(game, engine, GameEngineFixture.CreateTestMap(), CancellationToken.None);

        var bidAction = Assert.IsType<BidAction>(action);
        Assert.Equal(BotTurnServiceTestHarness.ServerActorUserId, bidAction.ActorUserId);
        Assert.Equal(railroad.Index, bidAction.RailroadIndex);
        Assert.Equal(engine.CurrentTurn.AuctionState!.StartingPrice, bidAction.AmountBid);
        Assert.NotNull(bidAction.BotMetadata);
        Assert.Equal("Fallback", bidAction.BotMetadata!.DecisionSource);
    }

    [Fact]
    public async Task CreateBotActionAsync_AuctionCachesCeilingAndReusesItWithoutSecondOpenAiCall()
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

        var bidder = engine.Players[bidderIndex!.Value];
        bidder.Cash = railroad.PurchasePrice;
        var startingPrice = engine.CurrentTurn.AuctionState!.StartingPrice;
        var ceiling = startingPrice + global::Boxcars.Engine.Domain.GameEngine.AuctionBidIncrement;

        var presenceService = new GamePresenceService();
        BotTurnServiceTestHarness.ConfigureDelegatedControl(
            presenceService,
            "bob@example.com",
            BotTurnServiceTestHarness.ControllerUserId);

        var botDefinition = BotTurnServiceTestHarness.CreateBotDefinition(
            botDefinitionId: "bob@example.com",
            name: "Ghost Strategy");
        var (service, handler) = BotTurnServiceTestHarness.CreateServiceWithOpenAiSelection(
            presenceService,
            $"auction-cap:{ceiling}",
            botDefinition);
        var game = BotTurnServiceTestHarness.CreateGhostControlledGame(
            BotTurnServiceTestHarness.CreateSelections(
                "seller@example.com",
                "bob@example.com",
                "charlie@example.com"),
            "bob@example.com",
            BotTurnServiceTestHarness.ControllerUserId,
            botDefinition.BotDefinitionId);

        var firstAction = Assert.IsType<BidAction>(await service.CreateBotActionAsync(game, engine, GameEngineFixture.CreateTestMap(), CancellationToken.None));

        Assert.Equal(startingPrice, firstAction.AmountBid);
        Assert.Equal(1, handler.RequestCount);

        var cachedAssignment = Assert.Single(BotAssignmentSerialization.Deserialize(game.BotAssignmentsJson));
        Assert.Equal(engine.ToSnapshot().TurnNumber, cachedAssignment.AuctionPlanTurnNumber);
        Assert.Equal(railroad.Index, cachedAssignment.AuctionPlanRailroadIndex);
        Assert.Equal(startingPrice, cachedAssignment.AuctionPlanStartingPrice);
        Assert.Equal(ceiling, cachedAssignment.AuctionPlanMaximumBid);

        var auctionState = engine.CurrentTurn.AuctionState!;
        engine.CurrentTurn.AuctionState = new AuctionState
        {
            RailroadIndex = auctionState.RailroadIndex,
            RailroadName = auctionState.RailroadName,
            SellerPlayerIndex = auctionState.SellerPlayerIndex,
            SellerPlayerName = auctionState.SellerPlayerName,
            StartingPrice = auctionState.StartingPrice,
            CurrentBid = startingPrice,
            LastBidderPlayerIndex = 2,
            CurrentBidderPlayerIndex = bidderIndex.Value,
            RoundNumber = auctionState.RoundNumber,
            ConsecutiveNoBidTurnCount = auctionState.ConsecutiveNoBidTurnCount,
            Status = auctionState.Status,
            Participants = auctionState.Participants
        };

        var secondAction = Assert.IsType<BidAction>(await service.CreateBotActionAsync(game, engine, GameEngineFixture.CreateTestMap(), CancellationToken.None));

        Assert.Equal(ceiling, secondAction.AmountBid);
        Assert.Equal(1, handler.RequestCount);
        Assert.NotNull(secondAction.BotMetadata);
        Assert.Equal("AuctionPlan", secondAction.BotMetadata!.DecisionSource);
    }

    [Fact]
    public async Task CreateBotActionAsync_AuctionDropsOutWhenMinimumExceedsCachedCeilingWithoutSecondOpenAiCall()
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

        var bidder = engine.Players[bidderIndex!.Value];
        bidder.Cash = railroad.PurchasePrice;
        var startingPrice = engine.CurrentTurn.AuctionState!.StartingPrice;

        var presenceService = new GamePresenceService();
        BotTurnServiceTestHarness.ConfigureDelegatedControl(
            presenceService,
            "bob@example.com",
            BotTurnServiceTestHarness.ControllerUserId);

        var botDefinition = BotTurnServiceTestHarness.CreateBotDefinition(
            botDefinitionId: "bob@example.com",
            name: "Ghost Strategy");
        var (service, handler) = BotTurnServiceTestHarness.CreateServiceWithOpenAiSelection(
            presenceService,
            $"auction-cap:{startingPrice}",
            botDefinition);
        var game = BotTurnServiceTestHarness.CreateGhostControlledGame(
            BotTurnServiceTestHarness.CreateSelections(
                "seller@example.com",
                "bob@example.com",
                "charlie@example.com"),
            "bob@example.com",
            BotTurnServiceTestHarness.ControllerUserId,
            botDefinition.BotDefinitionId);

        var firstAction = Assert.IsType<BidAction>(await service.CreateBotActionAsync(game, engine, GameEngineFixture.CreateTestMap(), CancellationToken.None));
        Assert.Equal(startingPrice, firstAction.AmountBid);
        Assert.Equal(1, handler.RequestCount);

        var auctionState = engine.CurrentTurn.AuctionState!;
        engine.CurrentTurn.AuctionState = new AuctionState
        {
            RailroadIndex = auctionState.RailroadIndex,
            RailroadName = auctionState.RailroadName,
            SellerPlayerIndex = auctionState.SellerPlayerIndex,
            SellerPlayerName = auctionState.SellerPlayerName,
            StartingPrice = auctionState.StartingPrice,
            CurrentBid = startingPrice + global::Boxcars.Engine.Domain.GameEngine.AuctionBidIncrement,
            LastBidderPlayerIndex = 2,
            CurrentBidderPlayerIndex = bidderIndex.Value,
            RoundNumber = auctionState.RoundNumber,
            ConsecutiveNoBidTurnCount = auctionState.ConsecutiveNoBidTurnCount,
            Status = auctionState.Status,
            Participants = auctionState.Participants
        };

        var secondAction = Assert.IsType<AuctionDropOutAction>(await service.CreateBotActionAsync(game, engine, GameEngineFixture.CreateTestMap(), CancellationToken.None));

        Assert.Equal(railroad.Index, secondAction.RailroadIndex);
        Assert.Equal(1, handler.RequestCount);
        Assert.NotNull(secondAction.BotMetadata);
        Assert.Equal("AuctionPlan", secondAction.BotMetadata!.DecisionSource);
    }

    [Fact]
    public async Task TryResolveAllAiAuctionAsync_WithOnlyAiBidders_AwardsRailroadInOneStep()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine(GameEngineFixture.ThreePlayerNames, 3);
        engine.CurrentTurn.Phase = TurnPhase.Purchase;

        var seller = engine.CurrentTurn.ActivePlayer;
        var bidderOne = engine.Players[1];
        var bidderTwo = engine.Players[2];
        var railroad = engine.Railroads[0];
        railroad.Owner = seller;
        seller.OwnedRailroads.Add(railroad);
        bidderOne.Cash = railroad.PurchasePrice;
        bidderTwo.Cash = railroad.PurchasePrice;

        engine.AuctionRailroad(railroad);
        var startingPrice = engine.CurrentTurn.AuctionState!.StartingPrice;
        var turnNumber = engine.ToSnapshot().TurnNumber;

        var presenceService = new GamePresenceService();
        var bidderOneDefinition = BotTurnServiceTestHarness.CreateBotDefinition("bob@example.com", "Bob Bot");
        var bidderTwoDefinition = BotTurnServiceTestHarness.CreateBotDefinition("charlie@example.com", "Charlie Bot");
        var service = BotTurnServiceTestHarness.CreateService(presenceService, bidderOneDefinition, bidderTwoDefinition);
        var game = new GameEntity
        {
            PartitionKey = BotTurnServiceTestHarness.GameId,
            RowKey = "GAME",
            GameId = BotTurnServiceTestHarness.GameId,
            PlayersJson = GamePlayerSelectionSerialization.Serialize(
                BotTurnServiceTestHarness.CreateSelections(
                    "seller@example.com",
                    "bob@example.com",
                    "charlie@example.com")),
            BotAssignmentsJson = BotAssignmentSerialization.Serialize(
            [
                BotTurnServiceTestHarness.CreateDedicatedBotAssignment("bob@example.com", bidderOneDefinition.BotDefinitionId) with
                {
                    AuctionPlanTurnNumber = turnNumber,
                    AuctionPlanRailroadIndex = railroad.Index,
                    AuctionPlanStartingPrice = startingPrice,
                    AuctionPlanMaximumBid = startingPrice
                },
                BotTurnServiceTestHarness.CreateDedicatedBotAssignment("charlie@example.com", bidderTwoDefinition.BotDefinitionId) with
                {
                    AuctionPlanTurnNumber = turnNumber,
                    AuctionPlanRailroadIndex = railroad.Index,
                    AuctionPlanStartingPrice = startingPrice,
                    AuctionPlanMaximumBid = startingPrice + global::Boxcars.Engine.Domain.GameEngine.AuctionBidIncrement
                }
            ])
        };

        var summaryAction = await service.TryResolveAllAiAuctionAsync(game, engine, GameEngineFixture.CreateTestMap(), CancellationToken.None);

        var bidAction = Assert.IsType<BidAction>(summaryAction);
        Assert.Equal("All AI bidders", bidAction.PlayerId);
        Assert.Equal(startingPrice + global::Boxcars.Engine.Domain.GameEngine.AuctionBidIncrement, bidAction.AmountBid);
        Assert.Null(engine.CurrentTurn.AuctionState);
        Assert.Equal(bidderTwo, railroad.Owner);
        Assert.DoesNotContain(railroad, seller.OwnedRailroads);
        Assert.Contains(railroad, bidderTwo.OwnedRailroads);
        Assert.Equal("AllAiAuctionResolution", bidAction.BotMetadata!.DecisionSource);
    }

    [Fact]
    public async Task TryResolveAllAiAuctionAsync_WhenAllAiBiddersDropOut_SellsRailroadToBank()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine(GameEngineFixture.ThreePlayerNames, 3);
        engine.CurrentTurn.Phase = TurnPhase.Purchase;

        var seller = engine.CurrentTurn.ActivePlayer;
        var railroad = engine.Railroads[0];
        railroad.Owner = seller;
        seller.OwnedRailroads.Add(railroad);
        engine.AuctionRailroad(railroad);
        var startingPrice = engine.CurrentTurn.AuctionState!.StartingPrice;
        var turnNumber = engine.ToSnapshot().TurnNumber;

        var presenceService = new GamePresenceService();
        var bidderOneDefinition = BotTurnServiceTestHarness.CreateBotDefinition("bob@example.com", "Bob Bot");
        var bidderTwoDefinition = BotTurnServiceTestHarness.CreateBotDefinition("charlie@example.com", "Charlie Bot");
        var service = BotTurnServiceTestHarness.CreateService(presenceService, bidderOneDefinition, bidderTwoDefinition);
        var game = new GameEntity
        {
            PartitionKey = BotTurnServiceTestHarness.GameId,
            RowKey = "GAME",
            GameId = BotTurnServiceTestHarness.GameId,
            PlayersJson = GamePlayerSelectionSerialization.Serialize(
                BotTurnServiceTestHarness.CreateSelections(
                    "seller@example.com",
                    "bob@example.com",
                    "charlie@example.com")),
            BotAssignmentsJson = BotAssignmentSerialization.Serialize(
            [
                BotTurnServiceTestHarness.CreateDedicatedBotAssignment("bob@example.com", bidderOneDefinition.BotDefinitionId) with
                {
                    AuctionPlanTurnNumber = turnNumber,
                    AuctionPlanRailroadIndex = railroad.Index,
                    AuctionPlanStartingPrice = startingPrice,
                    AuctionPlanMaximumBid = 0
                },
                BotTurnServiceTestHarness.CreateDedicatedBotAssignment("charlie@example.com", bidderTwoDefinition.BotDefinitionId) with
                {
                    AuctionPlanTurnNumber = turnNumber,
                    AuctionPlanRailroadIndex = railroad.Index,
                    AuctionPlanStartingPrice = startingPrice,
                    AuctionPlanMaximumBid = 0
                }
            ])
        };

        var summaryAction = await service.TryResolveAllAiAuctionAsync(game, engine, GameEngineFixture.CreateTestMap(), CancellationToken.None);

        var dropOutAction = Assert.IsType<AuctionDropOutAction>(summaryAction);
        Assert.Equal("All AI bidders", dropOutAction.PlayerId);
        Assert.Null(engine.CurrentTurn.AuctionState);
        Assert.Null(railroad.Owner);
        Assert.DoesNotContain(railroad, seller.OwnedRailroads);
        Assert.Equal("AllAiAuctionResolution", dropOutAction.BotMetadata!.DecisionSource);
    }
}