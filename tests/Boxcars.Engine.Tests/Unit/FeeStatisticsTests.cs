using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Domain;
using Boxcars.Engine.Tests.Fixtures;
using RailBaronGameEngine = global::Boxcars.Engine.Domain.GameEngine;

namespace Boxcars.Engine.Tests.Unit;

public sealed class FeeStatisticsTests
{
    [Fact]
    public void DeclinePurchase_WhenFeesResolveImmediately_TracksPaidAndCollectedAcrossFeeTypes()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();
        var rider = engine.Players[0];
        var owner = engine.Players[1];
        var privateRailroad = engine.Railroads.First(rr => rr.Index == 0);
        var opponentRailroad = engine.Railroads.First(rr => rr.Index == 1);
        var publicRailroad = new Railroad(
            new RailroadDefinition { Index = 99, Name = "Public Connector", ShortName = "PUB" },
            purchasePrice: 4_000,
            isPublic: true);

        engine.Railroads.Add(publicRailroad);

        privateRailroad.Owner = rider;
        opponentRailroad.Owner = owner;
        rider.OwnedRailroads.Add(privateRailroad);
        owner.OwnedRailroads.Add(opponentRailroad);
        rider.Cash = 100_000;
        owner.Cash = 0;

        PreparePurchaseTurn(engine, rider, [publicRailroad.Index, privateRailroad.Index, opponentRailroad.Index], [opponentRailroad.Index]);

        var riderCashBefore = rider.Cash;
        var ownerCashBefore = owner.Cash;
        engine.DeclinePurchase();

        Assert.Equal(TurnPhase.EndTurn, engine.CurrentTurn.Phase);
        Assert.Equal(riderCashBefore - 7_000, rider.Cash);
        Assert.Equal(ownerCashBefore + 5_000, owner.Cash);
        Assert.Equal(7_000, rider.TotalFeesPaid);
        Assert.Equal(0, rider.TotalFeesCollected);
        Assert.Equal(5_000, rider.FeesPaidToPlayers[owner.Index]);
        Assert.Equal(0, owner.TotalFeesPaid);
        Assert.Equal(5_000, owner.TotalFeesCollected);
    }

    [Fact]
    public void FeeTotals_AccumulateAcrossMultipleTurns()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();
        var alice = engine.Players[0];
        var bob = engine.Players[1];
        var aliceRailroad = engine.Railroads.First(rr => rr.Index == 0);
        var bobRailroad = engine.Railroads.First(rr => rr.Index == 1);

        aliceRailroad.Owner = alice;
        bobRailroad.Owner = bob;
        alice.OwnedRailroads.Add(aliceRailroad);
        bob.OwnedRailroads.Add(bobRailroad);
        alice.Cash = 100_000;
        bob.Cash = 100_000;

        PreparePurchaseTurn(engine, alice, [bobRailroad.Index], [bobRailroad.Index]);
        var aliceCashBefore = alice.Cash;
        var bobCashBefore = bob.Cash;
        engine.DeclinePurchase();

        Assert.Equal(TurnPhase.EndTurn, engine.CurrentTurn.Phase);
        Assert.Equal(aliceCashBefore - 5_000, alice.Cash);
        Assert.Equal(bobCashBefore + 5_000, bob.Cash);
        Assert.Equal(5_000, alice.TotalFeesPaid);
        Assert.Equal(5_000, alice.FeesPaidToPlayers[bob.Index]);
        Assert.Equal(5_000, bob.TotalFeesCollected);

        engine.EndTurn();

        PreparePurchaseTurn(engine, bob, [aliceRailroad.Index], [aliceRailroad.Index]);
        bobCashBefore = bob.Cash;
        aliceCashBefore = alice.Cash;
        engine.DeclinePurchase();

        Assert.Equal(TurnPhase.EndTurn, engine.CurrentTurn.Phase);
        Assert.Equal(bobCashBefore - 5_000, bob.Cash);
        Assert.Equal(aliceCashBefore + 5_000, alice.Cash);
        Assert.Equal(5_000, alice.TotalFeesPaid);
        Assert.Equal(5_000, alice.TotalFeesCollected);
        Assert.Equal(5_000, bob.TotalFeesPaid);
        Assert.Equal(5_000, bob.FeesPaidToPlayers[alice.Index]);
        Assert.Equal(5_000, bob.TotalFeesCollected);
    }

    [Fact]
    public void FeesPaidToPlayers_TracksSeparateTotalsPerOpponent()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine(GameEngineFixture.ThreePlayerNames, playerCount: 3);
        var rider = engine.Players[0];
        var bob = engine.Players[1];
        var charlie = engine.Players[2];
        var bobRailroad = engine.Railroads.First(rr => rr.Index == 0);
        var charlieRailroad = new Railroad(
            new RailroadDefinition { Index = 99, Name = "Western Link", ShortName = "WL" },
            purchasePrice: 4_000,
            isPublic: false);

        engine.Railroads.Add(charlieRailroad);

        bobRailroad.Owner = bob;
        charlieRailroad.Owner = charlie;
        bob.OwnedRailroads.Add(bobRailroad);
        charlie.OwnedRailroads.Add(charlieRailroad);
        rider.Cash = 100_000;

        PreparePurchaseTurn(engine, rider, [bobRailroad.Index, charlieRailroad.Index], [bobRailroad.Index, charlieRailroad.Index]);

        engine.DeclinePurchase();

        Assert.Equal(10_000, rider.TotalFeesPaid);
        Assert.Equal(2, rider.FeesPaidToPlayers.Count);
        Assert.Equal(5_000, rider.FeesPaidToPlayers[bob.Index]);
        Assert.Equal(5_000, rider.FeesPaidToPlayers[charlie.Index]);
        Assert.DoesNotContain(rider.Index, rider.FeesPaidToPlayers.Keys);
    }

    private static void PreparePurchaseTurn(
        RailBaronGameEngine engine,
        Player activePlayer,
        IReadOnlyCollection<int> railroadIndices,
        IReadOnlyCollection<int> railroadsRequiringFullOwnerRate)
    {
        engine.CurrentTurn.ActivePlayer = activePlayer;
        engine.CurrentTurn.Phase = TurnPhase.Purchase;
        engine.CurrentTurn.BonusRollAvailable = false;
        engine.CurrentTurn.PendingFeeAmount = 0;
        engine.CurrentTurn.ArrivalResolution = null;
        engine.CurrentTurn.ForcedSaleState = null;
        engine.CurrentTurn.AuctionState = null;
        engine.CurrentTurn.RailroadsRiddenThisTurn.Clear();
        engine.CurrentTurn.RailroadsRequiringFullOwnerRateThisTurn.Clear();

        foreach (var railroadIndex in railroadIndices)
        {
            engine.CurrentTurn.RailroadsRiddenThisTurn.Add(railroadIndex);
        }

        foreach (var railroadIndex in railroadsRequiringFullOwnerRate)
        {
            engine.CurrentTurn.RailroadsRequiringFullOwnerRateThisTurn.Add(railroadIndex);
        }
    }
}
