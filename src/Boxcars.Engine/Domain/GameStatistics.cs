namespace Boxcars.Engine.Domain;

/// <summary>
/// Running totals for the Stats tab, kept by the engine as the game is played and saved with the game, so showing
/// them never reads the game's history.
/// </summary>
public sealed class GameStatistics
{
    /// <summary>How often each city has been drawn as a destination or a declared player's alternate.</summary>
    public Dictionary<string, int> DestinationDraws { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Fees owners have collected for each railroad, whoever owned it at the time.</summary>
    public Dictionary<int, int> RailroadFeesCollected { get; } = new();

    /// <summary>Each player's net worth, by player index, at the end of each round so far (the current one as it stands).</summary>
    public SortedDictionary<int, IReadOnlyList<int>> NetWorthByRound { get; } = new();
}
