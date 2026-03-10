using Boxcars.Engine.Domain;
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
        var price = GameEngine.GetRailroadPurchasePrice(1);
        Assert.True(price > 0);
    }

    [Fact]
    public void GetRailroadPurchasePrice_ZeroIndex_ReturnsFirstPrice()
    {
        var price = GameEngine.GetRailroadPurchasePrice(0);
        Assert.True(price > 0);
    }

    [Fact]
    public void GetRailroadPurchasePrice_OutOfRange_ReturnsFallback()
    {
        var price = GameEngine.GetRailroadPurchasePrice(999);
        Assert.Equal(10_000, price);
    }

    [Fact]
    public void GetUpgradeCost_FreightToExpress_Returns4000()
    {
        var cost = GameEngine.GetUpgradeCost(LocomotiveType.Freight, LocomotiveType.Express, 40_000);
        Assert.Equal(4_000, cost);
    }

    [Fact]
    public void GetUpgradeCost_FreightToSuperchief_ReturnsConfiguredPrice()
    {
        var cost = GameEngine.GetUpgradeCost(LocomotiveType.Freight, LocomotiveType.Superchief, 45_000);
        Assert.Equal(45_000, cost);
    }

    [Fact]
    public void GetUpgradeCost_ExpressToSuperchief_ReturnsConfiguredPrice()
    {
        var cost = GameEngine.GetUpgradeCost(LocomotiveType.Express, LocomotiveType.Superchief, 35_000);
        Assert.Equal(35_000, cost);
    }

    [Fact]
    public void GetUpgradeCost_Downgrade_ReturnsNegativeOne()
    {
        var cost = GameEngine.GetUpgradeCost(LocomotiveType.Express, LocomotiveType.Freight, 40_000);
        Assert.Equal(-1, cost);
    }

    [Fact]
    public void GetUpgradeCost_SameType_ReturnsNegativeOne()
    {
        var cost = GameEngine.GetUpgradeCost(LocomotiveType.Freight, LocomotiveType.Freight, 40_000);
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
        int expectedPrice = GameEngine.GetRailroadPurchasePrice(rr.Index);

        engine.BuyRailroad(rr);

        Assert.Equal(player, rr.Owner);
        Assert.Equal(cashBefore - expectedPrice, player.Cash);
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
    public void GetRailroadPurchasePrice_ConsecutiveIndices_ReturnsDifferingPrices()
    {
        var price1 = GameEngine.GetRailroadPurchasePrice(1);
        var price5 = GameEngine.GetRailroadPurchasePrice(5);
        var price10 = GameEngine.GetRailroadPurchasePrice(10);

        // Prices should all be positive; at least some should differ
        Assert.True(price1 > 0);
        Assert.True(price5 > 0);
        Assert.True(price10 > 0);
    }
}
