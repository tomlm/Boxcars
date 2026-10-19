using Boxcars.Services;

namespace Boxcars.Engine.Tests.Unit;

public class AuctionValuationTests
{
    private const int ListPrice = 20_000;
    private const int PrivateRailroads = 28;
    private const int LotsOfCash = 500_000;

    [Fact]
    public void Evaluate_UselessRailroadWhilePlentyAreUnsold_IsWorthTheOpeningPrice()
    {
        var valuation = AuctionValuation.Evaluate(ListPrice, 0m, 0m, PrivateRailroads - 1, PrivateRailroads, LotsOfCash, 10_000);

        Assert.Equal(ListPrice / 2, valuation.MaximumBid);
        Assert.Equal(1m, valuation.ScarcityPremium);
    }

    [Fact]
    public void Evaluate_UsefulRailroadWhilePlentyAreUnsold_IsWorthTheListPrice()
    {
        var valuation = AuctionValuation.Evaluate(ListPrice, 10m, 0m, PrivateRailroads - 1, PrivateRailroads, LotsOfCash, 10_000);

        Assert.Equal(ListPrice, valuation.MaximumBid);
    }

    [Fact]
    public void Evaluate_WhenEveryOtherRailroadIsSold_PaysTheSoldOutPremium()
    {
        var useless = AuctionValuation.Evaluate(ListPrice, 0m, 0m, 0, PrivateRailroads, LotsOfCash, 20_000);
        var useful = AuctionValuation.Evaluate(ListPrice, 10m, 0m, 0, PrivateRailroads, LotsOfCash, 20_000);

        // Two to four times the list price.
        Assert.Equal(ListPrice * 2, useless.MaximumBid);
        Assert.Equal(ListPrice * 4, useful.MaximumBid);
        Assert.Equal(AuctionValuation.SoldOutPremium, useful.ScarcityPremium);
        Assert.Contains("no other railroads are left", useful.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_PremiumStaysSmallUntilMostRailroadsAreGone()
    {
        var halfSold = AuctionValuation.Evaluate(ListPrice, 10m, 0m, (PrivateRailroads - 1) / 2, PrivateRailroads, LotsOfCash, 10_000);
        var nearlySold = AuctionValuation.Evaluate(ListPrice, 10m, 0m, 2, PrivateRailroads, LotsOfCash, 10_000);

        Assert.InRange(halfSold.ScarcityPremium, 1.7m, 1.9m);
        Assert.InRange(nearlySold.ScarcityPremium, 3.5m, 4m);
        Assert.True(nearlySold.MaximumBid > halfSold.MaximumBid);
    }

    [Fact]
    public void Evaluate_NeverSpendsTheOperatingReserve()
    {
        var valuation = AuctionValuation.Evaluate(ListPrice, 10m, 0m, 0, PrivateRailroads, cash: 45_250, operatingReserve: 20_000);

        // $25,250 spare, rounded down to the $500 bid increment.
        Assert.Equal(25_000, valuation.MaximumBid);
        Assert.Equal(ListPrice * 4, valuation.Value);
        Assert.Contains("operating reserve", valuation.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_MonopolyGainCountsHalfAsMuchAsAccess()
    {
        var access = AuctionValuation.Evaluate(ListPrice, 4m, 0m, PrivateRailroads - 1, PrivateRailroads, LotsOfCash, 10_000);
        var monopoly = AuctionValuation.Evaluate(ListPrice, 0m, 8m, PrivateRailroads - 1, PrivateRailroads, LotsOfCash, 10_000);

        Assert.Equal(0.5m, access.Usefulness);
        Assert.Equal(access.MaximumBid, monopoly.MaximumBid);
    }

    [Fact]
    public void Evaluate_KeepsWhatTheBidderIsSavingFor()
    {
        var valuation = AuctionValuation.Evaluate(
            ListPrice,
            10m,
            0m,
            0,
            PrivateRailroads,
            cash: 100_000,
            operatingReserve: 20_000,
            [new AuctionCashCommitment("for a Superchief", 40_000)]);

        Assert.Equal(40_000, valuation.MaximumBid);
        Assert.Contains("$40,000 for a Superchief", valuation.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_WhenSavingsTakeEverything_BidsNothing()
    {
        var valuation = AuctionValuation.Evaluate(
            ListPrice,
            10m,
            0m,
            0,
            PrivateRailroads,
            cash: 310_000,
            operatingReserve: 20_000,
            [new AuctionCashCommitment("to win the game", 300_000)]);

        Assert.Equal(0, valuation.MaximumBid);
    }
}
