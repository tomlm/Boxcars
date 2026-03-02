namespace Boxcars.Data.Maps;

public sealed class MapDefinition
{
    public string? Name { get; set; }
    public string? Version { get; set; }
    public double ScaleLeft { get; set; }
    public double ScaleTop { get; set; }
    public double ScaleWidth { get; set; }
    public double ScaleHeight { get; set; }
    public string? BackgroundKey { get; set; }
    public List<RegionDefinition> Regions { get; } = new();
    public List<CityDefinition> Cities { get; } = new();
    public List<RailroadDefinition> Railroads { get; } = new();
    public List<TrainDot> TrainDots { get; } = new();
    public List<RailroadRouteSegmentDefinition> RailroadRouteSegments { get; } = new();
    public List<LineSegment> MapLines { get; } = new();
    public List<LineSegment> Separators { get; } = new();
}

public sealed class RegionDefinition
{
    public required string Name { get; init; }
    public required string Code { get; init; }
    public double? Probability { get; init; }
}

public sealed class CityDefinition
{
    public required string Name { get; init; }
    public required string RegionCode { get; init; }
    public double? Probability { get; init; }
    public int? PayoutIndex { get; init; }
    public int? MapDotIndex { get; init; }
}

public sealed class RailroadDefinition
{
    public required int Index { get; init; }
    public required string Name { get; init; }
    public string? ShortName { get; init; }
    public int? ColorIndex { get; init; }
    public int? Red { get; init; }
    public int? Green { get; init; }
    public int? Blue { get; init; }
}

public sealed class RailroadRouteSegmentDefinition
{
    public required int RailroadIndex { get; init; }
    public required int StartRegionIndex { get; init; }
    public required int StartDotIndex { get; init; }
    public required int EndRegionIndex { get; init; }
    public required int EndDotIndex { get; init; }
}
