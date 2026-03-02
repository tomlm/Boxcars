namespace Boxcars.Data.Maps;

public sealed class TrainDot
{
    public required string Id { get; init; }
    public required int RegionIndex { get; init; }
    public required int DotIndex { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
    public int? ColorIndex { get; init; }
}

public sealed class LineSegment
{
    public required double X1 { get; init; }
    public required double Y1 { get; init; }
    public required double X2 { get; init; }
    public required double Y2 { get; init; }
    public int? StyleIndex { get; init; }
}

public sealed class CityRenderItem
{
    public required string Name { get; init; }
    public required string RegionCode { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
}

public sealed class RailroadRenderSegment
{
    public required double X1 { get; init; }
    public required double Y1 { get; init; }
    public required double X2 { get; init; }
    public required double Y2 { get; init; }
    public required string StrokeColor { get; init; }
    public bool IsOwned { get; init; }
}

public sealed class RegionLabelRenderItem
{
    public required string Text { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
}

public sealed class BoardRenderModel
{
    public required IReadOnlyList<CityRenderItem> Cities { get; init; }
    public required IReadOnlyList<TrainDot> TrainDots { get; init; }
    public required IReadOnlyList<RailroadRenderSegment> RailroadSegments { get; init; }
    public required IReadOnlyList<RegionLabelRenderItem> RegionLabels { get; init; }
    public required IReadOnlyList<LineSegment> MapLines { get; init; }
    public required IReadOnlyList<LineSegment> Separators { get; init; }
}

public sealed class BoardProjectionResult
{
    public required BoardRenderModel Model { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}
