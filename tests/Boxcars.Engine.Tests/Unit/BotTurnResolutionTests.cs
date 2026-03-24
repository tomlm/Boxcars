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
        presenceService.SetMockConnectionState(BotTurnServiceTestHarness.GameId, BotTurnServiceTestHarness.ActivePlayerUserId, isConnected: false);
        var playerProfile = BotTurnServiceTestHarness.CreateUser(
            BotTurnServiceTestHarness.ActivePlayerUserId,
            name: "Alice",
            strategyText: "Prefer the only legal region.");
        var service = BotTurnServiceTestHarness.CreateServiceWithUsers(presenceService, [playerProfile], botDefinition);
        var playerStates = BotTurnServiceTestHarness.CreateAssignedPlayerStates(
            BotTurnServiceTestHarness.CreateSelections(
                BotTurnServiceTestHarness.ActivePlayerUserId,
                BotTurnServiceTestHarness.OtherPlayerUserId),
            BotTurnServiceTestHarness.ActivePlayerUserId,
            BotTurnServiceTestHarness.ControllerUserId,
            botDefinition.BotDefinitionId);

        var action = await service.CreateBotActionAsync(BotTurnServiceTestHarness.GameId, playerStates.Cast<GameSeatState>().ToList(), engine, GameEngineFixture.CreateTestMap(), CancellationToken.None);

        var regionAction = Assert.IsType<ChooseDestinationRegionAction>(action);
        Assert.Equal("SE", regionAction.SelectedRegionCode);
        Assert.Equal(BotTurnServiceTestHarness.ServerActorUserId, regionAction.ActorUserId);
        Assert.NotNull(regionAction.BotMetadata);
        Assert.Equal("OnlyLegalChoice", regionAction.BotMetadata!.DecisionSource);
        Assert.Equal("Alice", regionAction.BotMetadata.BotName);
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
        var playerStates = BotTurnServiceTestHarness.CreateDedicatedBotSeatPlayerStates(
            BotTurnServiceTestHarness.CreateSelections(
                BotTurnServiceTestHarness.ActivePlayerUserId,
                BotTurnServiceTestHarness.OtherPlayerUserId),
            BotTurnServiceTestHarness.ActivePlayerUserId,
            botDefinition.BotDefinitionId);

        var action = await service.CreateBotActionAsync(BotTurnServiceTestHarness.GameId, playerStates.Cast<GameSeatState>().ToList(), engine, GameEngineFixture.CreateTestMap(), CancellationToken.None);

        var regionAction = Assert.IsType<ChooseDestinationRegionAction>(action);
        Assert.Equal(BotTurnServiceTestHarness.ServerActorUserId, regionAction.ActorUserId);
        Assert.Equal("OnlyLegalChoice", regionAction.BotMetadata!.DecisionSource);
    }

    [Fact]
    public async Task EnsureBotSeatControlStatesAsync_LegacyBotSeatWithoutBotControl_CreatesDedicatedBotControlOnce()
    {
        var presenceService = new GamePresenceService();
        var botDefinition = BotTurnServiceTestHarness.CreateBotDefinition(
            botDefinitionId: BotTurnServiceTestHarness.ActivePlayerUserId,
            name: "Dedicated Bot");
        var service = BotTurnServiceTestHarness.CreateService(presenceService, botDefinition);
        var playerStates = BotTurnServiceTestHarness.CreatePlayerStates(
            BotTurnServiceTestHarness.CreateSelections(
                BotTurnServiceTestHarness.ActivePlayerUserId,
                BotTurnServiceTestHarness.OtherPlayerUserId));

        await service.EnsureBotSeatControlStatesAsync(
            BotTurnServiceTestHarness.GameId,
            playerStates.Cast<GameSeatState>().ToList(),
            BotTurnServiceTestHarness.ControllerUserId,
            CancellationToken.None);
        await service.EnsureBotSeatControlStatesAsync(
            BotTurnServiceTestHarness.GameId,
            playerStates.Cast<GameSeatState>().ToList(),
            BotTurnServiceTestHarness.ControllerUserId,
            CancellationToken.None);

        var activeBotControl = Assert.Single(playerStates.Where(playerState =>
            string.Equals(playerState.PlayerUserId, BotTurnServiceTestHarness.ActivePlayerUserId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(playerState.BotControlStatus, BotControlStatuses.Active, StringComparison.OrdinalIgnoreCase)
            && playerState.BotControlClearedUtc is null));
        Assert.Equal(SeatControllerModes.AI, activeBotControl.ControllerMode);
        Assert.Equal(string.Empty, activeBotControl.BotDefinitionId);
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
        var playerStates = BotTurnServiceTestHarness.CreateDedicatedBotSeatPlayerStates(
            BotTurnServiceTestHarness.CreateSelections(
                BotTurnServiceTestHarness.ActivePlayerUserId,
                BotTurnServiceTestHarness.OtherPlayerUserId),
            BotTurnServiceTestHarness.ActivePlayerUserId,
            assignedBot.BotDefinitionId);

        var action = await service.CreateBotActionAsync(BotTurnServiceTestHarness.GameId, playerStates.Cast<GameSeatState>().ToList(), engine, GameEngineFixture.CreateTestMap(), CancellationToken.None);

        var regionAction = Assert.IsType<ChooseDestinationRegionAction>(action);
        Assert.NotNull(regionAction.BotMetadata);
        Assert.Equal("Seat Scoped Bot", regionAction.BotMetadata!.BotName);
    }

    [Fact]
    public async Task CreateBotActionAsync_PurchaseWithOnlyDeclineChoice_ReturnsDeclinePurchaseAction()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();
        engine.CurrentTurn.Phase = TurnPhase.Purchase;
        engine.CurrentTurn.PendingRegionChoice = null;
        engine.CurrentTurn.ActivePlayer.Cash = 0;

        var presenceService = new GamePresenceService();
        var botDefinition = BotTurnServiceTestHarness.CreateBotDefinition();
        presenceService.SetMockConnectionState(BotTurnServiceTestHarness.GameId, BotTurnServiceTestHarness.ActivePlayerUserId, isConnected: false);
        var playerProfile = BotTurnServiceTestHarness.CreateUser(
            BotTurnServiceTestHarness.ActivePlayerUserId,
            name: "Alice",
            strategyText: "Decline purchases when there is no legal buy.");
        var service = BotTurnServiceTestHarness.CreateServiceWithUsers(presenceService, [playerProfile], botDefinition);
        var playerStates = BotTurnServiceTestHarness.CreateAssignedPlayerStates(
            BotTurnServiceTestHarness.CreateSelections(
                BotTurnServiceTestHarness.ActivePlayerUserId,
                BotTurnServiceTestHarness.OtherPlayerUserId),
            BotTurnServiceTestHarness.ActivePlayerUserId,
            BotTurnServiceTestHarness.ControllerUserId,
            botDefinition.BotDefinitionId);

        var action = await service.CreateBotActionAsync(BotTurnServiceTestHarness.GameId, playerStates.Cast<GameSeatState>().ToList(), engine, GameEngineFixture.CreateTestMap(), CancellationToken.None);

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
        var playerStates = BotTurnServiceTestHarness.CreateDedicatedBotSeatPlayerStates(
            BotTurnServiceTestHarness.CreateSelections(
                BotTurnServiceTestHarness.ActivePlayerUserId,
                BotTurnServiceTestHarness.OtherPlayerUserId),
            BotTurnServiceTestHarness.ActivePlayerUserId,
            botDefinition.BotDefinitionId);

        var action = await service.CreateBotActionAsync(BotTurnServiceTestHarness.GameId, playerStates.Cast<GameSeatState>().ToList(), engine, GameEngineFixture.CreateTestMap(), CancellationToken.None);

        var declineAction = Assert.IsType<DeclinePurchaseAction>(action);
        Assert.Equal(BotTurnServiceTestHarness.ServerActorUserId, declineAction.ActorUserId);
        Assert.NotNull(declineAction.BotMetadata);
        Assert.Equal("Fallback", declineAction.BotMetadata!.DecisionSource);
        Assert.Equal("OpenAI request failed with status 500.", declineAction.BotMetadata.FallbackReason);
    }

    [Fact]
    public async Task CreateBotActionAsync_PurchaseRequest_IncludesOperatingReserveAnalysis()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();
        engine.CurrentTurn.Phase = TurnPhase.Purchase;
        engine.CurrentTurn.PendingRegionChoice = null;

        var presenceService = new GamePresenceService();
        var botDefinition = BotTurnServiceTestHarness.CreateBotDefinition(
            botDefinitionId: BotTurnServiceTestHarness.ActivePlayerUserId,
            name: "Reserve Bot");
        var (service, handler) = BotTurnServiceTestHarness.CreateServiceWithOpenAiSelection(
            presenceService,
            "decline-purchase",
            botDefinition);
        var playerStates = BotTurnServiceTestHarness.CreateDedicatedBotSeatPlayerStates(
            BotTurnServiceTestHarness.CreateSelections(
                BotTurnServiceTestHarness.ActivePlayerUserId,
                BotTurnServiceTestHarness.OtherPlayerUserId),
            BotTurnServiceTestHarness.ActivePlayerUserId,
            botDefinition.BotDefinitionId);

        var action = await service.CreateBotActionAsync(
            BotTurnServiceTestHarness.GameId,
            playerStates.Cast<GameSeatState>().ToList(),
            engine,
            GameEngineFixture.CreateTestMap(),
            CancellationToken.None);

        Assert.IsType<DeclinePurchaseAction>(action);
        Assert.Equal(1, handler.RequestCount);
        Assert.Contains("RecommendedOperatingReserveCash", handler.LastRequestBody, StringComparison.Ordinal);
        Assert.Contains("TopRiskRegions", handler.LastRequestBody, StringComparison.Ordinal);
        Assert.Contains("PreservesRecommendedOperatingReserve", handler.LastRequestBody, StringComparison.Ordinal);
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
        var botDefinition = BotTurnServiceTestHarness.CreateBotDefinition();
        presenceService.SetMockConnectionState(BotTurnServiceTestHarness.GameId, BotTurnServiceTestHarness.ActivePlayerUserId, isConnected: false);
        var playerProfile = BotTurnServiceTestHarness.CreateUser(
            BotTurnServiceTestHarness.ActivePlayerUserId,
            name: "Alice",
            strategyText: "Follow the saved route.");
        var service = BotTurnServiceTestHarness.CreateServiceWithUsers(presenceService, [playerProfile], botDefinition);
        var playerStates = BotTurnServiceTestHarness.CreateAssignedPlayerStates(
            BotTurnServiceTestHarness.CreateSelections(
                BotTurnServiceTestHarness.ActivePlayerUserId,
                BotTurnServiceTestHarness.OtherPlayerUserId),
            BotTurnServiceTestHarness.ActivePlayerUserId,
            BotTurnServiceTestHarness.ControllerUserId,
            botDefinition.BotDefinitionId);

        var action = await service.CreateBotActionAsync(BotTurnServiceTestHarness.GameId, playerStates.Cast<GameSeatState>().ToList(), engine, GameEngineFixture.CreateTestMap(), CancellationToken.None);

        var moveAction = Assert.IsType<MoveAction>(action);
        Assert.Equal(expectedPoints, moveAction.PointsTaken);
        Assert.NotEmpty(moveAction.SelectedSegmentKeys);
        Assert.NotNull(moveAction.BotMetadata);
        Assert.Equal("SuggestedRoute", moveAction.BotMetadata!.DecisionSource);
    }

    [Fact]
    public async Task CreateBotActionAsync_DisconnectedHumanWithoutBotControl_UsesPlayerStrategyProfile()
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
        var playerStates = BotTurnServiceTestHarness.CreatePlayerStates(
            BotTurnServiceTestHarness.CreateSelections(
                BotTurnServiceTestHarness.ActivePlayerUserId,
                BotTurnServiceTestHarness.OtherPlayerUserId));

        var action = await service.CreateBotActionAsync(BotTurnServiceTestHarness.GameId, playerStates.Cast<GameSeatState>().ToList(), engine, GameEngineFixture.CreateTestMap(), CancellationToken.None);

        var regionAction = Assert.IsType<ChooseDestinationRegionAction>(action);
        Assert.Equal("SE", regionAction.SelectedRegionCode);
        Assert.Equal(BotTurnServiceTestHarness.ServerActorUserId, regionAction.ActorUserId);
        Assert.NotNull(regionAction.BotMetadata);
        Assert.Equal("OnlyLegalChoice", regionAction.BotMetadata!.DecisionSource);
        Assert.Equal("Alice", regionAction.BotMetadata.BotName);
    }

    [Fact]
    public async Task CreateBotActionAsync_BotControlWithoutDelegatedControl_UsesPlayerStrategyProfile()
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
        var playerStates = BotTurnServiceTestHarness.CreateBotControlledPlayerStates(
            BotTurnServiceTestHarness.CreateSelections(
                BotTurnServiceTestHarness.ActivePlayerUserId,
                BotTurnServiceTestHarness.OtherPlayerUserId),
            BotTurnServiceTestHarness.ActivePlayerUserId,
            BotTurnServiceTestHarness.ControllerUserId,
            "legacy-bot-mode-definition");

        var action = await service.CreateBotActionAsync(BotTurnServiceTestHarness.GameId, playerStates.Cast<GameSeatState>().ToList(), engine, GameEngineFixture.CreateTestMap(), CancellationToken.None);

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
            name: "Bot Mode Strategy");
        var service = BotTurnServiceTestHarness.CreateService(presenceService, botDefinition);
        var playerStates = BotTurnServiceTestHarness.CreateBotControlledPlayerStates(
            BotTurnServiceTestHarness.CreateSelections(
                "seller@example.com",
                "bob@example.com",
                "charlie@example.com"),
            "bob@example.com",
            BotTurnServiceTestHarness.ControllerUserId,
            botDefinition.BotDefinitionId);

        var action = await service.CreateBotActionAsync(BotTurnServiceTestHarness.GameId, playerStates.Cast<GameSeatState>().ToList(), engine, GameEngineFixture.CreateTestMap(), CancellationToken.None);

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
            name: "Bot Mode Strategy");
        var service = BotTurnServiceTestHarness.CreateService(presenceService, botDefinition);
        var playerStates = BotTurnServiceTestHarness.CreateBotControlledPlayerStates(
            BotTurnServiceTestHarness.CreateSelections(
                "seller@example.com",
                "bob@example.com",
                "charlie@example.com"),
            "bob@example.com",
            BotTurnServiceTestHarness.ControllerUserId,
            botDefinition.BotDefinitionId);

        var action = await service.CreateBotActionAsync(BotTurnServiceTestHarness.GameId, playerStates.Cast<GameSeatState>().ToList(), engine, GameEngineFixture.CreateTestMap(), CancellationToken.None);

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
            name: "Bot Mode Strategy");
        var (service, handler) = BotTurnServiceTestHarness.CreateServiceWithOpenAiSelection(
            presenceService,
            $"auction-cap:{ceiling}",
            botDefinition);
        var playerStates = BotTurnServiceTestHarness.CreateBotControlledPlayerStates(
            BotTurnServiceTestHarness.CreateSelections(
                "seller@example.com",
                "bob@example.com",
                "charlie@example.com"),
            "bob@example.com",
            BotTurnServiceTestHarness.ControllerUserId,
            botDefinition.BotDefinitionId);

        var firstAction = Assert.IsType<BidAction>(await service.CreateBotActionAsync(BotTurnServiceTestHarness.GameId, playerStates.Cast<GameSeatState>().ToList(), engine, GameEngineFixture.CreateTestMap(), CancellationToken.None));

        Assert.Equal(startingPrice, firstAction.AmountBid);
        Assert.Equal(1, handler.RequestCount);

        var cachedBotControl = Assert.Single(playerStates.Where(playerState =>
            string.Equals(playerState.PlayerUserId, "bob@example.com", StringComparison.OrdinalIgnoreCase)
            && string.Equals(playerState.BotControlStatus, BotControlStatuses.Active, StringComparison.OrdinalIgnoreCase)
            && playerState.BotControlClearedUtc is null));
        Assert.Equal(engine.ToSnapshot().TurnNumber, cachedBotControl.AuctionPlanTurnNumber);
        Assert.Equal(railroad.Index, cachedBotControl.AuctionPlanRailroadIndex);
        Assert.Equal(startingPrice, cachedBotControl.AuctionPlanStartingPrice);
        Assert.Equal(ceiling, cachedBotControl.AuctionPlanMaximumBid);

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

        var secondAction = Assert.IsType<BidAction>(await service.CreateBotActionAsync(BotTurnServiceTestHarness.GameId, playerStates.Cast<GameSeatState>().ToList(), engine, GameEngineFixture.CreateTestMap(), CancellationToken.None));

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
            name: "Bot Mode Strategy");
        var (service, handler) = BotTurnServiceTestHarness.CreateServiceWithOpenAiSelection(
            presenceService,
            $"auction-cap:{startingPrice}",
            botDefinition);
        var playerStates = BotTurnServiceTestHarness.CreateBotControlledPlayerStates(
            BotTurnServiceTestHarness.CreateSelections(
                "seller@example.com",
                "bob@example.com",
                "charlie@example.com"),
            "bob@example.com",
            BotTurnServiceTestHarness.ControllerUserId,
            botDefinition.BotDefinitionId);

        var firstAction = Assert.IsType<BidAction>(await service.CreateBotActionAsync(BotTurnServiceTestHarness.GameId, playerStates.Cast<GameSeatState>().ToList(), engine, GameEngineFixture.CreateTestMap(), CancellationToken.None));
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

        var secondAction = Assert.IsType<AuctionDropOutAction>(await service.CreateBotActionAsync(BotTurnServiceTestHarness.GameId, playerStates.Cast<GameSeatState>().ToList(), engine, GameEngineFixture.CreateTestMap(), CancellationToken.None));

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
        var playerStates = BotTurnServiceTestHarness.CreateDedicatedBotSeatPlayerStates(
            BotTurnServiceTestHarness.CreateSelections(
                "seller@example.com",
                "bob@example.com",
                "charlie@example.com"),
            "bob@example.com",
            bidderOneDefinition.BotDefinitionId);
        var bidderTwoState = playerStates.Single(playerState => string.Equals(playerState.PlayerUserId, "charlie@example.com", StringComparison.OrdinalIgnoreCase));
        bidderTwoState.ControllerMode = SeatControllerModes.AI;
        bidderTwoState.ControllerUserId = string.Empty;
        bidderTwoState.BotDefinitionId = bidderTwoDefinition.BotDefinitionId;
        bidderTwoState.BotControlActivatedUtc = DateTimeOffset.UtcNow;
        bidderTwoState.BotControlStatus = BotControlStatuses.Active;

        var bidderOneState = playerStates.Single(playerState => string.Equals(playerState.PlayerUserId, "bob@example.com", StringComparison.OrdinalIgnoreCase));
        bidderOneState.AuctionPlanTurnNumber = turnNumber;
        bidderOneState.AuctionPlanRailroadIndex = railroad.Index;
        bidderOneState.AuctionPlanStartingPrice = startingPrice;
        bidderOneState.AuctionPlanMaximumBid = startingPrice;
        bidderTwoState.AuctionPlanTurnNumber = turnNumber;
        bidderTwoState.AuctionPlanRailroadIndex = railroad.Index;
        bidderTwoState.AuctionPlanStartingPrice = startingPrice;
        bidderTwoState.AuctionPlanMaximumBid = startingPrice + global::Boxcars.Engine.Domain.GameEngine.AuctionBidIncrement;

        var summaryAction = await service.TryResolveAllAiAuctionAsync(BotTurnServiceTestHarness.GameId, playerStates.Cast<GameSeatState>().ToList(), engine, GameEngineFixture.CreateTestMap(), CancellationToken.None);

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
    public async Task TryResolveAllAiAuctionAsync_WithDisconnectedHumanBidder_DoesNotAutoResolveAuction()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine(GameEngineFixture.ThreePlayerNames, 3);
        engine.CurrentTurn.Phase = TurnPhase.Purchase;

        var seller = engine.CurrentTurn.ActivePlayer;
        var bidderOne = engine.Players[1];
        var bidderTwo = engine.Players[2];
        var railroad = engine.Railroads[0];
        railroad.Owner = seller;
        seller.OwnedRailroads.Add(railroad);
        bidderOne.Cash = 16_000;
        bidderTwo.Cash = 16_000;

        engine.AuctionRailroad(railroad);
        var startingPrice = engine.CurrentTurn.AuctionState!.StartingPrice;
        var turnNumber = engine.ToSnapshot().TurnNumber;
        Assert.Equal(bidderOne.Index, engine.CurrentTurn.AuctionState.CurrentBidderPlayerIndex);
        Assert.Equal(2, engine.CurrentTurn.AuctionState.Participants.Count(participant => participant.IsEligible && !participant.HasDroppedOut));

        var presenceService = new GamePresenceService();
        var humanUser = BotTurnServiceTestHarness.CreateUser(
            "bob@example.com",
            name: "Bob Human",
            strategyText: "Prefer strong networks but stay cash-positive.",
            isBot: false);
        var bidderTwoDefinition = BotTurnServiceTestHarness.CreateBotDefinition("charlie@example.com", "Charlie Bot");
        var service = BotTurnServiceTestHarness.CreateServiceWithUsers(presenceService, [humanUser], bidderTwoDefinition);
        var playerStates = BotTurnServiceTestHarness.CreateDedicatedBotSeatPlayerStates(
            BotTurnServiceTestHarness.CreateSelections(
                "seller@example.com",
                "bob@example.com",
                "charlie@example.com"),
            "charlie@example.com",
            bidderTwoDefinition.BotDefinitionId);
        var bidderOneState = playerStates.Single(playerState => string.Equals(playerState.PlayerUserId, "bob@example.com", StringComparison.OrdinalIgnoreCase));
        Assert.False(PlayerControlRules.HasActiveBotControl(bidderOneState));
        var bidderTwoState = playerStates.Single(playerState => string.Equals(playerState.PlayerUserId, "charlie@example.com", StringComparison.OrdinalIgnoreCase));
        bidderTwoState.AuctionPlanTurnNumber = turnNumber;
        bidderTwoState.AuctionPlanRailroadIndex = railroad.Index;
        bidderTwoState.AuctionPlanStartingPrice = startingPrice;
        bidderTwoState.AuctionPlanMaximumBid = startingPrice + global::Boxcars.Engine.Domain.GameEngine.AuctionBidIncrement;

        var summaryAction = await service.TryResolveAllAiAuctionAsync(BotTurnServiceTestHarness.GameId, playerStates.Cast<GameSeatState>().ToList(), engine, GameEngineFixture.CreateTestMap(), CancellationToken.None);

        Assert.Null(summaryAction);
        Assert.NotNull(engine.CurrentTurn.AuctionState);
        Assert.Equal(bidderOne.Index, engine.CurrentTurn.AuctionState!.CurrentBidderPlayerIndex);
        Assert.Equal(railroad.Owner, seller);
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
        var playerStates = BotTurnServiceTestHarness.CreateDedicatedBotSeatPlayerStates(
            BotTurnServiceTestHarness.CreateSelections(
                "seller@example.com",
                "bob@example.com",
                "charlie@example.com"),
            "bob@example.com",
            bidderOneDefinition.BotDefinitionId);
        var bidderTwoState = playerStates.Single(playerState => string.Equals(playerState.PlayerUserId, "charlie@example.com", StringComparison.OrdinalIgnoreCase));
        bidderTwoState.ControllerMode = SeatControllerModes.AI;
        bidderTwoState.ControllerUserId = string.Empty;
        bidderTwoState.BotDefinitionId = bidderTwoDefinition.BotDefinitionId;
        bidderTwoState.BotControlActivatedUtc = DateTimeOffset.UtcNow;
        bidderTwoState.BotControlStatus = BotControlStatuses.Active;

        foreach (var botState in playerStates.Where(playerState =>
                     string.Equals(playerState.PlayerUserId, "bob@example.com", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(playerState.PlayerUserId, "charlie@example.com", StringComparison.OrdinalIgnoreCase)))
        {
            botState.AuctionPlanTurnNumber = turnNumber;
            botState.AuctionPlanRailroadIndex = railroad.Index;
            botState.AuctionPlanStartingPrice = startingPrice;
            botState.AuctionPlanMaximumBid = 0;
        }

        var summaryAction = await service.TryResolveAllAiAuctionAsync(BotTurnServiceTestHarness.GameId, playerStates.Cast<GameSeatState>().ToList(), engine, GameEngineFixture.CreateTestMap(), CancellationToken.None);

        var dropOutAction = Assert.IsType<AuctionDropOutAction>(summaryAction);
        Assert.Equal("All AI bidders", dropOutAction.PlayerId);
        Assert.Null(engine.CurrentTurn.AuctionState);
        Assert.Null(railroad.Owner);
        Assert.DoesNotContain(railroad, seller.OwnedRailroads);
        Assert.Equal("AllAiAuctionResolution", dropOutAction.BotMetadata!.DecisionSource);
    }
}
