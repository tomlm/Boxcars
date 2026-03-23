using System.Text;
using System.Text.Json;
using Boxcars.Engine.Persistence;
using Microsoft.AspNetCore.WebUtilities;

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
    public GameSettings Settings { get; init; } = GameSettings.Default;

    public int MaxPlayers => Players.Count;
}

public sealed record ReplayGamePreset
{
    public string MapFileName { get; init; } = "U21MAP.RB3";
    public IReadOnlyList<GamePlayerSelection> Players { get; init; } = [];
    public GameSettings Settings { get; init; } = GameSettings.Default;
}

public enum EventTimelineKind
{
    Other,
    NewDestination,
    CashAnnouncement,
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
    public string ActingUserId { get; init; } = string.Empty;
    public bool IsAiAction { get; init; }
    public bool IsBotPlayer { get; init; }
    public string BotDefinitionId { get; init; } = string.Empty;
    public string BotName { get; init; } = string.Empty;
    public string BotControllerMode { get; init; } = string.Empty;
    public string BotDecisionSource { get; init; } = string.Empty;
    public string BotFallbackReason { get; init; } = string.Empty;
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

public static class ReplayGamePresetSerialization
{
    public static string Serialize(ReplayGamePreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        var json = JsonSerializer.Serialize(preset);
        return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(json));
    }

    public static ReplayGamePreset? Deserialize(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            var json = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(payload));
            return JsonSerializer.Deserialize<ReplayGamePreset>(json);
        }
        catch
        {
            return null;
        }
    }
}
