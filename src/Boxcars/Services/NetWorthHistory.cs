using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Domain;
using Boxcars.Engine.Persistence;

namespace Boxcars.Services;

/// <summary>
/// Each player's net worth (cash plus the purchase price of their railroads and engine) at the end of every round.
/// </summary>
/// <param name="RoundNumbers">The round each point belongs to, oldest first.</param>
public sealed record NetWorthHistory(IReadOnlyList<int> RoundNumbers, IReadOnlyList<NetWorthSeries> Series)
{
    public static NetWorthHistory Empty { get; } = new([], []);

    /// <summary>The history the engine has kept in the game's statistics (see GameStatistics.NetWorthByRound).</summary>
    public static NetWorthHistory Build(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var rounds = state.Statistics?.NetWorth.OrderBy(round => round.Round).ToList() ?? [];
        if (rounds.Count == 0)
        {
            return Empty;
        }

        var series = state.Players
            .Select((player, index) => new NetWorthSeries(
                index,
                player.Name,
                rounds.Select(round => index < round.Values.Count ? round.Values[index] : 0).ToList()))
            .ToList();

        return new NetWorthHistory(rounds.Select(round => round.Round).ToList(), series);
    }
}

public sealed record NetWorthSeries(int PlayerIndex, string PlayerName, IReadOnlyList<int> Values);
