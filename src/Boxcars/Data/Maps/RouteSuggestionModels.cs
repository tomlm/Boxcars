namespace Boxcars.Data.Maps;

public enum PlayerMovementType
{
    TwoDie = 2,
    ThreeDie = 3
}

public enum RailroadOwnershipCategory
{
    Public,
    Friendly,
    Unfriendly
}

public sealed class RouteSuggestionRequest
{
    public required string PlayerId { get; init; }
    public required string StartNodeId { get; init; }
    public required string DestinationNodeId { get; init; }
    public required PlayerMovementType MovementType { get; init; }
    public int MovementCapacity { get; init; }
    public double AverageFutureMovement { get; init; }
    public IReadOnlyList<string> TraveledSegmentKeys { get; init; } = [];
    public required string PlayerColor { get; init; }
    public required Func<int, RailroadOwnershipCategory> ResolveRailroadOwnership { get; init; }
    public Func<int, int>? ResolveRailroadFee { get; init; }
    public Func<int, int?>? ResolveRailroadOwnerPlayerIndex { get; init; }
    public Func<int, int>? ResolvePlayerCash { get; init; }
    public Func<int, double>? ResolvePlayerAccessibleDestinationPercent { get; init; }
    public Func<int, double>? ResolvePlayerMonopolyDestinationPercent { get; init; }
    public int MaximumExploredStates { get; init; }
    public int MaximumSearchMilliseconds { get; init; }
    public bool BonusOutAvailable { get; init; }
    public int CurrentWhiteDiceMovement { get; init; }
    public int CurrentFixedBonusMovement { get; init; }
    public bool BonusOutRequiresWhiteDiceArrival { get; init; }
    public IReadOnlyList<string> DeclaredPlayerRouteNodeIds { get; init; } = [];
    public string? DeclaredPlayerCurrentNodeId { get; init; }
}

public sealed class RouteSuggestionOutlook
{
    public int ArrivalCost { get; init; }
    public int ExitCost { get; init; }
    public int CombinedCost { get; init; }
    public int WorstCaseExitCost { get; init; }
    public int WorstCaseCombinedCost { get; init; }
    public double ExpectedExitCost { get; init; }
    public double ExpectedCombinedCost { get; init; }
    public double BonusOutProbability { get; init; }
}

public enum RouteSuggestionStatus
{
    Success,
    NoRoute,
    Error
}

public sealed class RouteSuggestionSegment
{
    public required string FromNodeId { get; init; }
    public required string ToNodeId { get; init; }
    public required int RailroadIndex { get; init; }
    public required RailroadOwnershipCategory OwnershipCategory { get; init; }
    public required int Turns { get; init; }
    public required int CostPerTurn { get; init; }
    public required int TotalCost { get; init; }
}

public sealed class RouteSuggestionResult
{
    public required RouteSuggestionStatus Status { get; init; }
    public string Message { get; init; } = string.Empty;
    public string StartNodeId { get; init; } = string.Empty;
    public string DestinationNodeId { get; init; } = string.Empty;
    public List<string> NodeIds { get; init; } = [];
    public List<RouteSuggestionSegment> Segments { get; init; } = [];
    public int TotalTurns { get; init; }
    public int TotalCost { get; init; }
    public RouteSuggestionOutlook Outlook { get; init; } = new();
}

public enum RouteSuggestionHighlightType
{
    Solid,
    Endpoint,
    Dashed
}

public sealed class RouteSuggestionHighlight
{
    public required string NodeId { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
    public required string Color { get; init; }
    public required double Radius { get; init; }
    public RouteSuggestionHighlightType HighlightType { get; init; }
}

public sealed class RouteSuggestionSegmentOverlay
{
    public required double X1 { get; init; }
    public required double Y1 { get; init; }
    public required double X2 { get; init; }
    public required double Y2 { get; init; }
    public required string PlayerColor { get; init; }
    public required string RailroadColor { get; init; }
    public string OwnerColor { get; init; } = string.Empty;
    public RailroadOwnershipCategory OwnershipCategory { get; init; }
    public bool IsThisTurn { get; init; }

    /// <summary>
    /// Fill (top line): railroad color when public, player color when player-owned,
    /// owner color when owned by another player.
    /// </summary>
    public string StrokeColor => OwnershipCategory switch
    {
        RailroadOwnershipCategory.Friendly => PlayerColor,
        RailroadOwnershipCategory.Unfriendly =>
            !string.IsNullOrWhiteSpace(OwnerColor) ? OwnerColor : RailroadColor,
        _ => RailroadColor
    };

    /// <summary>
    /// Border (bottom line): white when suggested; for selected segments:
    /// player color when public, contrast of player when friendly,
    /// player color when unfriendly.
    /// </summary>
    public string BorderColor => IsThisTurn
        ? OwnershipCategory switch
        {
            RailroadOwnershipCategory.Friendly => ColorUtilities.GetContrastColor(PlayerColor),
            RailroadOwnershipCategory.Unfriendly => PlayerColor,
            _ => PlayerColor
        }
        : "#FFFFFF";
}
