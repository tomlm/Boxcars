using Boxcars.Engine.Persistence;
using global::Boxcars.Services;

namespace Boxcars.Engine.Tests.Unit;

public class NetWorthHistoryTests
{
    [Fact]
    public void Build_ShowsEachPlayersNetWorthByRound()
    {
        var state = new GameState
        {
            Players = [new PlayerState { Name = "Alice" }, new PlayerState { Name = "Bob" }],
            Statistics = new GameStatisticsState
            {
                NetWorth =
                [
                    new NetWorthRoundState { Round = 2, Values = [30_000, 19_000] },
                    new NetWorthRoundState { Round = 1, Values = [25_000, 18_000] }
                ]
            }
        };

        var history = NetWorthHistory.Build(state);

        Assert.Equal([1, 2], history.RoundNumbers);
        var alice = history.Series.Single(series => series.PlayerName == "Alice");
        var bob = history.Series.Single(series => series.PlayerName == "Bob");
        Assert.Equal([25_000, 30_000], alice.Values);
        Assert.Equal([18_000, 19_000], bob.Values);
        Assert.Equal(0, alice.PlayerIndex);
        Assert.Equal(1, bob.PlayerIndex);
    }

    [Fact]
    public void Build_GameSavedWithoutStatistics_IsEmpty()
    {
        var history = NetWorthHistory.Build(new GameState { Players = [new PlayerState { Name = "Alice" }] });

        Assert.Empty(history.RoundNumbers);
        Assert.Empty(history.Series);
    }
}
