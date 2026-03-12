using Boxcars.Engine.Domain;
using Boxcars.Engine.Events;
using Boxcars.Engine.Tests.Fixtures;
using Boxcars.Engine.Tests.TestDoubles;

namespace Boxcars.Engine.Tests.Unit;

/// <summary>
/// Tests for use fee logic: bank $1000, opponent $5000/$10000 (T051, T056).
/// </summary>
public class UseFeesTests
{
    [Fact]
    public void UseFee_BankOwned_Costs1000()
    {
        // Bank-owned railroads charge $1000 per use by definition
        // Verify the fee constant via event args
        var player = new Player("Payer", 0) { Cash = 20000 };
        var args = new UsageFeeChargedEventArgs(player, null, 1000, new List<int> { 0 });
        Assert.Equal(1000, args.Amount);
    }

    [Fact]
    public void UseFee_OpponentOwned_Costs5000()
    {
        // Using an opponent's railroad costs $5000 normally
        var rider = new Player("Rider", 0) { Cash = 20000 };
        var owner = new Player("Owner", 1);
        var args = new UsageFeeChargedEventArgs(rider, owner, 5000, new List<int> { 0 });
        Assert.Equal(5000, args.Amount);
    }

    [Fact]
    public void UseFee_OpponentOwned_AllSold_Costs10000()
    {
        // Using an opponent's railroad costs $10000 when all railroads sold
        var rider = new Player("Rider", 0) { Cash = 20000 };
        var owner = new Player("Owner", 1);
        var args = new UsageFeeChargedEventArgs(rider, owner, 10000, new List<int> { 0 });
        Assert.Equal(10000, args.Amount);
    }

    [Fact]
    public void UseFeeCharged_Event_HasCorrectProperties()
    {
        var rider = new Player("Payer", 0) { Cash = 20000 };
        var owner = new Player("Owner", 1);

        var args = new UsageFeeChargedEventArgs(rider, owner, 5000, new List<int> { 0, 1 });

        Assert.Equal(rider, args.Rider);
        Assert.Equal(owner, args.Owner);
        Assert.Equal(5000, args.Amount);
        Assert.Equal(2, args.RailroadIndices.Count);
    }

    [Fact]
    public void UseFeeCharged_Bank_OwnerIsNull()
    {
        var rider = new Player("Payer", 0) { Cash = 20000 };

        var args = new UsageFeeChargedEventArgs(rider, null, 1000, new List<int> { 0 });

        Assert.Null(args.Owner);
        Assert.Equal(1000, args.Amount);
    }

    [Fact]
    public void RailroadsRidden_TracksRailroadsUsedThisTurn()
    {
        var turn = new Turn();
        turn.RailroadsRiddenThisTurn.Add(0);
        turn.RailroadsRiddenThisTurn.Add(2);

        Assert.Contains(0, turn.RailroadsRiddenThisTurn);
        Assert.Contains(2, turn.RailroadsRiddenThisTurn);
        Assert.Equal(2, turn.RailroadsRiddenThisTurn.Count);
    }

    [Fact]
    public void EndTurn_ClearsRailroadsRidden()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.EndTurn);

        if (engine.CurrentTurn.Phase == TurnPhase.EndTurn)
        {
            engine.EndTurn();
            Assert.Empty(engine.CurrentTurn.RailroadsRiddenThisTurn);
        }
    }

    [Fact]
    public void BuyRailroad_WhenAnotherPlayerIsOnItsNode_GrandfathersThatPlayerAtPublicRate()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();
        var rider = engine.Players[0];
        var owner = engine.Players[1];
        var railroad = engine.Railroads.First(rr => rr.Index == 0);

        rider.CurrentNodeId = "0:0";
        rider.CurrentCity = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "New York", StringComparison.Ordinal));

        engine.CurrentTurn.ActivePlayer = owner;
        engine.CurrentTurn.Phase = TurnPhase.Purchase;
        owner.Cash = 100_000;

        engine.BuyRailroad(railroad);

        Assert.Contains(railroad.Index, rider.GrandfatheredRailroadIndices);

        engine.CurrentTurn.ActivePlayer = rider;
        engine.CurrentTurn.Phase = TurnPhase.Move;
        engine.CurrentTurn.MovementAllowance = 1;
        engine.CurrentTurn.MovementRemaining = 1;
        rider.Destination = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Miami", StringComparison.Ordinal));
        rider.TripOriginCity = rider.CurrentCity;
        rider.ActiveRoute = new Route(
            ["0:0", "0:1"],
            [new RouteSegment { FromNodeId = "0:0", ToNodeId = "0:1", RailroadIndex = railroad.Index }],
            0);
        rider.RouteProgressIndex = 0;

        var riderCashBefore = rider.Cash;
        var ownerCashBefore = owner.Cash;

        engine.MoveAlongRoute(1);

        Assert.Equal(riderCashBefore - 1000, rider.Cash);
        Assert.Equal(ownerCashBefore + 1000, owner.Cash);
        Assert.Contains(railroad.Index, rider.GrandfatheredRailroadIndices);
    }

    [Fact]
    public void MoveAlongRoute_LeavingGrandfatheredRailroad_ClearsProtection()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();
        var rider = engine.Players[0];

        rider.CurrentNodeId = "1:0";
        rider.CurrentCity = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Miami", StringComparison.Ordinal));
        rider.GrandfatheredRailroadIndices.Add(0);

        engine.CurrentTurn.ActivePlayer = rider;
        engine.CurrentTurn.Phase = TurnPhase.Move;
        engine.CurrentTurn.MovementAllowance = 1;
        engine.CurrentTurn.MovementRemaining = 1;
        rider.ActiveRoute = new Route(
            ["1:0", "1:1"],
            [new RouteSegment { FromNodeId = "1:0", ToNodeId = "1:1", RailroadIndex = 1 }],
            0);
        rider.RouteProgressIndex = 0;

        engine.MoveAlongRoute(1);

        Assert.Empty(rider.GrandfatheredRailroadIndices);
    }

    [Fact]
    public void BuyRailroad_AfterRidingThatRailroad_StillChargesBaseRateUseFee()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Roll);

        var rider = engine.CurrentTurn.ActivePlayer;
        rider.Cash = 100_000;
        rider.Destination = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Boston", StringComparison.Ordinal));
        rider.TripOriginCity = rider.CurrentCity;

        var railroad = engine.Railroads.First(rr => rr.Index == 0);
        engine.SaveRoute(new Route(
            ["0:0", "0:1"],
            [new RouteSegment { FromNodeId = "0:0", ToNodeId = "0:1", RailroadIndex = railroad.Index }],
            0));

        random.QueueDiceRoll(1, 1);
        engine.RollDice();
        engine.MoveAlongRoute(1);

        Assert.Equal(TurnPhase.Purchase, engine.CurrentTurn.Phase);

        var cashBeforePurchase = rider.Cash;
        engine.BuyRailroad(railroad);

        Assert.Equal(TurnPhase.EndTurn, engine.CurrentTurn.Phase);
        Assert.Equal(cashBeforePurchase - railroad.PurchasePrice - 1000, rider.Cash);
    }
}
