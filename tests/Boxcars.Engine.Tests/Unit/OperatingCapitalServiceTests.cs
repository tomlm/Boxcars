using Boxcars.Engine.Persistence;
using global::Boxcars.Services;

namespace Boxcars.Engine.Tests.Unit;

public class OperatingCapitalServiceTests
{
    private const int Rider = 0, Owner = 1;
    private const int Established = 1, PreSale = 2, FullRate = 3;

    [Fact]
    public void CalculateFeesOwed_EstablishedRate_IsOwedAtThatRate()
    {
        // Issue #148: established on the owner's line at $5,000, not the $1,000 private fee.
        var rider = new PlayerState
        {
            GrandfatheredRailroadIndices = [Established],
            GrandfatheredRailroadFees = [new GrandfatheredRailroadFeeState { RailroadIndex = Established, FeeAmount = 5_000 }]
        };
        var state = CreateState(rider, new TurnState { RailroadsRiddenThisTurn = [Established] });

        Assert.Equal(5_000, OperatingCapitalService.CalculateFeesOwed(state, Rider, GameSettings.Default));
    }

    [Fact]
    public void CalculateFeesOwed_RiddenBeforeTheLastRailroadSold_IsOwedAtThePreSaleRate()
    {
        // Issue #149: ridden before the sale at $5,000; the owner's other line ridden after it costs $10,000.
        var preSaleOnly = CreateState(new PlayerState(), new TurnState
        {
            RailroadsRiddenThisTurn = [PreSale],
            ReducedRailroadFeesThisTurn = [new GrandfatheredRailroadFeeState { RailroadIndex = PreSale, FeeAmount = 5_000 }]
        });
        var thenFullRate = CreateState(new PlayerState(), new TurnState
        {
            RailroadsRiddenThisTurn = [PreSale, FullRate],
            RailroadsRequiringFullOwnerRateThisTurn = [FullRate],
            ReducedRailroadFeesThisTurn = [new GrandfatheredRailroadFeeState { RailroadIndex = PreSale, FeeAmount = 5_000 }]
        });

        Assert.Equal(5_000, OperatingCapitalService.CalculateFeesOwed(preSaleOnly, Rider, GameSettings.Default));
        Assert.Equal(10_000, OperatingCapitalService.CalculateFeesOwed(thenFullRate, Rider, GameSettings.Default));
    }

    private static GameState CreateState(PlayerState rider, TurnState turn)
    {
        return new GameState
        {
            ActivePlayerIndex = Rider,
            AllRailroadsSold = true,
            Players = [rider, new PlayerState()],
            RailroadOwnership = new Dictionary<int, int?> { [Established] = Owner, [PreSale] = Owner, [FullRate] = Owner },
            Turn = turn
        };
    }
}
