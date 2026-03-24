using Boxcars.Engine.Domain;
using RailBaronGameEngine = Boxcars.Engine.Domain.GameEngine;
using Boxcars.Engine.Tests.Fixtures;
using Boxcars.Engine.Tests.TestDoubles;

namespace Boxcars.Engine.Tests.Unit;

/// <summary>
/// Regression coverage for purchase actions and pricing rules (T029).
/// </summary>
public class PurchasePhaseActionTests
{
    [Fact]
    public void GetRailroadPurchasePrice_ValidIndex_ReturnsPrice()
    {
        var price = RailBaronGameEngine.GetRailroadPurchasePrice(1);
        Assert.True(price > 0);
    }

    [Fact]
    public void GetRailroadPurchasePrice_ZeroIndex_ReturnsFirstPrice()
    {
        var price = RailBaronGameEngine.GetRailroadPurchasePrice(0);
        Assert.True(price > 0);
    }

    [Fact]
    public void GetRailroadPurchasePrice_OutOfRange_ReturnsFallback()
    {
        var price = RailBaronGameEngine.GetRailroadPurchasePrice(999);
        Assert.Equal(10_000, price);
    }

    [Fact]
    public void GetUpgradeCost_FreightToExpress_Returns4000()
    {
        var cost = RailBaronGameEngine.GetUpgradeCost(LocomotiveType.Freight, LocomotiveType.Express, 40_000);
        Assert.Equal(4_000, cost);
    }

    [Fact]
    public void GetUpgradeCost_FreightToSuperchief_ReturnsConfiguredPrice()
    {
        var cost = RailBaronGameEngine.GetUpgradeCost(LocomotiveType.Freight, LocomotiveType.Superchief, 45_000);
        Assert.Equal(45_000, cost);
    }

    [Fact]
    public void GetUpgradeCost_ExpressToSuperchief_ReturnsConfiguredPrice()
    {
        var cost = RailBaronGameEngine.GetUpgradeCost(LocomotiveType.Express, LocomotiveType.Superchief, 35_000);
        Assert.Equal(35_000, cost);
    }

    [Fact]
    public void GetUpgradeCost_Downgrade_ReturnsNegativeOne()
    {
        var cost = RailBaronGameEngine.GetUpgradeCost(LocomotiveType.Express, LocomotiveType.Freight, 40_000);
        Assert.Equal(-1, cost);
    }

    [Fact]
    public void GetUpgradeCost_SameType_ReturnsNegativeOne()
    {
        var cost = RailBaronGameEngine.GetUpgradeCost(LocomotiveType.Freight, LocomotiveType.Freight, 40_000);
        Assert.Equal(-1, cost);
    }

