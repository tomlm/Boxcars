using System.Text.Json;

namespace Boxcars.Data;

public sealed record BotAssignment
{
    public string GameId { get; init; } = string.Empty;
    public string PlayerUserId { get; init; } = string.Empty;
    public string ControllerUserId { get; init; } = string.Empty;
    public string BotDefinitionId { get; init; } = string.Empty;
    public DateTimeOffset AssignedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ClearedUtc { get; init; }
    public string Status { get; init; } = BotAssignmentStatuses.Active;
    public string? ClearReason { get; init; }
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
    public string DecisionSource { get; init; } = string.Empty;
    public string? FallbackReason { get; init; }
}

public sealed record BotAssignmentDialogResult
{
    public string? BotDefinitionId { get; init; }
    public bool ClearRequested { get; init; }
}

public static class BotAssignmentSerialization
{
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