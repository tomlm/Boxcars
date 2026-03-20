using System.Text.Json;

namespace Boxcars.Data;

public sealed record SeatControllerState
{
    public string GameId { get; init; } = string.Empty;
    public string PlayerUserId { get; init; } = string.Empty;
    public string ControllerMode { get; init; } = SeatControllerModes.Self;
    public string? DelegatedControllerUserId { get; init; }
    public string? OwningHumanUserId { get; init; }
    public bool IsConnected { get; init; }
    public string? BotDefinitionId { get; init; }
}

public static class SeatControllerModes
{
    public const string Self = "Self";
    public const string Delegated = "Delegated";
    public const string AI = "AI";

    public static bool IsAiControlled(string? controllerMode)
    {
        return string.Equals(controllerMode, AI, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsDelegated(string? controllerMode)
    {
        return string.Equals(controllerMode, Delegated, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSelf(string? controllerMode)
    {
        return string.Equals(controllerMode, Self, StringComparison.OrdinalIgnoreCase);
    }

    public static string Normalize(string? controllerMode)
    {
        if (string.IsNullOrWhiteSpace(controllerMode))
        {
            return string.Empty;
        }

        if (IsAiControlled(controllerMode))
        {
            return AI;
        }

        if (IsDelegated(controllerMode))
        {
            return Delegated;
        }

        if (IsSelf(controllerMode))
        {
            return Self;
        }

        return controllerMode;
    }
}

public static class BotControlStatuses
{
    public const string Active = "Active";
    public const string Cleared = "Cleared";
    public const string MissingDefinition = "MissingDefinition";
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
    public bool IsBotPlayer { get; init; }
    public string DecisionSource { get; init; } = string.Empty;
    public string? FallbackReason { get; init; }
}

public sealed record DisconnectedSeatControlRequest
{
    public int PlayerIndex { get; init; }
    public string ControlMode { get; init; } = DisconnectedSeatControlModes.AI;
}

public static class DisconnectedSeatControlModes
{
    public const string AI = "AI";
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

