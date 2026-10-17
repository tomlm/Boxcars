using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Persistence;

namespace Boxcars.Services;

/// <summary>Each player's net worth (cash plus the purchase price of their railroads) at the end of every round.</summary>
/// <param name="RoundNumbers">The round each point belongs to, oldest first.</param>
public sealed record NetWorthHistory(IReadOnlyList<int> RoundNumbers, IReadOnlyList<NetWorthSeries> Series)
{
    public static NetWorthHistory Empty { get; } = new([], []);

    /// <summary>
    /// Builds the history from saved game states in the order they happened. A round is one turn for each player, and
    /// its point is the last state saved in it.
    /// </summary>
    public static NetWorthHistory Build(IEnumerable<GameState> statesInOrder, MapDefinition mapDefinition)
    {
        ArgumentNullException.ThrowIfNull(statesInOrder);
        ArgumentNullException.ThrowIfNull(mapDefinition);

        var lastStateByRound = new SortedDictionary<int, GameState>();
        foreach (var state in statesInOrder)
        {
            if (state.Players.Count == 0)
            {
                continue;
            }

            var round = (Math.Max(1, state.TurnNumber) - 1) / state.Players.Count + 1;
            lastStateByRound[round] = state;
        }

        if (lastStateByRound.Count == 0)
        {
            return Empty;
        }

        var railroadPrices = mapDefinition.Railroads.ToDictionary(railroad => railroad.Index, railroad => railroad.PurchasePrice ?? 0);
        var latest = lastStateByRound.Values.Last();
        var series = latest.Players
            .Select((player, index) => new NetWorthSeries(
                index,
                player.Name,
                lastStateByRound.Values
                    .Select(state => index < state.Players.Count ? CalculateNetWorth(state.Players[index], railroadPrices) : 0)
                    .ToList()))
            .ToList();

        return new NetWorthHistory(lastStateByRound.Keys.ToList(), series);
    }

    private static int CalculateNetWorth(PlayerState player, Dictionary<int, int> railroadPrices)
    {
        return player.Cash + player.OwnedRailroadIndices.Sum(railroadIndex => railroadPrices.GetValueOrDefault(railroadIndex));
    }
}

public sealed record NetWorthSeries(int PlayerIndex, string PlayerName, IReadOnlyList<int> Values);
