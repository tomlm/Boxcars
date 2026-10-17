using Boxcars.Engine.Persistence;
using Boxcars.Engine.Tests.Fixtures;
using global::Boxcars.Services;

namespace Boxcars.Engine.Tests.Unit;

public class NetWorthHistoryTests
{
    [Fact]
    public void Build_CountsCashPlusRailroadPurchasePrices_AtTheEndOfEachRound()
    {
        const int price = 10_000;
        var map = new global::Boxcars.Engine.Data.Maps.MapDefinition { Name = "Net Worth Test", Version = "1.0" };
        var railroad = new global::Boxcars.Engine.Data.Maps.RailroadDefinition { Index = 3, Name = "Test Line", ShortName = "TL", PurchasePrice = price };
        map.Railroads.Add(railroad);

        var history = NetWorthHistory.Build(
        [
            // Round 1: turns 1 (Alice) and 2 (Bob). Only the last state in the round counts.
            CreateState(turnNumber: 1, aliceCash: 20_000, bobCash: 20_000),
            CreateState(turnNumber: 2, aliceCash: 25_000, bobCash: 18_000),
            // Round 2: Alice has bought the railroad.
            CreateState(turnNumber: 3, aliceCash: 25_000 - price, bobCash: 18_000, aliceRailroads: [railroad.Index]),
            CreateState(turnNumber: 4, aliceCash: 30_000 - price, bobCash: 19_000, aliceRailroads: [railroad.Index])
        ], map);

        Assert.Equal([1, 2], history.RoundNumbers);
        var alice = history.Series.Single(series => series.PlayerName == "Alice");
        var bob = history.Series.Single(series => series.PlayerName == "Bob");
        Assert.Equal([25_000, 30_000], alice.Values);
        Assert.Equal([18_000, 19_000], bob.Values);
        Assert.Equal(0, alice.PlayerIndex);
        Assert.Equal(1, bob.PlayerIndex);
    }

    [Fact]
    public void Build_NoStates_IsEmpty()
    {
        var history = NetWorthHistory.Build([], GameEngineFixture.CreateTestMap());

        Assert.Empty(history.RoundNumbers);
        Assert.Empty(history.Series);
    }

    private static GameState CreateState(int turnNumber, int aliceCash, int bobCash, List<int>? aliceRailroads = null)
    {
        return new GameState
        {
            TurnNumber = turnNumber,
            Players =
            [
                new PlayerState { Name = "Alice", Cash = aliceCash, OwnedRailroadIndices = aliceRailroads ?? [] },
                new PlayerState { Name = "Bob", Cash = bobCash }
            ]
        };
    }
}
