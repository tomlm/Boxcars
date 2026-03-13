using Boxcars.Engine.Domain;
using Boxcars.Engine.Tests.Fixtures;
using Boxcars.Engine.Tests.TestDoubles;

namespace Boxcars.Engine.Tests.Unit;

/// <summary>
/// Tests for railroad purchase validation and ownership (T037).
/// </summary>
public class RailroadPurchaseTests
{
    [Fact]
    public void BuyRailroad_Valid_TransfersOwnership()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Purchase);

        if (engine.CurrentTurn.Phase == TurnPhase.Purchase)
        {
            var rr = engine.Railroads.FirstOrDefault(r => r.Owner == null && !r.IsPublic);
            if (rr != null)
            {
                var player = engine.CurrentTurn.ActivePlayer;
                int cashBefore = player.Cash;

                engine.BuyRailroad(rr);

                Assert.Equal(player, rr.Owner);
                Assert.Contains(rr, player.OwnedRailroads);
                Assert.Equal(cashBefore - rr.PurchasePrice - 1000, player.Cash);
            }
        }
    }

    [Fact]
    public void BuyRailroad_AlreadyOwned_ThrowsInvalidOperation()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Purchase);

        if (engine.CurrentTurn.Phase == TurnPhase.Purchase)
        {
            var rr = engine.Railroads[0];
            // Pre-set owner
            rr.Owner = engine.Players[1];

            var ex = Assert.Throws<InvalidOperationException>(() => engine.BuyRailroad(rr));
            Assert.Contains("already owned", ex.Message);
        }
    }

    [Fact]
    public void BuyRailroad_InsufficientFunds_ThrowsInvalidOperation()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Purchase);

        if (engine.CurrentTurn.Phase == TurnPhase.Purchase)
        {
            var player = engine.CurrentTurn.ActivePlayer;
            player.Cash = 0; // Set cash to 0

            var rr = engine.Railroads.FirstOrDefault(r => r.Owner == null && !r.IsPublic);
            if (rr != null)
            {
                var ex = Assert.Throws<InvalidOperationException>(() => engine.BuyRailroad(rr));
                Assert.Contains("Insufficient funds", ex.Message);
            }
        }
    }

    [Fact]
    public void BuyRailroad_NotInPurchasePhase_ThrowsInvalidOperation()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            engine.BuyRailroad(engine.Railroads[0]));
        Assert.Contains("Not in Purchase phase", ex.Message);
    }

    [Fact]
    public void BuyRailroad_NullRailroad_ThrowsArgumentNull()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Purchase);

        if (engine.CurrentTurn.Phase == TurnPhase.Purchase)
        {
            Assert.Throws<ArgumentNullException>(() => engine.BuyRailroad(null!));
        }
    }

    [Fact]
    public void BuyRailroad_AdvancesPhaseToUseFees()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Purchase);

        if (engine.CurrentTurn.Phase == TurnPhase.Purchase)
        {
            var rr = engine.Railroads.FirstOrDefault(r => r.Owner == null && !r.IsPublic);
            if (rr != null && engine.CurrentTurn.ActivePlayer.Cash >= rr.PurchasePrice)
            {
                engine.BuyRailroad(rr);
                // After buying, use fees are resolved and phase advances to EndTurn
                Assert.Equal(TurnPhase.EndTurn, engine.CurrentTurn.Phase);
            }
        }
    }

    [Fact]
    public void BuyRailroad_DoesNotMutateStateOnFailure()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Purchase);

        if (engine.CurrentTurn.Phase == TurnPhase.Purchase)
        {
            var player = engine.CurrentTurn.ActivePlayer;
            int cashBefore = player.Cash;
            player.Cash = 0;

            var rr = engine.Railroads.FirstOrDefault(r => r.Owner == null && !r.IsPublic);
            if (rr != null)
            {
                Assert.Throws<InvalidOperationException>(() => engine.BuyRailroad(rr));
                Assert.Null(rr.Owner);
                Assert.Equal(0, player.Cash);
            }
        }
    }

    [Fact]
    public void BuyRailroad_PublicRailroad_ThrowsInvalidOperation()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Purchase);

        if (engine.CurrentTurn.Phase == TurnPhase.Purchase)
        {
            var publicDef = new global::Boxcars.Engine.Data.Maps.RailroadDefinition { Index = 99, Name = "Public Line" };
            var publicRr = new Railroad(publicDef, 5000, isPublic: true);
            engine.Railroads.Add(publicRr);

            var ex = Assert.Throws<InvalidOperationException>(() => engine.BuyRailroad(publicRr));
            Assert.Contains("Public railroads cannot be purchased", ex.Message);
        }
    }

    [Fact]
    public void BuyRailroad_ExactFunds_Succeeds()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Purchase);

        if (engine.CurrentTurn.Phase == TurnPhase.Purchase)
        {
            var rr = engine.Railroads.FirstOrDefault(r => r.Owner == null && !r.IsPublic);
            if (rr != null)
            {
                var player = engine.CurrentTurn.ActivePlayer;
                player.Cash = rr.PurchasePrice;

                engine.BuyRailroad(rr);

                Assert.Equal(player, rr.Owner);
                Assert.Equal(0, player.Cash);
            }
        }
    }

    [Fact]
    public void BuyRailroad_SetsAllRailroadsSoldWhenLastPurchased()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Purchase);

        if (engine.CurrentTurn.Phase == TurnPhase.Purchase)
        {
            var player = engine.CurrentTurn.ActivePlayer;
            var nonPublic = engine.Railroads.Where(r => !r.IsPublic).ToList();

            // Pre-assign all but one to another player
            for (int i = 1; i < nonPublic.Count; i++)
            {
                nonPublic[i].Owner = engine.Players[1];
                engine.Players[1].OwnedRailroads.Add(nonPublic[i]);
            }

            var last = nonPublic[0];
            Assert.False(engine.AllRailroadsSold);

            engine.BuyRailroad(last);

            Assert.True(engine.AllRailroadsSold);
        }
    }

    [Fact]
    public void BuyRailroad_AllRailroadsSoldRemainsFalseWhenMoreAvailable()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Purchase);

        if (engine.CurrentTurn.Phase == TurnPhase.Purchase)
        {
            var nonPublic = engine.Railroads.Where(r => !r.IsPublic).ToList();
            if (nonPublic.Count > 1)
            {
                engine.BuyRailroad(nonPublic[0]);

                Assert.False(engine.AllRailroadsSold);
            }
        }
    }

    [Fact]
    public void DeclinePurchase_AdvancesToEndTurn()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Purchase);

        if (engine.CurrentTurn.Phase == TurnPhase.Purchase)
        {
            engine.DeclinePurchase();

            Assert.Equal(TurnPhase.EndTurn, engine.CurrentTurn.Phase);
        }
    }

    [Fact]
    public void DeclinePurchase_NotInPurchasePhase_ThrowsInvalidOperation()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        var ex = Assert.Throws<InvalidOperationException>(() => engine.DeclinePurchase());
        Assert.Contains("Not in Purchase phase", ex.Message);
    }

    [Fact]
    public void SellRailroadToBank_ValidForcedSale_ReturnsRailroadToBank()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Purchase);

        var player = engine.CurrentTurn.ActivePlayer;
        var railroad = engine.Railroads.First(rr => rr.Index == 0);
        railroad.Owner = player;
        player.OwnedRailroads.Add(railroad);
        player.Cash = 500;
        engine.CurrentTurn.RailroadsRiddenThisTurn.Add(1);

        engine.DeclinePurchase();
        engine.SellRailroadToBank(railroad);

        Assert.Null(railroad.Owner);
        Assert.DoesNotContain(railroad, player.OwnedRailroads);
    }

    [Fact]
    public void SellRailroadToBank_NotInForcedSale_ThrowsInvalidOperation()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Purchase);

        var player = engine.CurrentTurn.ActivePlayer;
        var railroad = engine.Railroads.First(rr => rr.Index == 0);
        railroad.Owner = player;
        player.OwnedRailroads.Add(railroad);

        var ex = Assert.Throws<InvalidOperationException>(() => engine.SellRailroadToBank(railroad));

        Assert.Contains("UseFees", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
