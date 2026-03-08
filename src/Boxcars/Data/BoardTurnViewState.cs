namespace Boxcars.Data;

public sealed class BoardTurnViewState
{
    public int ActivePlayerIndex { get; init; } = -1;
    public int CurrentUserPlayerIndex { get; init; } = -1;
    public string ActivePlayerName { get; init; } = string.Empty;
    public string TurnPhase { get; init; } = string.Empty;
    public int MovementAllowance { get; init; }
    public int MovementRemaining { get; init; }
    public int PreviewFee { get; init; }
    public int CurrentRollTotal { get; init; }
    public TurnMovementPreview SelectedRoutePreview { get; init; } = TurnMovementPreview.Empty;
    public IReadOnlyList<string> TraveledSegmentKeys { get; init; } = [];
    public bool IsCurrentUserActivePlayer { get; init; }
    public bool CanEndTurn { get; init; }
    public ArrivalResolutionModel? ArrivalResolution { get; init; }
}