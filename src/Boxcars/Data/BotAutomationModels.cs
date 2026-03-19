using System.Text.Json;

namespace Boxcars.Data;

public sealed record BotAssignment
{
    public string GameId { get; init; } = string.Empty;
    public string PlayerUserId { get; init; } = string.Empty;
    public string ControllerUserId { get; init; } = string.Empty;
    public string ControllerMode { get; init; } = string.Empty;
    public string BotDefinitionId { get; init; } = string.Empty;
    public int? AuctionPlanTurnNumber { get; init; }
    public int? AuctionPlanRailroadIndex { get; init; }
    public int? AuctionPlanStartingPrice { get; init; }
    public int? AuctionPlanMaximumBid { get; init; }
    public DateTimeOffset AssignedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ClearedUtc { get; init; }
    public string Status { get; init; } = BotAssignmentStatuses.Active;
    public string? ClearReason { get; init; }
}

public sealed record SeatControllerState
{
    public string GameId { get; init; } = string.Empty;
    public string PlayerUserId { get; init; } = string.Empty;
    public string ControllerMode { get; init; } = SeatControllerModes.HumanDirect;
    public string? DelegatedControllerUserId { get; init; }
    public string? OwningHumanUserId { get; init; }
    public bool IsConnected { get; init; }
    public string? BotDefinitionId { get; init; }
}

public static class SeatControllerModes
{
    public const string HumanDirect = "HumanDirect";
    public const string HumanDelegated = "HumanDelegated";
    public const string AiBotSeat = "AiBotSeat";
    public const string AiGhost = "AiGhost";
}

public static class BotAssignmentStatuses
{
    public const string Active = "Active";
    public const string Cleared = "Cleared";
    public const string MissingDefinition = "MissingDefinition";
    public const string DisconnectedController = "DisconnectedController";
}

public sealed record BotLegalOption
{
    public string OptionId { get; init; } = string.Empty;
    public string OptionType { get; init; } = string.Empty;
    public string DisplayText { get; init; } = string.Empty;
    public string Payload { get; init; } = string.Empty;
}

public sealed record BotDecisionContext
{
    public string GameId { get; init; } = string.Empty;
    public string PlayerUserId { get; init; } = string.Empty;
    public string TargetPlayerName { get; init; } = string.Empty;
    public string Phase { get; init; } = string.Empty;
    public int TurnNumber { get; init; }
    public string BotName { get; init; } = string.Empty;
    public string StrategyText { get; init; } = string.Empty;
    public string GameStatePayload { get; init; } = string.Empty;
    public IReadOnlyList<BotLegalOption> LegalOptions { get; init; } = [];
    public DateTimeOffset TimeoutUtc { get; init; }
}

public sealed record BotDecisionResolution
{
    public string GameId { get; init; } = string.Empty;
    public string PlayerUserId { get; init; } = string.Empty;
    public string Phase { get; init; } = string.Empty;
    public string SelectedOptionId { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string? FallbackReason { get; init; }
    public DateTimeOffset ResolvedUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record SellImpactEvaluation
{
    public int RailroadIndex { get; init; }
    public string RailroadId { get; init; } = string.Empty;
    public int AccessDeltaScore { get; init; }
    public int MonopolyDeltaScore { get; init; }
    public string TieBreakerKey { get; init; } = string.Empty;
    public string CompositeRank { get; init; } = string.Empty;
}

public sealed record BotRecordedActionMetadata
{
    public string BotDefinitionId { get; init; } = string.Empty;
    public string BotName { get; init; } = string.Empty;
    public string ControllerMode { get; init; } = string.Empty;
    public string DecisionSource { get; init; } = string.Empty;
    public string? FallbackReason { get; init; }
}

public sealed record BotAssignmentDialogResult
{
    public string? BotDefinitionId { get; init; }
    public bool ClearRequested { get; init; }
}

public sealed record DisconnectedSeatControlRequest
{
    public int PlayerIndex { get; init; }
    public string ControlMode { get; init; } = DisconnectedSeatControlModes.Bot;
}

public static class DisconnectedSeatControlModes
{
    public const string Bot = "Bot";
    public const string Manual = "Manual";
}

public static class SeatControllerStateSerialization
{
    public static string Serialize(IReadOnlyList<SeatControllerState> controllerStates)
    {
        return JsonSerializer.Serialize(controllerStates);
    }

    public static IReadOnlyList<SeatControllerState> Deserialize(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<SeatControllerState>>(payload) ?? [];
    }
}

public static class BotAssignmentSerialization
{
    public const string EmptyPayload = "[]";

    public static string Serialize(IReadOnlyList<BotAssignment> assignments)
    {
        return JsonSerializer.Serialize(assignments);
    }

    public static IReadOnlyList<BotAssignment> Deserialize(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<BotAssignment>>(payload) ?? [];
    }
}