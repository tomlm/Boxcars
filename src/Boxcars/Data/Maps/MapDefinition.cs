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
    public required string Name { get; init; }
    public string? ShortName { get; init; }
}
