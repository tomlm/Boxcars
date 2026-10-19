using Boxcars.Engine.Data.Maps;

namespace Boxcars.Engine.Domain;

/// <summary>
/// Immutable route from a start node to a destination along railroad segments.
/// </summary>
public sealed class Route
{
    /// <summary>Ordered list of node IDs from start to destination.</summary>
    public IReadOnlyList<string> NodeIds { get; }

    /// <summary>Ordered list of segments connecting nodes.</summary>
    public IReadOnlyList<RouteSegment> Segments { get; }

    /// <summary>Estimated total use-fee cost for this route.</summary>
    public int TotalCost { get; }

    public Route(IReadOnlyList<string> nodeIds, IReadOnlyList<RouteSegment> segments, int totalCost)
    {
        NodeIds = nodeIds;
        Segments = segments;
        TotalCost = totalCost;
    }
}

/// <summary>
/// A single segment of a route between two adjacent train dots.
/// </summary>
public sealed class RouteSegment
{
    public required string FromNodeId { get; init; }
    public required string ToNodeId { get; init; }
    public required int RailroadIndex { get; init; }
}

/// <summary>
/// Normalized key for a track segment, independent of direction.
/// </summary>
public readonly record struct SegmentKey : IComparable<SegmentKey>
{
    public string NodeA { get; }
    public string NodeB { get; }
    public int RailroadIndex { get; }

    public static bool operator <(SegmentKey left, SegmentKey right) => left.CompareTo(right) < 0;
    public static bool operator >(SegmentKey left, SegmentKey right) => left.CompareTo(right) > 0;
    public static bool operator <=(SegmentKey left, SegmentKey right) => left.CompareTo(right) <= 0;
    public static bool operator >=(SegmentKey left, SegmentKey right) => left.CompareTo(right) >= 0;

    public SegmentKey(string from, string to, int railroadIndex)
    {
        // Normalize direction so (A,B,RR) == (B,A,RR)
        if (string.Compare(from, to, StringComparison.OrdinalIgnoreCase) <= 0)
        {
            NodeA = from;
            NodeB = to;
        }
        else
        {
            NodeA = to;
            NodeB = from;
        }

        RailroadIndex = railroadIndex;
    }

    public int CompareTo(SegmentKey other)
    {
        int cmp = string.Compare(NodeA, other.NodeA, StringComparison.OrdinalIgnoreCase);
        if (cmp != 0) return cmp;
        cmp = string.Compare(NodeB, other.NodeB, StringComparison.OrdinalIgnoreCase);
        return cmp != 0 ? cmp : RailroadIndex.CompareTo(other.RailroadIndex);
    }

    public override string ToString() => $"{NodeA}-{NodeB}:{RailroadIndex}";
}

/// <summary>Options for <see cref="GameEngine.SuggestRouteForPlayer(int, RouteSuggestionOptions)"/>.</summary>
public sealed class RouteSuggestionOptions
{
    /// <summary>Plan to this node instead of the player's destination; no rovering or laying up.</summary>
    public string? DestinationNodeId { get; init; }

    /// <summary>Declared players the route shouldn't chase, e.g. other seats of the same person.</summary>
    public IReadOnlyCollection<int> PlayersNotToRover { get; init; } = [];
}

/// <summary>How the fee rules apply to one segment of a planned route.</summary>
/// <param name="StartsTurn">The segment is the first dot of a turn (this one, or a later average one).</param>
/// <param name="FeePerTurn">What riding its railroad costs the player in a turn.</param>
/// <param name="FeeAdded">The fee riding it adds, given what the turn has already paid for.</param>
public readonly record struct RouteSegmentCost(bool StartsTurn, int FeePerTurn, int FeeAdded);

/// <summary>What a bonus does for a move that reaches a destination only opponents serve.</summary>
public enum ArrivalBonus
{
    /// <summary>No bonus to count on, or the move doesn't arrive somewhere the player could be stuck.</summary>
    None,

    /// <summary>A bonus die is still to roll; it may get off the opponent's railroad.</summary>
    Possible,

    /// <summary>This turn's known bonus is enough to get off the opponent's railroad.</summary>
    GetsOff
}
