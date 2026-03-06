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
}
