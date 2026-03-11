namespace Boxcars.Data.Maps;

public enum PlayerMovementType
{
    TwoDie = 2,
    ThreeDie = 3
}

public enum RailroadOwnershipCategory
{
    Unowned,
    OwnedByPlayer,
    OwnedByOtherPlayer
}

public sealed class RouteSuggestionRequest
{
    public required string PlayerId { get; init; }
    public required string StartNodeId { get; init; }
    public required string DestinationNodeId { get; init; }
    public required PlayerMovementType MovementType { get; init; }
    public int MovementCapacity { get; init; }
    public IReadOnlyList<string> TraveledSegmentKeys { get; init; } = [];
    public required string PlayerColor { get; init; }
    public required Func<int, RailroadOwnershipCategory> ResolveRailroadOwnership { get; init; }
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
    public bool IsOwned { get; init; }
    public bool IsThisTurn { get; init; }

    /// <summary>Main stroke: railroad color when unowned, player color when owned.</summary>
    public string StrokeColor => IsOwned ? PlayerColor : RailroadColor;

    /// <summary>Border stroke: player color when unowned, contrasting color when owned.</summary>
    public string BorderColor => IsOwned ? GetContrastColor(PlayerColor) : PlayerColor;

    private static string GetContrastColor(string hexColor)
    {
        if (string.IsNullOrWhiteSpace(hexColor) || hexColor.Length < 7 || hexColor[0] != '#')
        {
            return "#FFFFFF";
        }

        var r = Convert.ToInt32(hexColor.Substring(1, 2), 16);
        var g = Convert.ToInt32(hexColor.Substring(3, 2), 16);
        var b = Convert.ToInt32(hexColor.Substring(5, 2), 16);
        var luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;
        return luminance > 0.5 ? "#000000" : "#FFFFFF";
    }
}
