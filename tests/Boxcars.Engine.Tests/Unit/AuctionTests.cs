using Boxcars.Engine.Domain;
using Boxcars.Engine.Events;
using Boxcars.Engine.Tests.Fixtures;
using Boxcars.Engine.Tests.TestDoubles;

namespace Boxcars.Engine.Tests.Unit;

/// <summary>
/// Tests for auction events and resolution (T038).
/// </summary>
public class AuctionTests
{
    [Fact]
    public void AuctionRailroad_Valid_RaisesAuctionStartedEvent()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Purchase);

        AuctionStartedEventArgs? eventArgs = null;
        engine.AuctionStarted += (s, e) => eventArgs = e;

        if (engine.CurrentTurn.Phase == TurnPhase.Purchase)
        {
            // Give the player a railroad to auction
            var rr = engine.Railroads[0];
            var player = engine.CurrentTurn.ActivePlayer;
            rr.Owner = player;
            player.OwnedRailroads.Add(rr);

            engine.AuctionRailroad(rr);

            Assert.NotNull(eventArgs);
            Assert.Equal(rr, eventArgs!.Railroad);
            Assert.NotEmpty(eventArgs.EligibleBidders);
            Assert.DoesNotContain(player, eventArgs.EligibleBidders);
        }
    }

    [Fact]
    public void AuctionRailroad_NotOwner_ThrowsInvalidOperation()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Purchase);

        if (engine.CurrentTurn.Phase == TurnPhase.Purchase)
        {
            var rr = engine.Railroads[0]; // Not owned by active player

            var ex = Assert.Throws<InvalidOperationException>(() => engine.AuctionRailroad(rr));
            Assert.Contains("Player does not own this railroad", ex.Message);
        }
    }

    [Fact]
    public void AuctionRailroad_WrongPhase_ThrowsInvalidOperation()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            engine.AuctionRailroad(engine.Railroads[0]));
        Assert.Contains("Cannot auction in current phase", ex.Message);
    }

    [Fact]
    public void ResolveAuction_WithWinner_TransfersOwnership()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Purchase);

        if (engine.CurrentTurn.Phase == TurnPhase.Purchase)
        {
            var seller = engine.CurrentTurn.ActivePlayer;
            var buyer = engine.Players.First(p => p != seller);

            var rr = engine.Railroads[0];
            rr.Owner = seller;
            seller.OwnedRailroads.Add(rr);

            int buyerCashBefore = buyer.Cash;
            int sellerCashBefore = seller.Cash;

            engine.ResolveAuction(rr, buyer, 5000);

            Assert.Equal(buyer, rr.Owner);
            Assert.Contains(rr, buyer.OwnedRailroads);
            Assert.DoesNotContain(rr, seller.OwnedRailroads);
            Assert.Equal(buyerCashBefore - 5000, buyer.Cash);
            Assert.Equal(sellerCashBefore + 5000, seller.Cash);
        }
    }

    [Fact]
    public void ResolveAuction_NoWinner_ReturnsToBank()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Purchase);

        if (engine.CurrentTurn.Phase == TurnPhase.Purchase)
        {
            var seller = engine.CurrentTurn.ActivePlayer;
            var rr = engine.Railroads[0];
            rr.Owner = seller;
            seller.OwnedRailroads.Add(rr);

            engine.ResolveAuction(rr, null, 0);

            Assert.Null(rr.Owner);
            Assert.DoesNotContain(rr, seller.OwnedRailroads);
        }
    }

    [Fact]
    public void ResolveAuction_RaisesAuctionCompletedEvent()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Purchase);

        AuctionCompletedEventArgs? eventArgs = null;
        engine.AuctionCompleted += (s, e) => eventArgs = e;

        if (engine.CurrentTurn.Phase == TurnPhase.Purchase)
        {
            var seller = engine.CurrentTurn.ActivePlayer;
            var rr = engine.Railroads[0];
            rr.Owner = seller;
            seller.OwnedRailroads.Add(rr);

            engine.ResolveAuction(rr, null, 0);

            Assert.NotNull(eventArgs);
            Assert.Equal(rr, eventArgs!.Railroad);
        }
    }

    [Fact]
    public void AuctionRailroad_Valid_PopulatesAuctionState()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Purchase);

        var seller = engine.CurrentTurn.ActivePlayer;
        var rr = engine.Railroads[0];
        rr.Owner = seller;
        seller.OwnedRailroads.Add(rr);

        engine.AuctionRailroad(rr);

        Assert.NotNull(engine.CurrentTurn.AuctionState);
        Assert.Equal(rr.Index, engine.CurrentTurn.AuctionState!.RailroadIndex);
        Assert.Equal(rr.PurchasePrice / 2, engine.CurrentTurn.AuctionState.StartingPrice);
        Assert.Equal(engine.Players[1].Index, engine.CurrentTurn.AuctionState.CurrentBidderPlayerIndex);
    }

    [Fact]
    public void PassAuctionTurn_RotatesToNextParticipant()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine(GameEngineFixture.ThreePlayerNames, 3);
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Purchase);

        var seller = engine.CurrentTurn.ActivePlayer;
        var rr = engine.Railroads[0];
        rr.Owner = seller;
        seller.OwnedRailroads.Add(rr);

        engine.AuctionRailroad(rr);

        var firstBidder = engine.Players[1];
        engine.PassAuctionTurn(rr, firstBidder);

        Assert.NotNull(engine.CurrentTurn.AuctionState);
        Assert.Equal(engine.Players[2].Index, engine.CurrentTurn.AuctionState!.CurrentBidderPlayerIndex);
        Assert.Contains(engine.CurrentTurn.AuctionState.Participants, participant => participant.PlayerIndex == firstBidder.Index && participant.HasPassedThisRound);
    }

    [Fact]
    public void DropOutOfAuction_WithNoBids_ReturnsRailroadToBank()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine(playerNames: ["Seller", "Bidder"], playerCount: 2);
        var seller = engine.CurrentTurn.ActivePlayer;
        engine.CurrentTurn.Phase = TurnPhase.Purchase;

        var rr = engine.Railroads[0];
        rr.Owner = seller;
        seller.OwnedRailroads.Add(rr);

        engine.AuctionRailroad(rr);
        engine.DropOutOfAuction(rr, engine.Players[1]);

        Assert.Null(engine.CurrentTurn.AuctionState);
        Assert.Null(rr.Owner);
        Assert.DoesNotContain(rr, seller.OwnedRailroads);
    }

    [Fact]
    public void SubmitAuctionBid_ThenPass_AwardsToLastBidder()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine(GameEngineFixture.ThreePlayerNames, 3);
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Purchase);

        var seller = engine.CurrentTurn.ActivePlayer;
        var bidderOne = engine.Players[1];
        var bidderTwo = engine.Players[2];
        var rr = engine.Railroads[0];
        rr.Owner = seller;
        seller.OwnedRailroads.Add(rr);
        bidderOne.Cash = 20_000;
        bidderTwo.Cash = 20_000;

        engine.AuctionRailroad(rr);
        engine.SubmitAuctionBid(rr, bidderOne, rr.PurchasePrice / 2);
        engine.PassAuctionTurn(rr, bidderTwo);

        Assert.Null(engine.CurrentTurn.AuctionState);
        Assert.Equal(bidderOne, rr.Owner);
        Assert.Contains(rr, bidderOne.OwnedRailroads);
        Assert.DoesNotContain(rr, seller.OwnedRailroads);
    }

    [Fact]
    public void SubmitAuctionBid_NextBidMustIncreaseByTwoHundredFifty()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine(GameEngineFixture.ThreePlayerNames, 3);
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Purchase);

        var seller = engine.CurrentTurn.ActivePlayer;
        var bidderOne = engine.Players[1];
        var bidderTwo = engine.Players[2];
        var rr = engine.Railroads[0];
        rr.Owner = seller;
        seller.OwnedRailroads.Add(rr);
        bidderOne.Cash = 20_000;
        bidderTwo.Cash = 20_000;

        engine.AuctionRailroad(rr);
        engine.SubmitAuctionBid(rr, bidderOne, rr.PurchasePrice / 2);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            engine.SubmitAuctionBid(rr, bidderTwo, engine.CurrentTurn.AuctionState!.CurrentBid + 1));

        Assert.Contains((engine.CurrentTurn.AuctionState!.CurrentBid + global::Boxcars.Engine.Domain.GameEngine.AuctionBidIncrement).ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SubmitAuctionBid_WhenNextRequiredBidExceedsCash_AwardsToCurrentLeader()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine(GameEngineFixture.ThreePlayerNames, 3);
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Purchase);

        var seller = engine.CurrentTurn.ActivePlayer;
        var bidderOne = engine.Players[1];
        var bidderTwo = engine.Players[2];
        var rr = engine.Railroads[0];
        rr.Owner = seller;
        seller.OwnedRailroads.Add(rr);
        bidderOne.Cash = 20_000;
        bidderTwo.Cash = rr.PurchasePrice / 2 + global::Boxcars.Engine.Domain.GameEngine.AuctionBidIncrement - 1;

        engine.AuctionRailroad(rr);
        engine.SubmitAuctionBid(rr, bidderOne, rr.PurchasePrice / 2);

        Assert.Null(engine.CurrentTurn.AuctionState);
        Assert.Equal(bidderOne, rr.Owner);
        Assert.Contains(rr, bidderOne.OwnedRailroads);
        Assert.DoesNotContain(rr, seller.OwnedRailroads);
    }

    [Fact]
    public void AuctionRailroad_BidderWithoutOpeningCash_IsAutoDropped()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine(GameEngineFixture.ThreePlayerNames, 3);
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Purchase);

        var seller = engine.CurrentTurn.ActivePlayer;
        var bidderOne = engine.Players[1];
        var bidderTwo = engine.Players[2];
        var rr = engine.Railroads[0];
        rr.Owner = seller;
        seller.OwnedRailroads.Add(rr);
        bidderOne.Cash = rr.PurchasePrice / 2 - 1;
        bidderTwo.Cash = rr.PurchasePrice / 2;

        engine.AuctionRailroad(rr);

        Assert.NotNull(engine.CurrentTurn.AuctionState);
        Assert.Contains(engine.CurrentTurn.AuctionState!.Participants, participant =>
            participant.PlayerIndex == bidderOne.Index
            && !participant.IsEligible
            && participant.HasDroppedOut
            && participant.LastAction == AuctionParticipantAction.AutoDropOut);
        Assert.Equal(bidderTwo.Index, engine.CurrentTurn.AuctionState.CurrentBidderPlayerIndex);
    }
}
