using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Persistence;
using global::Boxcars.Services;

namespace Boxcars.Engine.Tests.Unit;

public class RailroadIncomeTests
{
    private const int Alice = 0, Bob = 1;
    private const int West = 1, East = 2, South = 3, Spare = 4;

    [Fact]
    public void Build_CreditsEachFeePaymentToTheRailroadsRiddenAtTheRateThatSetIt()
    {
        var income = RailroadIncome.Build(
        [
            CreateState(activePlayer: Bob, aliceFees: 0, bobFees: 0, ridden: []),
            // Bob rides West, then re-enters East at the full rate: the $5,000 is East's.
            CreateState(activePlayer: Bob, aliceFees: 5_000, bobFees: 0, ridden: [West, East, East], fullRate: [East]),
            // The same total in the next saved state is not a new payment.
            CreateState(activePlayer: Bob, aliceFees: 5_000, bobFees: 0, ridden: [West, East, East], fullRate: [East]),
            // Bob is grandfathered on West at $1,000.
            CreateState(activePlayer: Bob, aliceFees: 6_000, bobFees: 0, ridden: [West], grandfatheredWestFee: 1_000),
            // Alice pays Bob the $1,000 private fee for South.
            CreateState(activePlayer: Alice, aliceFees: 6_000, bobFees: 1_000, ridden: [South]),
            // Both of Alice's railroads at the same rate split the fee.
            CreateState(activePlayer: Bob, aliceFees: 7_000, bobFees: 1_000, ridden: [West, East])
        ], CreateMap(), GameSettings.Default);

        Assert.Equal(
            [
                (East, (int?)Alice, 5_500),
                (West, Alice, 1_500),
                (South, Bob, 1_000),
                (Spare, null, 0)
            ],
            income.Rows.Select(row => (row.RailroadIndex, row.OwnerIndex, row.FeesCollected)).ToArray());
        // East cost $11,000 and has collected $5,500.
        var east = income.Rows.Single(row => row.RailroadIndex == East);
        Assert.Equal(11_000, east.PurchasePrice);
        Assert.Equal(50m, east.ReturnPercentage);
    }

    private static GameState CreateState(int activePlayer, int aliceFees, int bobFees, int[] ridden, int[]? fullRate = null, int? grandfatheredWestFee = null)
    {
        var bob = new PlayerState { Name = "Bob", TotalFeesCollected = bobFees };
        if (grandfatheredWestFee is int fee)
        {
            bob.GrandfatheredRailroadIndices = [West];
            bob.GrandfatheredRailroadFees = [new GrandfatheredRailroadFeeState { RailroadIndex = West, FeeAmount = fee }];
        }

        return new GameState
        {
            ActivePlayerIndex = activePlayer,
            Players = [new PlayerState { Name = "Alice", TotalFeesCollected = aliceFees }, bob],
            RailroadOwnership = new Dictionary<int, int?> { [West] = Alice, [East] = Alice, [South] = Bob, [Spare] = null },
            Turn = new TurnState { RailroadsRiddenThisTurn = [.. ridden], RailroadsRequiringFullOwnerRateThisTurn = [.. fullRate ?? []] }
        };
    }

    private static MapDefinition CreateMap()
    {
        var map = new MapDefinition { Name = "Income Test", Version = "1.0" };
        foreach (var (index, name) in new[] { (West, "West Line"), (East, "East Line"), (South, "South Line"), (Spare, "Spare Line") })
        {
            map.Railroads.Add(new RailroadDefinition { Index = index, Name = name, ShortName = name[..2], PurchasePrice = 11_000 });
        }

        return map;
    }
}
