using Boxcars.Data;
using Boxcars.Engine.Domain;
using Boxcars.Engine.Tests.Fixtures;
using Boxcars.GameEngine;
using Boxcars.Services;
using RailBaronGameEngine = Boxcars.Engine.Domain.GameEngine;

namespace Boxcars.Engine.Tests.Unit;

public class BotRouteReviewTests
{
    [Fact]
    public async Task CreateBotActionAsync_DebugModeMovePayingAnOpponent_WaitsForTheAdmin()
    {
        var (engine, service, playerStates) = CreateBotOnMove();
        var opponent = SellFirstRailroadOfRouteToOpponent(engine);

        var action = await service.CreateBotActionAsync(BotTurnServiceTestHarness.GameId, playerStates, engine, GameEngineFixture.CreateTestMap(), CancellationToken.None, reviewMovesThatPayOpponents: true);

        Assert.Null(action);
        var review = service.RouteReviews.GetPending(BotTurnServiceTestHarness.GameId);
        Assert.NotNull(review);
        Assert.Equal(engine.CurrentTurn.ActivePlayer.Index, review!.PlayerIndex);
        Assert.Equal(engine.CurrentTurn.TurnNumber, review.TurnNumber);
        var fee = Assert.Single(review.Fees);
        Assert.Equal(opponent.Name, fee.OwnerName);
        Assert.Equal(RailBaronGameEngine.GetUnfriendlyFee(engine.Settings, engine.AllRailroadsSold), fee.Amount);
        Assert.Equal(engine.CurrentTurn.ActivePlayer.ActiveRoute!.NodeIds, review.RouteNodeIds);
    }

    [Fact]
    public async Task CreateBotActionAsync_ApprovedMove_IsMadeOnceAndRecordedAsApproved()
    {
        var (engine, service, playerStates) = CreateBotOnMove();
        SellFirstRailroadOfRouteToOpponent(engine);
        var map = GameEngineFixture.CreateTestMap();
        await service.CreateBotActionAsync(BotTurnServiceTestHarness.GameId, playerStates, engine, map, CancellationToken.None, reviewMovesThatPayOpponents: true);

        Assert.True(service.RouteReviews.Approve(BotTurnServiceTestHarness.GameId));
        var approvedAction = await service.CreateBotActionAsync(BotTurnServiceTestHarness.GameId, playerStates, engine, map, CancellationToken.None, reviewMovesThatPayOpponents: true);
        var askedAgain = await service.CreateBotActionAsync(BotTurnServiceTestHarness.GameId, playerStates, engine, map, CancellationToken.None, reviewMovesThatPayOpponents: true);

        var moveAction = Assert.IsType<MoveAction>(approvedAction);
        Assert.Equal(BotRouteReviewService.ApprovedDecisionSource, moveAction.BotMetadata!.DecisionSource);
        // The approval covered that one move.
        Assert.Null(askedAgain);
    }

    [Fact]
    public async Task CreateBotActionAsync_DebugModeMovePayingNoOpponent_MovesWithoutWaiting()
    {
        var (engine, service, playerStates) = CreateBotOnMove();

        var action = await service.CreateBotActionAsync(BotTurnServiceTestHarness.GameId, playerStates, engine, GameEngineFixture.CreateTestMap(), CancellationToken.None, reviewMovesThatPayOpponents: true);

        var moveAction = Assert.IsType<MoveAction>(action);
        Assert.Equal("SuggestedRoute", moveAction.BotMetadata!.DecisionSource);
        Assert.Null(service.RouteReviews.GetPending(BotTurnServiceTestHarness.GameId));
    }

    [Fact]
    public async Task CreateBotActionAsync_DebugModeOff_MovesOverOpponentsRailroadsWithoutWaiting()
    {
        var (engine, service, playerStates) = CreateBotOnMove();
        SellFirstRailroadOfRouteToOpponent(engine);

        var action = await service.CreateBotActionAsync(BotTurnServiceTestHarness.GameId, playerStates, engine, GameEngineFixture.CreateTestMap(), CancellationToken.None);

        Assert.IsType<MoveAction>(action);
        Assert.Null(service.RouteReviews.GetPending(BotTurnServiceTestHarness.GameId));
    }

    [Fact]
    public void TryConsumeApproval_ForAnotherTurnOrPlayer_IsNotApproved()
    {
        var reviews = new BotRouteReviewService();
        reviews.SetPending(CreateReview(turnNumber: 4, playerIndex: 1));
        Assert.True(reviews.Approve("game-1"));

        Assert.False(reviews.TryConsumeApproval("game-1", turnNumber: 5, playerIndex: 1));
        // A mismatched check still uses the approval up, so it can't carry over to a later move.
        Assert.False(reviews.TryConsumeApproval("game-1", turnNumber: 4, playerIndex: 1));
    }

    [Fact]
    public void Approve_WithNothingWaiting_ReturnsFalse()
    {
        var reviews = new BotRouteReviewService();

        Assert.False(reviews.Approve("game-1"));
    }

    private static (RailBaronGameEngine Engine, BotTurnService Service, List<GameSeatState> PlayerStates) CreateBotOnMove()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Move);
        Assert.NotNull(engine.CurrentTurn.ActivePlayer.ActiveRoute);

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

        return (engine, service, playerStates.Cast<GameSeatState>().ToList());
    }

    private static Player SellFirstRailroadOfRouteToOpponent(RailBaronGameEngine engine)
    {
        var bot = engine.CurrentTurn.ActivePlayer;
        var opponent = engine.Players.First(player => player != bot);
        var railroadIndex = bot.ActiveRoute!.Segments[bot.RouteProgressIndex].RailroadIndex;
        var railroad = engine.Railroads.Single(candidate => candidate.Index == railroadIndex);
        railroad.Owner = opponent;
        opponent.OwnedRailroads.Add(railroad);
        return opponent;
    }

    private static BotRouteReview CreateReview(int turnNumber, int playerIndex)
    {
        return new BotRouteReview
        {
            GameId = "game-1",
            TurnNumber = turnNumber,
            PlayerIndex = playerIndex,
            PlayerName = "Bot",
            RouteNodeIds = [],
            RouteSegmentKeys = [],
            MoveSteps = 1,
            Fees = []
        };
    }
}
