using System.Text.Json;

namespace Boxcars.Data;

public sealed record GamePlayerSelection
{
    public string UserId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Color { get; init; } = string.Empty;
}

public sealed record CreateGameRequest
{
    public string CreatorUserId { get; init; } = string.Empty;
    public string MapFileName { get; init; } = "U21MAP.RB3";
    public IReadOnlyList<GamePlayerSelection> Players { get; init; } = [];

    public int MaxPlayers => Players.Count;
}

public sealed record EventTimelineItem
{
    public string EventId { get; init; } = string.Empty;
    public string EventKind { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public DateTimeOffset OccurredUtc { get; init; }
}

public static class GamePlayerSelectionSerialization
{
    public static string Serialize(IReadOnlyList<GamePlayerSelection> players)
    {
        return JsonSerializer.Serialize(players);
    }

    public static IReadOnlyList<GamePlayerSelection> Deserialize(string payload)
    {
        return JsonSerializer.Deserialize<List<GamePlayerSelection>>(payload) ?? [];
    }
}
