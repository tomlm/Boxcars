using System.Reflection;
using Azure.Data.Tables;
using Boxcars.Data;
using Boxcars.Engine.Domain;
using Boxcars.Engine.Tests.Fixtures;
using Boxcars.GameEngine;
using Boxcars.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Boxcars.Engine.Tests.Unit;

public class ForcedSaleActionTests
{
    [Fact]
    public void DescribeAction_AuctionBankFallback_ReportsSaleToBankAmount()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine(playerNames: ["Seller", "Bidder"], playerCount: 2);
        var seller = engine.CurrentTurn.ActivePlayer;
        engine.CurrentTurn.Phase = TurnPhase.UseFees;
        engine.CurrentTurn.PendingFeeAmount = 6_000;
        engine.CurrentTurn.ForcedSaleState = new ForcedSaleState
        {
            AmountOwed = 6_000,
            CashBeforeFees = seller.Cash,
            CashAfterLastSale = seller.Cash,
            SalesCompletedCount = 0,
            CanPayNow = false,
            EliminationTriggered = false
        };

        var railroad = engine.Railroads[0];
        railroad.Owner = seller;
        seller.OwnedRailroads.Add(railroad);

        engine.AuctionRailroad(railroad);
        var bidder = engine.Players[1];
        var snapshotBeforeAction = engine.ToSnapshot();
        var action = new AuctionDropOutAction
        {
            PlayerId = bidder.Name,
            PlayerIndex = bidder.Index,
            ActorUserId = "bidder@example.com",
            RailroadIndex = railroad.Index
        };

        engine.DropOutOfAuction(railroad, bidder);
        var snapshotAfterAction = engine.ToSnapshot();

        var summary = InvokeDescribeAction(action, snapshotBeforeAction, snapshotAfterAction, engine);
        var expectedRailroadName = string.IsNullOrWhiteSpace(railroad.ShortName) ? railroad.Name : railroad.ShortName;

