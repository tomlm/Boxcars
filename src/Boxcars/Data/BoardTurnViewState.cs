namespace Boxcars.Data;

public sealed class BoardTurnViewState
{
    public int ActivePlayerIndex { get; init; } = -1;
    public int CurrentUserPlayerIndex { get; init; } = -1;
    public string ActivePlayerName { get; init; } = string.Empty;
    public string TurnPhase { get; init; } = string.Empty;
    public int WhiteDieOne { get; init; }
    public int WhiteDieTwo { get; init; }
    public int? RedDie { get; init; }
    public bool BonusRollAvailable { get; init; }
    public int MovementAllowance { get; init; }
    public int MovementRemaining { get; init; }
    public int PreviewFee { get; init; }
    public bool PreviewHasUnfriendlyFee { get; init; }
    public int CurrentRollTotal { get; init; }
    public bool IsActivePlayerAtDestination { get; init; }
    public string ActivePlayerDestinationCity { get; init; } = string.Empty;
    public string ActivePlayerControllerMode { get; init; } = SeatControllerModes.Self;
    public TurnMovementPreview SelectedRoutePreview { get; init; } = TurnMovementPreview.Empty;
    public IReadOnlyList<string> TraveledSegmentKeys { get; init; } = [];
    public bool IsCurrentUserActivePlayer { get; init; }
    public bool CanEndTurn { get; init; }
    public ArrivalResolutionModel? ArrivalResolution { get; init; }
    public PurchasePhaseModel? PurchasePhase { get; init; }
    public ForcedSalePhaseModel? ForcedSalePhase { get; init; }
    public HomeCityChoicePhaseModel? HomeCityChoicePhase { get; init; }
    public HomeSwapPhaseModel? HomeSwapPhase { get; init; }
    public RegionChoicePhaseModel? RegionChoicePhase { get; init; }
}