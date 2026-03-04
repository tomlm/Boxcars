using Boxcars.Engine.Data.Maps;

namespace Boxcars.Data.Maps;

public sealed class CityRenderItem
{
    public required string Name { get; init; }
    public required string RegionCode { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
}

public sealed class RailroadRenderSegment
{
    public required int RailroadIndex { get; init; }
    public required int StartRegionIndex { get; init; }
    public required int StartDotIndex { get; init; }
    public required int EndRegionIndex { get; init; }
    public required int EndDotIndex { get; init; }
    public required double X1 { get; init; }
    public required double Y1 { get; init; }
    public required double X2 { get; init; }
    public required double Y2 { get; init; }
    public required string StrokeColor { get; init; }
    public bool IsOwned { get; init; }
}

public sealed class RouteNodeRenderItem
{
    public required string NodeId { get; init; }
    public required string Name { get; init; }
    public required int RegionIndex { get; init; }
    public required int DotIndex { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
}

public sealed class RoutePathRenderSegment
{
    public required string FromNodeId { get; init; }
    public required string ToNodeId { get; init; }
    public required int RailroadIndex { get; init; }
    public required double X1 { get; init; }
    public required double Y1 { get; init; }
    public required double X2 { get; init; }
    public required double Y2 { get; init; }
}

public sealed class RegionLabelRenderItem
{
    public required string Text { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
}

public sealed class RegionFillRenderItem
{
    public required int RegionIndex { get; init; }
    public required string RegionCode { get; init; }
    public required string PathData { get; init; }
    public required string FillColor { get; init; }
}

public sealed class BoardRenderModel
{
    public required IReadOnlyList<RegionFillRenderItem> RegionFills { get; init; }
    public required IReadOnlyList<CityRenderItem> Cities { get; init; }
    public required IReadOnlyList<TrainDot> TrainDots { get; init; }
    public required IReadOnlyList<RailroadRenderSegment> RailroadSegments { get; init; }
    public required IReadOnlyList<RouteNodeRenderItem> RouteNodes { get; init; }
    public required IReadOnlyList<RegionLabelRenderItem> RegionLabels { get; init; }
    public required IReadOnlyList<LineSegment> MapLines { get; init; }
    public required IReadOnlyList<LineSegment> Separators { get; init; }
}

public sealed class BoardProjectionResult
{
    public required BoardRenderModel Model { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}