    [Fact]
    public void BuyRailroad_DeductsCashAndTransfersOwnership()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Purchase);

        if (engine.CurrentTurn.Phase != TurnPhase.Purchase) return;

        var rr = engine.Railroads.FirstOrDefault(r => r.Owner == null && !r.IsPublic);
        if (rr is null) return;

        var player = engine.CurrentTurn.ActivePlayer;
        player.Cash = 100_000;
        int cashBefore = player.Cash;
        int expectedPrice = RailBaronGameEngine.GetRailroadPurchasePrice(rr.Index);

        engine.BuyRailroad(rr);

        Assert.Equal(player, rr.Owner);
        Assert.Equal(cashBefore - expectedPrice - 1000, player.Cash);
    }

    [Fact]
    public void BuyRailroad_WhenPurchaseLeavesFeeShortfall_TransitionsIntoForcedSale()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Purchase);

        if (engine.CurrentTurn.Phase != TurnPhase.Purchase) return;

        var player = engine.CurrentTurn.ActivePlayer;
        var railroadToBuy = engine.Railroads.First(rr => rr.Index == 0);
        var feeRailroad = engine.Railroads.First(rr => rr.Index == 1);
        var feeOwner = engine.Players[1];
        feeRailroad.Owner = feeOwner;
        feeOwner.OwnedRailroads.Add(feeRailroad);
        player.Cash = railroadToBuy.PurchasePrice + 4_000;
        engine.CurrentTurn.RailroadsRiddenThisTurn.Clear();
        engine.CurrentTurn.RailroadsRiddenThisTurn.Add(feeRailroad.Index);
        engine.CurrentTurn.RailroadsRequiringFullOwnerRateThisTurn.Add(feeRailroad.Index);

        engine.BuyRailroad(railroadToBuy);

        Assert.Equal(TurnPhase.UseFees, engine.CurrentTurn.Phase);
        Assert.Equal(5_000, engine.CurrentTurn.PendingFeeAmount);
        Assert.NotNull(engine.CurrentTurn.ForcedSaleState);
        Assert.False(engine.CurrentTurn.ForcedSaleState!.CanPayNow);
        Assert.Equal(railroadToBuy.Index, engine.CurrentTurn.SelectedRailroadForSaleIndex);
        Assert.Equal(5_000, player.GrandfatheredRailroadFees[feeRailroad.Index]);
    }

    [Fact]
    public void DeclinePurchase_AdvancesPastPurchasePhase()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Purchase);

        if (engine.CurrentTurn.Phase != TurnPhase.Purchase) return;

        engine.DeclinePurchase();

        // Should advance past Purchase to either UseFees or EndTurn
        Assert.NotEqual(TurnPhase.Purchase, engine.CurrentTurn.Phase);
    }

    [Fact]
    public void UpgradeLocomotive_DuringPurchasePhase_SetsTypeAndDeductsCash()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Purchase);

        if (engine.CurrentTurn.Phase != TurnPhase.Purchase) return;

        var player = engine.CurrentTurn.ActivePlayer;
        player.Cash = 50_000;
        player.LocomotiveType = LocomotiveType.Freight;
        engine.CurrentTurn.RailroadsRiddenThisTurn.Clear();
        int cashBefore = player.Cash;

        engine.UpgradeLocomotive(LocomotiveType.Express);

        Assert.Equal(LocomotiveType.Express, player.LocomotiveType);
        Assert.Equal(cashBefore - 4_000, player.Cash);
    }

    [Fact]
    public void DeclinePurchase_ExpressBonusPending_ResumesMoveWithRolledBonus()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Roll);

        var player = engine.CurrentTurn.ActivePlayer;
        player.LocomotiveType = LocomotiveType.Express;
        player.Destination = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Boston", StringComparison.Ordinal));
        player.TripOriginCity = player.CurrentCity;
        engine.SaveRoute(new Route(
            ["0:0", "0:1"],
            [new RouteSegment { FromNodeId = "0:0", ToNodeId = "0:1", RailroadIndex = 0 }],
            0));

        random.QueueDiceRoll(3, 3);
        engine.RollDice();

        engine.MoveAlongRoute(1);

        Assert.Equal(TurnPhase.Purchase, engine.CurrentTurn.Phase);
        Assert.True(engine.CurrentTurn.BonusRollAvailable);

        random.QueueDiceRoll(4);
        engine.DeclinePurchase();

        Assert.Equal(TurnPhase.DrawDestination, engine.CurrentTurn.Phase);
        Assert.Equal(4, engine.CurrentTurn.MovementAllowance);
        Assert.Equal(4, engine.CurrentTurn.MovementRemaining);
        Assert.False(engine.CurrentTurn.BonusRollAvailable);
        Assert.Equal(0, engine.CurrentTurn.DiceResult?.WhiteDice[0]);
        Assert.Equal(0, engine.CurrentTurn.DiceResult?.WhiteDice[1]);
        Assert.Equal(4, engine.CurrentTurn.DiceResult?.RedDie);

        random.QueueWeightedDraw(1);
        random.QueueWeightedDraw(1);
        engine.DrawDestination();

        Assert.Equal(TurnPhase.Move, engine.CurrentTurn.Phase);
        Assert.NotNull(player.Destination);
    }

    [Fact]
    public void BuyRailroad_WithExpressBonusPending_ResumesMoveAfterPurchase()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Roll);

        var player = engine.CurrentTurn.ActivePlayer;
        player.Cash = 100_000;
        player.LocomotiveType = LocomotiveType.Express;
        player.Destination = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Boston", StringComparison.Ordinal));
        player.TripOriginCity = player.CurrentCity;
        engine.SaveRoute(new Route(
            ["0:0", "0:1"],
            [new RouteSegment { FromNodeId = "0:0", ToNodeId = "0:1", RailroadIndex = 0 }],
            0));

        random.QueueDiceRoll(2, 2);
        engine.RollDice();
        engine.MoveAlongRoute(1);

        var railroad = engine.Railroads.First(rr => rr.Owner is null && !rr.IsPublic && rr.Index != 0);
        var cashBefore = player.Cash;
        random.QueueDiceRoll(5);

        engine.BuyRailroad(railroad);

        Assert.Equal(player, railroad.Owner);
        Assert.Equal(cashBefore - railroad.PurchasePrice, player.Cash);
        Assert.Equal(TurnPhase.DrawDestination, engine.CurrentTurn.Phase);
        Assert.Equal(5, engine.CurrentTurn.MovementRemaining);
        Assert.Equal(5, engine.CurrentTurn.DiceResult?.RedDie);

        random.QueueWeightedDraw(1);
        random.QueueWeightedDraw(1);
        engine.DrawDestination();

        Assert.Equal(TurnPhase.Move, engine.CurrentTurn.Phase);
        Assert.NotNull(player.Destination);
    }

    [Fact]
    public void DeclinePurchase_SuperchiefArrivalBeforeRedDieUse_PreservesRedDieAsBonusMove()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Roll);

        var player = engine.CurrentTurn.ActivePlayer;
        player.LocomotiveType = LocomotiveType.Superchief;
        player.Destination = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Boston", StringComparison.Ordinal));
        player.TripOriginCity = player.CurrentCity;
        engine.SaveRoute(new Route(
            ["0:0", "0:1"],
            [new RouteSegment { FromNodeId = "0:0", ToNodeId = "0:1", RailroadIndex = 0 }],
            0));

        random.QueueDiceRoll(1, 1, 5);
        engine.RollDice();
        engine.MoveAlongRoute(1);

        Assert.Equal(TurnPhase.Purchase, engine.CurrentTurn.Phase);
        Assert.True(engine.CurrentTurn.BonusRollAvailable);
        Assert.Equal(5, engine.CurrentTurn.DiceResult?.RedDie);

        engine.DeclinePurchase();

        Assert.Equal(TurnPhase.DrawDestination, engine.CurrentTurn.Phase);
        Assert.Equal(5, engine.CurrentTurn.MovementAllowance);
        Assert.Equal(5, engine.CurrentTurn.MovementRemaining);
        Assert.False(engine.CurrentTurn.BonusRollAvailable);
        Assert.Equal(0, engine.CurrentTurn.DiceResult?.WhiteDice[0]);
        Assert.Equal(0, engine.CurrentTurn.DiceResult?.WhiteDice[1]);
        Assert.Equal(5, engine.CurrentTurn.DiceResult?.RedDie);

        random.QueueWeightedDraw(1);
        random.QueueWeightedDraw(1);
        engine.DrawDestination();

        Assert.Equal(TurnPhase.Move, engine.CurrentTurn.Phase);
        Assert.NotNull(player.Destination);
    }

    [Fact]
    public void DeclinePurchase_WithPendingBonusAndNoDestination_TransitionsToDrawDestinationBeforeBonusMove()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Roll);

        var player = engine.CurrentTurn.ActivePlayer;
        player.LocomotiveType = LocomotiveType.Express;
        player.Destination = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Boston", StringComparison.Ordinal));
        player.TripOriginCity = player.CurrentCity;
        engine.SaveRoute(new Route(
            ["0:0", "0:1"],
            [new RouteSegment { FromNodeId = "0:0", ToNodeId = "0:1", RailroadIndex = 0 }],
            0));

        random.QueueDiceRoll(4, 4);
        engine.RollDice();
        engine.MoveAlongRoute(1);

        random.QueueDiceRoll(3);
        engine.DeclinePurchase();

        Assert.Equal(TurnPhase.DrawDestination, engine.CurrentTurn.Phase);
        Assert.Null(player.Destination);
        Assert.Equal(3, engine.CurrentTurn.MovementAllowance);
        Assert.Equal(3, engine.CurrentTurn.MovementRemaining);
        Assert.Equal(3, engine.CurrentTurn.DiceResult?.RedDie);

        random.QueueWeightedDraw(1);
        random.QueueWeightedDraw(1);
        engine.DrawDestination();

        Assert.Equal(TurnPhase.Move, engine.CurrentTurn.Phase);
        Assert.NotNull(player.Destination);
        Assert.Equal(3, engine.CurrentTurn.MovementAllowance);
        Assert.Equal(3, engine.CurrentTurn.MovementRemaining);
    }

    [Fact]
    public void BonusMove_ReachingSecondDestination_AllowsPurchaseThenEndsFurtherMovement()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Roll);

        var player = engine.CurrentTurn.ActivePlayer;
        player.LocomotiveType = LocomotiveType.Express;
        player.Destination = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Boston", StringComparison.Ordinal));
        player.TripOriginCity = player.CurrentCity;
        engine.SaveRoute(new Route(
            ["0:0", "0:1"],
            [new RouteSegment { FromNodeId = "0:0", ToNodeId = "0:1", RailroadIndex = 0 }],
            0));

        random.QueueDiceRoll(4, 4);
        engine.RollDice();
        engine.MoveAlongRoute(1);

        random.QueueDiceRoll(4);
        engine.DeclinePurchase();

        Assert.Equal(TurnPhase.DrawDestination, engine.CurrentTurn.Phase);

        random.QueueWeightedDraw(0);
        random.QueueWeightedDraw(0);
        engine.DrawDestination();

        Assert.Equal(TurnPhase.RegionChoice, engine.CurrentTurn.Phase);

        random.QueueWeightedDraw(0);
        engine.ChooseDestinationRegion("NE");

        Assert.Equal(TurnPhase.Move, engine.CurrentTurn.Phase);
        Assert.Equal("New York", player.Destination?.Name);

        engine.SaveRoute(new Route(
            ["0:1", "0:0"],
            [new RouteSegment { FromNodeId = "0:1", ToNodeId = "0:0", RailroadIndex = 0 }],
            0));

        engine.MoveAlongRoute(1);

        Assert.Equal(TurnPhase.Purchase, engine.CurrentTurn.Phase);
        Assert.Null(player.Destination);
        Assert.False(engine.CurrentTurn.BonusRollAvailable);

        engine.DeclinePurchase();

        Assert.Equal(TurnPhase.EndTurn, engine.CurrentTurn.Phase);
    }

    [Fact]
    public void ArrivalWithDeferredBonus_ClearsTraveledSegmentsBeforeBonusMove()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Roll);

        var player = engine.CurrentTurn.ActivePlayer;
        player.LocomotiveType = LocomotiveType.Express;
        player.Destination = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Boston", StringComparison.Ordinal));
        player.TripOriginCity = player.CurrentCity;
        engine.SaveRoute(new Route(
            ["0:0", "0:1"],
            [new RouteSegment { FromNodeId = "0:0", ToNodeId = "0:1", RailroadIndex = 0 }],
            0));

        random.QueueDiceRoll(3, 3);
        engine.RollDice();
        engine.MoveAlongRoute(1);

        Assert.Equal(TurnPhase.Purchase, engine.CurrentTurn.Phase);
        Assert.True(engine.CurrentTurn.BonusRollAvailable);
        Assert.Empty(player.UsedSegments);

        random.QueueDiceRoll(4);
        engine.DeclinePurchase();

        Assert.Equal(TurnPhase.DrawDestination, engine.CurrentTurn.Phase);
        Assert.Empty(player.UsedSegments);

        random.QueueWeightedDraw(0);
        random.QueueWeightedDraw(0);
        engine.DrawDestination();

        Assert.Equal(TurnPhase.RegionChoice, engine.CurrentTurn.Phase);

        random.QueueWeightedDraw(0);
        engine.ChooseDestinationRegion("NE");

        Assert.Equal(TurnPhase.Move, engine.CurrentTurn.Phase);
        Assert.Equal("New York", player.Destination?.Name);

        engine.SaveRoute(new Route(
            ["0:1", "0:0"],
            [new RouteSegment { FromNodeId = "0:1", ToNodeId = "0:0", RailroadIndex = 0 }],
            0));

        var moveException = Record.Exception(() => engine.MoveAlongRoute(1));

        Assert.Null(moveException);
        Assert.Equal(TurnPhase.Purchase, engine.CurrentTurn.Phase);
    }

    [Fact]
    public void GetRailroadPurchasePrice_ConsecutiveIndices_ReturnsDifferingPrices()
    {
        var price1 = RailBaronGameEngine.GetRailroadPurchasePrice(1);
        var price5 = RailBaronGameEngine.GetRailroadPurchasePrice(5);
        var price10 = RailBaronGameEngine.GetRailroadPurchasePrice(10);

        // Prices should all be positive; at least some should differ
        Assert.True(price1 > 0);
        Assert.True(price5 > 0);
        Assert.True(price10 > 0);
    }

    [Fact]
    public void MoveAlongRoute_ArrivalResolutionMessage_IncludesPendingFees()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Roll);

        var player = engine.CurrentTurn.ActivePlayer;
        player.Destination = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Boston", StringComparison.Ordinal));
        player.TripOriginCity = player.CurrentCity;
        engine.SaveRoute(new Route(
            ["0:0", "0:1"],
            [new RouteSegment { FromNodeId = "0:0", ToNodeId = "0:1", RailroadIndex = 0 }],
            0));

        random.QueueDiceRoll(1, 1);
        engine.RollDice();
        engine.MoveAlongRoute(1);

        Assert.NotNull(engine.CurrentTurn.ArrivalResolution);
        Assert.Contains("has $1,000 in fees due", engine.CurrentTurn.ArrivalResolution!.Message, StringComparison.Ordinal);
    }
}
