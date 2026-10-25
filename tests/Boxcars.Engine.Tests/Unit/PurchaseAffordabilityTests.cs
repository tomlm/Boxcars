using global::Boxcars.Data;

namespace Boxcars.Engine.Tests.Unit;

public class PurchaseAffordabilityTests
{
    [Theory]
    [InlineData(50_000, 20_000, 28_000, PurchaseAffordabilityLevel.Comfortable)]
    [InlineData(32_000, 20_000, 10_000, PurchaseAffordabilityLevel.Comfortable)]
    [InlineData(30_000, 20_000, 8_000, PurchaseAffordabilityLevel.Tight)]
    [InlineData(22_000, 20_000, 0, PurchaseAffordabilityLevel.Tight)]
    [InlineData(21_000, 20_000, -1_000, PurchaseAffordabilityLevel.NeedsSale)]
    [InlineData(20_000, 20_000, -2_000, PurchaseAffordabilityLevel.NeedsSale)]
    [InlineData(19_999, 20_000, -2_001, PurchaseAffordabilityLevel.Unaffordable)]
    public void Assess_SubtractsPriceAndFees_AndComparesWithReserve(
        int cash, int price, int expectedRemaining, PurchaseAffordabilityLevel expectedLevel)
    {
        var budget = new PurchaseBudget(cash, FeesOwed: 2_000, OperatingReserve: 10_000);

        var affordability = budget.Assess(price);

        Assert.Equal(expectedRemaining, affordability.Remaining);
        Assert.Equal(expectedLevel, affordability.Level);
    }
}
