using Boxcars.Engine.Domain;
using Boxcars.Engine.Tests.Fixtures;
using Boxcars.Engine.Tests.TestDoubles;
using GE = Boxcars.Engine.Domain.GameEngine;

namespace Boxcars.Engine.Tests.Unit;

/// <summary>The Stats tab's running totals, kept by the engine and saved with the game.</summary>
public class GameStatisticsTests
{
    [Fact]
    public void DrawDestination_CountsTheDraw_AndItsSavedWithTheGame()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        random.QueueWeightedDraw(1);
        random.QueueWeightedDraw(0);

        var city = engine.DrawDestination();

        Assert.Equal(1, engine.Statistics.DestinationDraws[city.Name]);
        var restored = GE.FromSnapshot(engine.ToSnapshot(), engine.MapDefinition, new FixedRandomProvider());
        Assert.Equal(1, restored.Statistics.DestinationDraws[city.Name]);
    }

    [Fact]
    public void Fees_AreCreditedToTheRailroadsRiddenAtTheRateThatSetThem()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();
        var rider = engine.Players[0];
        var owner = engine.Players[1];
        var first = engine.Railroads.First(rr => rr.Index == 0);
        var second = engine.Railroads.First(rr => rr.Index == 1);
        foreach (var railroad in new[] { first, second })
        {
            railroad.Owner = owner;
            owner.OwnedRailroads.Add(railroad);
        }

        rider.Cash = 100_000;

        // Both at the owner's full rate: the fee is split between them.
        PreparePurchaseTurn(engine, rider, ridden: [first.Index, second.Index], fullRate: [first.Index, second.Index]);
        engine.DeclinePurchase();
        Assert.Equal(2_500, engine.Statistics.RailroadFeesCollected[first.Index]);
        Assert.Equal(2_500, engine.Statistics.RailroadFeesCollected[second.Index]);

        // Only the first at the full rate: it set the fee, so it gets all of it.
        PreparePurchaseTurn(engine, rider, ridden: [first.Index, second.Index], fullRate: [first.Index]);
        engine.DeclinePurchase();
        Assert.Equal(7_500, engine.Statistics.RailroadFeesCollected[first.Index]);
        Assert.Equal(2_500, engine.Statistics.RailroadFeesCollected[second.Index]);
    }

    [Fact]
    public void ToSnapshot_RecordsTheRoundsNetWorth_KeepingEarlierRounds()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();
        var alice = engine.Players[0];
        var railroad = engine.Railroads.First(rr => rr.Index == 0);
        alice.Cash = 20_000;

        engine.ToSnapshot();
        alice.Cash = 25_000;
        var roundOne = engine.ToSnapshot().Statistics!.NetWorth.Single();

        // Into round 2, Alice has bought the railroad.
        engine.CurrentTurn.TurnNumber = engine.Players.Count + 1;
        railroad.Owner = alice;
        alice.OwnedRailroads.Add(railroad);
        alice.Cash = 25_000 - railroad.PurchasePrice;
        var netWorth = engine.ToSnapshot().Statistics!.NetWorth;

        // A round's point is its last state.
        Assert.Equal(1, roundOne.Round);
        Assert.Equal(25_000, roundOne.Values[alice.Index]);
        Assert.Equal([1, 2], netWorth.Select(round => round.Round).ToArray());
        Assert.Equal(25_000, netWorth[0].Values[alice.Index]);
        Assert.Equal(25_000, netWorth[1].Values[alice.Index]);
    }

    private static void PreparePurchaseTurn(GE engine, Player activePlayer, int[] ridden, int[] fullRate)
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
        engine.CurrentTurn.RailroadsRiddenThisTurn.UnionWith(ridden);
        engine.CurrentTurn.RailroadsRequiringFullOwnerRateThisTurn.UnionWith(fullRate);
    }
}
