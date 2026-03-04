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

    public static bool operator <(SegmentKey left, SegmentKey right) => left.CompareTo(right) < 0;
    public static bool operator >(SegmentKey left, SegmentKey right) => left.CompareTo(right) > 0;
    public static bool operator <=(SegmentKey left, SegmentKey right) => left.CompareTo(right) <= 0;
    public static bool operator >=(SegmentKey left, SegmentKey right) => left.CompareTo(right) >= 0;

    public SegmentKey(string from, string to)
    {
        // Normalize direction so (A,B) == (B,A)
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
    }

    public int CompareTo(SegmentKey other)
    {
        int cmp = string.Compare(NodeA, other.NodeA, StringComparison.OrdinalIgnoreCase);
        return cmp != 0 ? cmp : string.Compare(NodeB, other.NodeB, StringComparison.OrdinalIgnoreCase);
    }

    public override string ToString() => $"{NodeA}-{NodeB}";
}
