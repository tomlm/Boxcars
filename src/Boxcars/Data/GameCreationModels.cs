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

public enum EventTimelineKind
{
    Other,
    NewDestination,
    DiceRoll,
    Move,
    Arrival,
    PayFees,
    PurchaseOpportunity,
    Purchase,
    DeclinedPurchase
}

public sealed record EventTimelineItem
{
    public string EventId { get; init; } = string.Empty;
    public EventTimelineKind EventKind { get; init; }
    public string Description { get; init; } = string.Empty;
    public DateTimeOffset OccurredUtc { get; init; }
    public int? ActingPlayerIndex { get; init; }
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
