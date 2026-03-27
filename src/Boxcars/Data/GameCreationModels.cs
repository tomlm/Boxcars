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

public sealed record GameSeatDefinition
{
    public int SeatIndex { get; init; }
    public string PlayerUserId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Color { get; init; } = string.Empty;
}

public sealed record CreateGameRequest
{
    public string CreatorUserId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public DateTimeOffset? GameDate { get; init; }
    public string MapFileName { get; init; } = "U21MAP.RB3";
    public IReadOnlyList<GamePlayerSelection> Players { get; init; } = [];
    public GameSettings Settings { get; init; } = GameSettings.Default;
    public IReadOnlyList<CityProbabilityOverride> CityProbabilityOverrides { get; init; } = [];
    public IReadOnlyList<RailroadPriceOverride> RailroadPriceOverrides { get; init; } = [];

    public int MaxPlayers => Players.Count;
}

public sealed record ReplayGamePreset
{
    public string Name { get; init; } = string.Empty;
    public DateTimeOffset? GameDate { get; init; }
    public string MapFileName { get; init; } = "U21MAP.RB3";
    public IReadOnlyList<GamePlayerSelection> Players { get; init; } = [];
    public GameSettings Settings { get; init; } = GameSettings.Default;
    public IReadOnlyList<CityProbabilityOverride> CityProbabilityOverrides { get; init; } = [];
    public IReadOnlyList<RailroadPriceOverride> RailroadPriceOverrides { get; init; } = [];
}

public sealed record CityProbabilityOverride
{
    public string CityName { get; init; } = string.Empty;
    public string RegionCode { get; init; } = string.Empty;
    public double Probability { get; init; }
}

public sealed record RailroadPriceOverride
{
    public int RailroadIndex { get; init; }
    public int PurchasePrice { get; init; }
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

public static class PersistedGameStates
{
    public const string Lobby = "Lobby";
    public const string Playing = "Playing";
}

public sealed record DashboardGameSummary
{
    public string GameId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? GameDate { get; init; }
    public string State { get; init; } = PersistedGameStates.Lobby;
    public bool IsCreator { get; init; }
    public int PlayerCount { get; init; }
    public int HumanPlayerCount { get; init; }
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

public static class GameSeatDefinitionSerialization
{
    public static string Serialize(IReadOnlyList<GameSeatDefinition> seats)
    {
        return JsonSerializer.Serialize(seats);
    }

    public static IReadOnlyList<GameSeatDefinition> Deserialize(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<GameSeatDefinition>>(payload) ?? [];
    }
}

public static class CityProbabilityOverrideSerialization
{
    public static string Serialize(IReadOnlyList<CityProbabilityOverride> overrides)
    {
        return JsonSerializer.Serialize(overrides);
    }

    public static IReadOnlyList<CityProbabilityOverride> Deserialize(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<CityProbabilityOverride>>(payload) ?? [];
    }
}

public static class RailroadPriceOverrideSerialization
{
    public static string Serialize(IReadOnlyList<RailroadPriceOverride> overrides)
    {
        return JsonSerializer.Serialize(overrides);
    }

    public static IReadOnlyList<RailroadPriceOverride> Deserialize(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<RailroadPriceOverride>>(payload) ?? [];
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

public static class ReplayGameNaming
{
    public static string BuildReplayName(string? gameName)
    {
        var trimmedName = string.IsNullOrWhiteSpace(gameName)
            ? "Game"
            : gameName.Trim();
        var lastSpaceIndex = trimmedName.LastIndexOf(' ');

        if (lastSpaceIndex > 0
            && int.TryParse(trimmedName[(lastSpaceIndex + 1)..], out var replayNumber)
            && replayNumber >= 2)
        {
            return $"{trimmedName[..lastSpaceIndex]} {replayNumber + 1}";
        }

        return $"{trimmedName} 2";
    }
}
