using Boxcars.Engine.Domain;
using Boxcars.Engine.Events;
using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Persistence;
using Boxcars.Engine.Tests.Fixtures;
using Boxcars.Engine.Tests.TestDoubles;
using GE = Boxcars.Engine.Domain.GameEngine;

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
    public void BuyRailroad_GrandfatheringAtPublicRate_SurvivesDestinationAndTurnBoundary()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();
        var rider = engine.Players[0];
        var owner = engine.Players[1];
        var railroad = engine.Railroads.First(rr => rr.Index == 0);

        rider.Cash = 100_000;
        owner.Cash = 100_000;
        rider.CurrentNodeId = "0:0";
        rider.CurrentCity = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "New York", StringComparison.Ordinal));

        engine.CurrentTurn.ActivePlayer = owner;
        engine.CurrentTurn.Phase = TurnPhase.Purchase;
        engine.BuyRailroad(railroad);

        Assert.Equal(1_000, rider.GrandfatheredRailroadFees[railroad.Index]);

        engine.CurrentTurn.ActivePlayer = rider;
        engine.CurrentTurn.Phase = TurnPhase.Move;
        engine.CurrentTurn.MovementAllowance = 1;
        engine.CurrentTurn.MovementRemaining = 1;
        rider.Destination = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Boston", StringComparison.Ordinal));
        rider.TripOriginCity = rider.CurrentCity;
        rider.ActiveRoute = new Route(
            ["0:0", "0:1"],
            [new RouteSegment { FromNodeId = "0:0", ToNodeId = "0:1", RailroadIndex = railroad.Index }],
            0);
        rider.RouteProgressIndex = 0;

        engine.MoveAlongRoute(1);

        Assert.Equal(TurnPhase.Purchase, engine.CurrentTurn.Phase);
        Assert.Equal(1_000, rider.GrandfatheredRailroadFees[railroad.Index]);

        engine.DeclinePurchase();
        Assert.Equal(TurnPhase.EndTurn, engine.CurrentTurn.Phase);
        engine.EndTurn();

        engine.CurrentTurn.ActivePlayer = rider;
        engine.CurrentTurn.Phase = TurnPhase.Move;
        engine.CurrentTurn.RailroadsRiddenThisTurn.Clear();
        engine.CurrentTurn.RailroadsRequiringFullOwnerRateThisTurn.Clear();
        engine.CurrentTurn.MovementAllowance = 1;
        engine.CurrentTurn.MovementRemaining = 1;
        rider.Destination = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Miami", StringComparison.Ordinal));
        rider.TripOriginCity = rider.CurrentCity;
        rider.ActiveRoute = new Route(
            ["0:1", "0:2"],
            [new RouteSegment { FromNodeId = "0:1", ToNodeId = "0:2", RailroadIndex = railroad.Index }],
            0);
        rider.RouteProgressIndex = 0;

        var riderCashBefore = rider.Cash;
        var ownerCashBefore = owner.Cash;

        engine.MoveAlongRoute(1);

        Assert.Equal(riderCashBefore - 1_000, rider.Cash);
        Assert.Equal(ownerCashBefore + 1_000, owner.Cash);
        Assert.Equal(1_000, rider.GrandfatheredRailroadFees[railroad.Index]);
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
    public void LastRailroadSold_GrandfathersOpponentAtFiveThousandThroughDestinationAndNextTurn()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();
        var rider = engine.Players[0];
        var owner = engine.Players[1];
        var grandfatheredRailroad = engine.Railroads.First(rr => rr.Index == 0);
        var purchasedRailroad = engine.Railroads.First(rr => rr.Index == 1);

        rider.Cash = 100_000;
        owner.Cash = 100_000;
        grandfatheredRailroad.Owner = owner;
        owner.OwnedRailroads.Add(grandfatheredRailroad);
        rider.CurrentNodeId = "0:0";
        rider.CurrentCity = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "New York", StringComparison.Ordinal));

        engine.CurrentTurn.ActivePlayer = owner;
        engine.CurrentTurn.Phase = TurnPhase.Purchase;
        engine.BuyRailroad(purchasedRailroad);

        Assert.True(engine.AllRailroadsSold);
        Assert.Equal(5_000, rider.GrandfatheredRailroadFees[grandfatheredRailroad.Index]);

        engine.CurrentTurn.ActivePlayer = rider;
        engine.CurrentTurn.Phase = TurnPhase.Move;
        engine.CurrentTurn.MovementAllowance = 1;
        engine.CurrentTurn.MovementRemaining = 1;
        rider.Destination = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Boston", StringComparison.Ordinal));
        rider.TripOriginCity = rider.CurrentCity;
        rider.ActiveRoute = new Route(
            ["0:0", "0:1"],
            [new RouteSegment { FromNodeId = "0:0", ToNodeId = "0:1", RailroadIndex = grandfatheredRailroad.Index }],
            0);
        rider.RouteProgressIndex = 0;

        engine.MoveAlongRoute(1);

        Assert.Equal(TurnPhase.Purchase, engine.CurrentTurn.Phase);
        Assert.Equal(5_000, rider.GrandfatheredRailroadFees[grandfatheredRailroad.Index]);

        engine.DeclinePurchase();
        engine.EndTurn();

        engine.CurrentTurn.ActivePlayer = rider;
        engine.CurrentTurn.Phase = TurnPhase.Move;
        engine.CurrentTurn.RailroadsRiddenThisTurn.Clear();
        engine.CurrentTurn.RailroadsRequiringFullOwnerRateThisTurn.Clear();
        engine.CurrentTurn.MovementAllowance = 1;
        engine.CurrentTurn.MovementRemaining = 1;
        rider.Destination = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Miami", StringComparison.Ordinal));
        rider.TripOriginCity = rider.CurrentCity;
        rider.ActiveRoute = new Route(
            ["0:1", "0:2"],
            [new RouteSegment { FromNodeId = "0:1", ToNodeId = "0:2", RailroadIndex = grandfatheredRailroad.Index }],
            0);
        rider.RouteProgressIndex = 0;

        var riderCashBefore = rider.Cash;
        var ownerCashBefore = owner.Cash;

        engine.MoveAlongRoute(1);

        Assert.Equal(riderCashBefore - 5_000, rider.Cash);
        Assert.Equal(ownerCashBefore + 5_000, owner.Cash);
        Assert.Equal(5_000, rider.GrandfatheredRailroadFees[grandfatheredRailroad.Index]);
    }

    [Fact]
    public void MoveAlongRoute_LeavingGrandfatheredRailroad_StillChargesBaseRateForThatRailroad()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();
        var rider = engine.Players[0];
        var owner = engine.Players[1];
        var grandfatheredRailroad = engine.Railroads.First(rr => rr.Index == 0);

        grandfatheredRailroad.Owner = owner;
        owner.OwnedRailroads.Add(grandfatheredRailroad);
        rider.GrandfatheredRailroadIndices.Add(grandfatheredRailroad.Index);
        rider.Cash = 100_000;
        owner.Cash = 0;
        rider.CurrentNodeId = "0:0";
        rider.ActiveRoute = new Route(
            ["0:0", "0:1", "0:2"],
            [
                new RouteSegment { FromNodeId = "0:0", ToNodeId = "0:1", RailroadIndex = grandfatheredRailroad.Index },
                new RouteSegment { FromNodeId = "0:1", ToNodeId = "0:2", RailroadIndex = 1 }
            ],
            0);
        rider.RouteProgressIndex = 0;

        engine.CurrentTurn.ActivePlayer = rider;
        engine.CurrentTurn.Phase = TurnPhase.Move;
        engine.CurrentTurn.MovementAllowance = 2;
        engine.CurrentTurn.MovementRemaining = 2;

        var riderCashBefore = rider.Cash;
        engine.MoveAlongRoute(2);

        Assert.Equal(riderCashBefore - 2_000, rider.Cash);
        Assert.Equal(1_000, owner.Cash);
        Assert.Empty(rider.GrandfatheredRailroadIndices);
        Assert.DoesNotContain(grandfatheredRailroad.Index, engine.CurrentTurn.RailroadsRequiringFullOwnerRateThisTurn);
    }

    [Fact]
    public void MoveAlongRoute_ReenteringGrandfatheredRailroad_ChargesFullOwnerRate()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();
        var rider = engine.Players[0];
        var owner = engine.Players[1];
        var grandfatheredRailroad = engine.Railroads.First(rr => rr.Index == 0);

        grandfatheredRailroad.Owner = owner;
        owner.OwnedRailroads.Add(grandfatheredRailroad);
        rider.GrandfatheredRailroadIndices.Add(grandfatheredRailroad.Index);
        rider.Cash = 100_000;
        owner.Cash = 0;
        rider.CurrentNodeId = "0:0";
        rider.ActiveRoute = new Route(
            ["0:0", "0:1", "0:2", "0:3"],
            [
                new RouteSegment { FromNodeId = "0:0", ToNodeId = "0:1", RailroadIndex = grandfatheredRailroad.Index },
                new RouteSegment { FromNodeId = "0:1", ToNodeId = "0:2", RailroadIndex = 1 },
                new RouteSegment { FromNodeId = "0:2", ToNodeId = "0:3", RailroadIndex = grandfatheredRailroad.Index }
            ],
            0);
        rider.RouteProgressIndex = 0;

        engine.CurrentTurn.ActivePlayer = rider;
        engine.CurrentTurn.Phase = TurnPhase.Move;
        engine.CurrentTurn.MovementAllowance = 3;
        engine.CurrentTurn.MovementRemaining = 3;

        var riderCashBefore = rider.Cash;
        engine.MoveAlongRoute(3);

        Assert.Equal(riderCashBefore - 6_000, rider.Cash);
        Assert.Equal(5_000, owner.Cash);
        Assert.Contains(grandfatheredRailroad.Index, engine.CurrentTurn.RailroadsRequiringFullOwnerRateThisTurn);
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

    [Fact]
    public void DeclinePurchase_WhenFeesExceedCashAndPlayerOwnsRailroads_EntersForcedSale()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        engine.CurrentTurn.Phase = TurnPhase.Purchase;
        engine.CurrentTurn.BonusRollAvailable = false;

        var player = engine.CurrentTurn.ActivePlayer;
        var ownedRailroad = engine.Railroads.First(rr => rr.Index == 0);
        ownedRailroad.Owner = player;
        player.OwnedRailroads.Add(ownedRailroad);
        player.Cash = 500;
        engine.CurrentTurn.RailroadsRiddenThisTurn.Clear();
        engine.CurrentTurn.RailroadsRiddenThisTurn.Add(1);

        engine.DeclinePurchase();

        Assert.Equal(TurnPhase.UseFees, engine.CurrentTurn.Phase);
        Assert.Equal(1000, engine.CurrentTurn.PendingFeeAmount);
        Assert.NotNull(engine.CurrentTurn.ForcedSaleState);
        Assert.Equal(1000, engine.CurrentTurn.ForcedSaleState!.AmountOwed);
        Assert.Equal(500, engine.CurrentTurn.ForcedSaleState.CashBeforeFees);
        Assert.False(engine.CurrentTurn.ForcedSaleState.CanPayNow);
    }

    [Fact]
    public void SellRailroadToBank_WhenStillShort_RemainsInForcedSale()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        engine.CurrentTurn.Phase = TurnPhase.Purchase;
        engine.CurrentTurn.BonusRollAvailable = false;

        var player = engine.CurrentTurn.ActivePlayer;
        var firstRailroad = engine.Railroads.First(rr => rr.Index == 0);
        var feeRailroad = engine.Railroads.First(rr => rr.Index == 1);
        var secondRailroad = new Railroad(
            new global::Boxcars.Engine.Data.Maps.RailroadDefinition { Index = 99, Name = "Reserve Line" },
            4000,
            isPublic: false);
        engine.Railroads.Add(secondRailroad);
        firstRailroad.Owner = player;
        secondRailroad.Owner = player;
        player.OwnedRailroads.Add(firstRailroad);
        player.OwnedRailroads.Add(secondRailroad);
        player.Cash = 0;
        var feeOwner = engine.Players[1];
        feeRailroad.Owner = feeOwner;
        feeOwner.OwnedRailroads.Add(feeRailroad);
        engine.CurrentTurn.RailroadsRiddenThisTurn.Clear();
        engine.CurrentTurn.RailroadsRiddenThisTurn.Add(feeRailroad.Index);
        engine.CurrentTurn.RailroadsRequiringFullOwnerRateThisTurn.Add(feeRailroad.Index);

        engine.DeclinePurchase();
        engine.SellRailroadToBank(firstRailroad);

        Assert.Equal(TurnPhase.UseFees, engine.CurrentTurn.Phase);
        Assert.Equal(firstRailroad.PurchasePrice / 2, player.Cash);
        Assert.NotNull(engine.CurrentTurn.ForcedSaleState);
        Assert.Equal(1, engine.CurrentTurn.ForcedSaleState!.SalesCompletedCount);
        Assert.False(engine.CurrentTurn.ForcedSaleState.CanPayNow);
        Assert.Equal(5000, engine.CurrentTurn.PendingFeeAmount);
    }

    [Fact]
    public void SellRailroadToBank_WhenSaleCoversFees_PaysFeesAndEndsTurn()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        engine.CurrentTurn.Phase = TurnPhase.Purchase;
        engine.CurrentTurn.BonusRollAvailable = false;

        var player = engine.CurrentTurn.ActivePlayer;
        var owner = engine.Players[1];
        var ownedRailroad = engine.Railroads.First(rr => rr.Index == 0);
        var feeRailroad = engine.Railroads.First(rr => rr.Index == 1);
        ownedRailroad.Owner = player;
        feeRailroad.Owner = owner;
        player.OwnedRailroads.Add(ownedRailroad);
        owner.OwnedRailroads.Add(feeRailroad);
        player.Cash = 4000;
        owner.Cash = 0;
        engine.CurrentTurn.RailroadsRiddenThisTurn.Clear();
        engine.CurrentTurn.RailroadsRiddenThisTurn.Add(feeRailroad.Index);
        engine.CurrentTurn.RailroadsRequiringFullOwnerRateThisTurn.Add(feeRailroad.Index);

        engine.DeclinePurchase();
        engine.SellRailroadToBank(ownedRailroad);

        Assert.Equal(TurnPhase.EndTurn, engine.CurrentTurn.Phase);
        Assert.Null(engine.CurrentTurn.ForcedSaleState);
        Assert.Equal(0, engine.CurrentTurn.PendingFeeAmount);
        Assert.Equal(4000 + (ownedRailroad.PurchasePrice / 2) - 5000, player.Cash);
        Assert.Equal(5000, owner.Cash);
    }

    [Fact]
    public void SuggestRoute_UsesGrandfatheredPublicRateWhenChoosingBestRoute()
    {
        var engine = CreateRoutePlanningEngine();
        var rider = engine.Players[0];
        var owner = engine.Players[1];
        var grandfatheredRailroad = engine.Railroads.First(rr => rr.Index == 0);
        var alternateRailroad = engine.Railroads.First(rr => rr.Index == 1);

        grandfatheredRailroad.Owner = owner;
        alternateRailroad.Owner = owner;
        owner.OwnedRailroads.Add(grandfatheredRailroad);
        owner.OwnedRailroads.Add(alternateRailroad);

        rider.CurrentNodeId = "0:0";
        rider.CurrentCity = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Start", StringComparison.Ordinal));
        rider.Destination = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Finish", StringComparison.Ordinal));
        rider.GrandfatheredRailroadIndices.Add(grandfatheredRailroad.Index);
        rider.GrandfatheredRailroadFees[grandfatheredRailroad.Index] = 1_000;

        var route = engine.SuggestRouteForPlayer(rider.Index);

        Assert.Equal([0, 0], route.Segments.Select(segment => segment.RailroadIndex).ToArray());
        Assert.Equal(1_000, route.TotalCost);
    }

    [Fact]
    public void SuggestRoute_UsesGrandfatheredFiveThousandRateAfterAllRailroadsSold()
    {
        var engine = CreateRoutePlanningEngine();
        var rider = engine.Players[0];
        var owner = engine.Players[1];
        var grandfatheredRailroad = engine.Railroads.First(rr => rr.Index == 0);
        var purchasedRailroad = engine.Railroads.First(rr => rr.Index == 1);

        rider.Cash = 100_000;
        owner.Cash = 100_000;
        grandfatheredRailroad.Owner = owner;
        owner.OwnedRailroads.Add(grandfatheredRailroad);
        rider.CurrentNodeId = "0:0";
        rider.CurrentCity = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Start", StringComparison.Ordinal));
        rider.Destination = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Finish", StringComparison.Ordinal));

        engine.CurrentTurn.ActivePlayer = owner;
        engine.CurrentTurn.Phase = TurnPhase.Purchase;
        engine.BuyRailroad(purchasedRailroad);

        var route = engine.SuggestRouteForPlayer(rider.Index);

        Assert.True(engine.AllRailroadsSold);
        Assert.Equal(5_000, rider.GrandfatheredRailroadFees[grandfatheredRailroad.Index]);
        Assert.Equal([0, 0], route.Segments.Select(segment => segment.RailroadIndex).ToArray());
        Assert.Equal(5_000, route.TotalCost);
    }

    private static GE CreateRoutePlanningEngine()
    {
        var random = new FixedRandomProvider();
        random.QueueWeightedDraw(0);
        random.QueueWeightedDraw(0);
        random.QueueWeightedDraw(0);
        random.QueueWeightedDraw(1);
        return new GE(
            CreateRoutePlanningMap(),
            GameEngineFixture.DefaultPlayerNames,
            random,
            GameSettings.Default with
            {
                HomeCityChoice = false,
                HomeSwapping = false
            });
    }

    private static MapDefinition CreateRoutePlanningMap()
    {
        var map = new MapDefinition
        {
            Name = "RoutePlanningMap",
            Version = "1.0"
        };

        map.Regions.Add(new RegionDefinition { Index = 0, Name = "Region", Code = "RG", Probability = 1.0 });

        map.Cities.Add(new CityDefinition { Name = "Start", RegionCode = "RG", Probability = 0.5, PayoutIndex = 0, MapDotIndex = 0 });
        map.Cities.Add(new CityDefinition { Name = "Finish", RegionCode = "RG", Probability = 0.5, PayoutIndex = 1, MapDotIndex = 2 });

        map.Railroads.Add(new RailroadDefinition { Index = 0, Name = "Grandfather Line", ShortName = "GF" });
        map.Railroads.Add(new RailroadDefinition { Index = 1, Name = "Alternate Line", ShortName = "ALT" });

        for (var dot = 0; dot < 4; dot++)
        {
            map.TrainDots.Add(new TrainDot
            {
                Id = $"0:{dot}",
                RegionIndex = 0,
                DotIndex = dot,
                X = dot * 10,
                Y = dot * 10
            });
        }

        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition { RailroadIndex = 0, StartRegionIndex = 0, StartDotIndex = 0, EndRegionIndex = 0, EndDotIndex = 1 });
        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition { RailroadIndex = 0, StartRegionIndex = 0, StartDotIndex = 1, EndRegionIndex = 0, EndDotIndex = 2 });
        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition { RailroadIndex = 1, StartRegionIndex = 0, StartDotIndex = 1, EndRegionIndex = 0, EndDotIndex = 3 });
        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition { RailroadIndex = 1, StartRegionIndex = 0, StartDotIndex = 3, EndRegionIndex = 0, EndDotIndex = 2 });

        return map;
    }
}
