namespace Boxcars.Data;

public sealed class AdvisorContextSnapshot
{
    public string GameId { get; init; } = string.Empty;
    public int TurnNumber { get; init; }
    public string TurnPhase { get; init; } = string.Empty;
    public int ActivePlayerIndex { get; init; } = -1;
    public int? ControlledPlayerIndex { get; init; }
    public string ControlledPlayerName { get; init; } = string.Empty;
    public string ControlledPlayerSummary { get; init; } = string.Empty;
    public IReadOnlyList<string> OtherPlayerSummaries { get; init; } = [];
    public string BoardSituationSummary { get; init; } = string.Empty;
    public string SeedContextContent { get; init; } = string.Empty;
    public string AuthoritativePayloadJson { get; init; } = string.Empty;
    public IReadOnlyList<AdvisorMessage> RecentConversation { get; init; } = [];
}