using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Persistence;
using global::Boxcars.Services;

namespace Boxcars.Engine.Tests.Unit;

public class RailroadIncomeTests
{
    private const int Alice = 0, Bob = 1;
    private const int West = 1, East = 2, South = 3, Spare = 4;

    [Fact]
    public void Build_ShowsEachRailroadsFeesUnderItsOwner()
    {
        var state = new GameState
        {
            Players = [new PlayerState { Name = "Alice" }, new PlayerState { Name = "Bob" }],
            RailroadOwnership = new Dictionary<int, int?> { [West] = Alice, [East] = Alice, [South] = Bob, [Spare] = null },
            Statistics = new GameStatisticsState { RailroadFeesCollected = new() { [East] = 5_500, [West] = 1_500, [South] = 1_000 } }
        };

        var income = RailroadIncome.Build(state, CreateMap());

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
        Assert.Equal(0.5m, east.Multiplier);
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