        Assert.Equal($"{expectedRailroadName} was sold to the bank for ${railroad.PurchasePrice / 2:N0}", summary);
    }

    [Fact]
    public void ValidateActionAuthorization_AllowsCurrentAuctionParticipantBid_WhenActorControlsThatSlot()
    {
        var (gameEntity, engine) = CreateAuctionAuthorizationContext();
        var currentBidderIndex = engine.CurrentTurn.AuctionState!.CurrentBidderPlayerIndex!.Value;
        var currentBidder = engine.Players[currentBidderIndex];

        var action = new BidAction
        {
            PlayerId = currentBidder.Name,
            PlayerIndex = currentBidder.Index,
            ActorUserId = "bidder@example.com",
            RailroadIndex = engine.CurrentTurn.AuctionState.RailroadIndex,
            AmountBid = engine.CurrentTurn.AuctionState.StartingPrice
        };

        InvokeValidateActionAuthorization(gameEntity, engine, action);
    }

    [Fact]
    public void ValidateActionAuthorization_RejectsAuctionPass_WhenActorDoesNotControlBidderSlot()
    {
        var (gameEntity, engine) = CreateAuctionAuthorizationContext();
        var currentBidderIndex = engine.CurrentTurn.AuctionState!.CurrentBidderPlayerIndex!.Value;
        var currentBidder = engine.Players[currentBidderIndex];

        var action = new AuctionPassAction
        {
            PlayerId = currentBidder.Name,
            PlayerIndex = currentBidder.Index,
            ActorUserId = "intruder@example.com",
            RailroadIndex = engine.CurrentTurn.AuctionState.RailroadIndex
        };

        var exception = Assert.Throws<TargetInvocationException>(() => InvokeValidateActionAuthorization(gameEntity, engine, action));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("Only the controlling participant for the acting player may perform this auction action.", exception.InnerException!.Message);
    }

    [Fact]
    public void ValidateActionAuthorization_RejectsAuctionDropOut_ForUncontrolledBeatlesSlot()
    {
        var (gameEntity, engine) = CreateAuctionAuthorizationContext(slotTwoUserId: "george@beatles.com");
        var currentBidderIndex = engine.CurrentTurn.AuctionState!.CurrentBidderPlayerIndex!.Value;
        var currentBidder = engine.Players[currentBidderIndex];

        var action = new AuctionDropOutAction
        {
            PlayerId = currentBidder.Name,
            PlayerIndex = currentBidder.Index,
            ActorUserId = "someone-else@example.com",
            RailroadIndex = engine.CurrentTurn.AuctionState.RailroadIndex
        };

        var exception = Assert.Throws<TargetInvocationException>(() => InvokeValidateActionAuthorization(gameEntity, engine, action));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("Only the controlling participant for the acting player may perform this auction action.", exception.InnerException!.Message);
    }

    [Fact]
    public void ValidateActionAuthorization_RejectsAuctionBid_WhenParticipantHasBeenEliminated()
    {
        var (gameEntity, engine) = CreateAuctionAuthorizationContext();
        var currentBidderIndex = engine.CurrentTurn.AuctionState!.CurrentBidderPlayerIndex!.Value;
        var currentBidder = engine.Players[currentBidderIndex];
        currentBidder.IsActive = false;
        currentBidder.IsBankrupt = true;

        var action = new BidAction
        {
            PlayerId = currentBidder.Name,
            PlayerIndex = currentBidder.Index,
            ActorUserId = "bidder@example.com",
            RailroadIndex = engine.CurrentTurn.AuctionState.RailroadIndex,
            AmountBid = engine.CurrentTurn.AuctionState.StartingPrice
        };

        var exception = Assert.Throws<TargetInvocationException>(() => InvokeValidateActionAuthorization(gameEntity, engine, action));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("Eliminated players may only spectate", exception.InnerException!.Message);
    }

    [Fact]
    public void DescribeAction_ChooseDestinationRegion_ReportsSelectedRegionAndDestination()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();

        random.QueueWeightedDraw(0);
        random.QueueWeightedDraw(1);
        engine.DrawDestination();

        var snapshotBeforeAction = engine.ToSnapshot();
        var action = new ChooseDestinationRegionAction
        {
            PlayerId = engine.CurrentTurn.ActivePlayer.Name,
            PlayerIndex = engine.CurrentTurn.ActivePlayer.Index,
            ActorUserId = "alice@example.com",
            SelectedRegionCode = "SE"
        };

        random.QueueWeightedDraw(1);
        engine.ChooseDestinationRegion("SE");
        var snapshotAfterAction = engine.ToSnapshot();

        var summary = InvokeDescribeAction(action, snapshotBeforeAction, snapshotAfterAction, engine);

        Assert.Equal("Alice chose SE as the replacement destination region and received Atlanta.", summary);
    }

    [Fact]
    public void DescribeAction_ChooseDestinationRegion_SameCityRedraw_ReportsLostTurn()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();

        random.QueueWeightedDraw(0);
        random.QueueWeightedDraw(1);
        engine.DrawDestination();

        var snapshotBeforeAction = engine.ToSnapshot();
        var action = new ChooseDestinationRegionAction
        {
            PlayerId = engine.CurrentTurn.ActivePlayer.Name,
            PlayerIndex = engine.CurrentTurn.ActivePlayer.Index,
            ActorUserId = "alice@example.com",
            SelectedRegionCode = "NE"
        };

        random.QueueWeightedDraw(0);
        engine.ChooseDestinationRegion("NE");
        var snapshotAfterAction = engine.ToSnapshot();

        var summary = InvokeDescribeAction(action, snapshotBeforeAction, snapshotAfterAction, engine);

        Assert.Equal("Alice chose NE as the replacement destination region and lost the turn after redrawing the current city.", summary);
    }

    [Fact]
    public void ValidateActionAuthorization_RejectsRegionChoice_WhenActorDoesNotControlActivePlayerSlot()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();

        random.QueueWeightedDraw(0);
        random.QueueWeightedDraw(1);
        engine.DrawDestination();

        var gameEntity = new GameEntity
        {
            PartitionKey = "game-1",
            GameId = "game-1",
            PlayersJson = GamePlayerSelectionSerialization.Serialize(
            [
                new GamePlayerSelection { UserId = "alice@example.com", DisplayName = engine.Players[0].Name, Color = "#111111" },
                new GamePlayerSelection { UserId = "bob@example.com", DisplayName = engine.Players[1].Name, Color = "#222222" }
            ])
        };

        var action = new ChooseDestinationRegionAction
        {
            PlayerId = engine.CurrentTurn.ActivePlayer.Name,
            PlayerIndex = engine.CurrentTurn.ActivePlayer.Index,
            ActorUserId = "intruder@example.com",
            SelectedRegionCode = "SE"
        };

        var exception = Assert.Throws<TargetInvocationException>(() => InvokeValidateActionAuthorization(gameEntity, engine, action));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("Only the controlling participant for the active player may perform this action.", exception.InnerException!.Message);
    }

    [Fact]
    public void ValidateActionAuthorization_AllowsServerActor_ForAiControlledSeat()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();
        var gameEntity = new GameEntity
        {
            PartitionKey = "game-1",
            GameId = "game-1",
            PlayersJson = GamePlayerSelectionSerialization.Serialize(
            [
                new GamePlayerSelection { UserId = "alice@example.com", DisplayName = engine.Players[0].Name, Color = "#111111" },
                new GamePlayerSelection { UserId = "bob@example.com", DisplayName = engine.Players[1].Name, Color = "#222222" }
            ]),
            BotAssignmentsJson = BotAssignmentSerialization.Serialize(
            [
                new BotAssignment
                {
                    GameId = "game-1",
                    PlayerUserId = "alice@example.com",
                    ControllerMode = SeatControllerModes.AiBotSeat,
                    BotDefinitionId = "bot-1",
                    Status = BotAssignmentStatuses.Active
                }
            ])
        };

        var action = new EndTurnAction
        {
            PlayerId = engine.CurrentTurn.ActivePlayer.Name,
            PlayerIndex = engine.CurrentTurn.ActivePlayer.Index,
            ActorUserId = BotOptions.DefaultServerActorUserId
        };

        InvokeValidateActionAuthorization(gameEntity, engine, action);
    }

    [Fact]
    public void ValidateActionAuthorization_RejectsServerActor_ForHumanControlledSeat()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();
        var gameEntity = new GameEntity
        {
            PartitionKey = "game-1",
            GameId = "game-1",
            PlayersJson = GamePlayerSelectionSerialization.Serialize(
            [
                new GamePlayerSelection { UserId = "alice@example.com", DisplayName = engine.Players[0].Name, Color = "#111111" },
                new GamePlayerSelection { UserId = "bob@example.com", DisplayName = engine.Players[1].Name, Color = "#222222" }
            ])
        };

        var action = new EndTurnAction
        {
            PlayerId = engine.CurrentTurn.ActivePlayer.Name,
            PlayerIndex = engine.CurrentTurn.ActivePlayer.Index,
            ActorUserId = BotOptions.DefaultServerActorUserId
        };

        var exception = Assert.Throws<TargetInvocationException>(() => InvokeValidateActionAuthorization(gameEntity, engine, action));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("Only the controlling participant for the active player may perform this action.", exception.InnerException!.Message);
    }

    private static void InvokeValidateActionAuthorization(GameEntity gameEntity, Boxcars.Engine.Domain.GameEngine engine, PlayerAction action)
    {
        var service = CreateGameEngineServiceForTests();
        var method = typeof(GameEngineService).GetMethod("ValidateActionAuthorization", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("ValidateActionAuthorization was not found.");

        method.Invoke(service, [gameEntity, engine, action]);
    }

    private static string InvokeDescribeAction(PlayerAction action, Boxcars.Engine.Persistence.GameState snapshotBeforeAction, Boxcars.Engine.Persistence.GameState snapshotAfterAction, Boxcars.Engine.Domain.GameEngine engine)
    {
        var method = typeof(GameEngineService).GetMethod("DescribeAction", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("DescribeAction was not found.");

        return (string)(method.Invoke(CreateGameEngineServiceForTests(), [new GameEntity { PartitionKey = "game-1", RowKey = "GAME", GameId = "game-1" }, action, snapshotBeforeAction, snapshotAfterAction, engine])
            ?? throw new InvalidOperationException("DescribeAction returned null."));
    }

    private static (GameEntity GameEntity, Boxcars.Engine.Domain.GameEngine Engine) CreateAuctionAuthorizationContext(string slotTwoUserId = "bidder@example.com")
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine(GameEngineFixture.ThreePlayerNames, 3);
        engine.CurrentTurn.Phase = TurnPhase.Purchase;

        var seller = engine.CurrentTurn.ActivePlayer;
        var railroad = engine.Railroads[0];
        railroad.Owner = seller;
        seller.OwnedRailroads.Add(railroad);
        engine.AuctionRailroad(railroad);

        var gameEntity = new GameEntity
        {
            PartitionKey = "game-1",
            GameId = "game-1",
            PlayersJson = GamePlayerSelectionSerialization.Serialize(
            [
                new GamePlayerSelection { UserId = "seller@example.com", DisplayName = engine.Players[0].Name, Color = "#111111" },
                new GamePlayerSelection { UserId = slotTwoUserId, DisplayName = engine.Players[1].Name, Color = "#222222" },
                new GamePlayerSelection { UserId = "other@example.com", DisplayName = engine.Players[2].Name, Color = "#333333" }
            ])
        };

        return (gameEntity, engine);
    }

    private static GameEngineService CreateGameEngineServiceForTests()
    {
        return new GameEngineService(
            new TestWebHostEnvironment(),
            new TableServiceClient(new Uri("https://example.com"), new TableSharedKeyCredential("devstoreaccount1", Convert.ToBase64String(new byte[32]))),
            new GamePresenceService(),
            Options.Create(new BotOptions()),
            Options.Create(new PurchaseRulesOptions()),
            NullLogger<GameEngineService>.Instance);
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Boxcars.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}