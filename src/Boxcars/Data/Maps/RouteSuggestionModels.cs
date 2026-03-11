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

public sealed class RouteSuggestionHighlight
{
    public required string NodeId { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
    public required string Color { get; init; }
    public required double Radius { get; init; }
    public bool IsDashed { get; init; }
}
