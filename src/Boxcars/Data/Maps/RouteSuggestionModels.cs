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

public enum RouteSuggestionStatus
{
    Success,
    NoRoute,
    Error
}

/// <summary>One dot of a shared route: the track from one node to the next on a railroad.</summary>
public sealed record SharedRouteSegment(string FromNodeId, string ToNodeId, int RailroadIndex);

/// <summary>
/// The route the player on turn is previewing, as others see it: <paramref name="Selected"/> is this turn's move
/// (suggested or hand-picked), <paramref name="Suggested"/> the whole suggested route to the destination.
/// </summary>
public sealed record SharedRoutePreview(
    int PlayerIndex,
    int TurnNumber,
    IReadOnlyList<SharedRouteSegment> Selected,
    IReadOnlyList<SharedRouteSegment> Suggested)
{
    public bool Equals(SharedRoutePreview? other)
    {
        return other is not null
            && PlayerIndex == other.PlayerIndex
            && TurnNumber == other.TurnNumber
            && Selected.SequenceEqual(other.Selected)
            && Suggested.SequenceEqual(other.Suggested);
    }

    public override int GetHashCode() => HashCode.Combine(PlayerIndex, TurnNumber, Selected.Count, Suggested.Count);
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

    /// <summary>The go-in-or-lay-up comparison for this turn's move; null when it wasn't made.</summary>
    public Boxcars.Engine.Domain.DestinationApproachDecision? Approach { get; init; }

    /// <summary>When this turn's move reaches a destination only opponents serve: whether a bonus gets off.</summary>
    public Boxcars.Engine.Domain.ArrivalBonus ArrivalBonus { get; init; }

    /// <summary>The declared opponent this route chases to rover, instead of heading for the destination; null when it doesn't.</summary>
    public string? RoverTargetPlayerName { get; init; }
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
