namespace Boxcars.Data;

public sealed class NetworkCoverageSnapshot
{
    public int AccessibleCityCount { get; init; }

    public int TotalCityCount { get; init; }

    public decimal AccessibleCityPercent { get; init; }

    public decimal AccessibleDestinationPercent { get; init; }

    public int MonopolyCityCount { get; init; }

    public decimal MonopolyCityPercent { get; init; }

    public decimal MonopolyDestinationPercent { get; init; }

    public IReadOnlyList<RegionCoverageSnapshot> RegionAccess { get; init; } = [];

    public static NetworkCoverageSnapshot Empty { get; } = new();
}

public sealed class RegionCoverageSnapshot
{
    public string RegionCode { get; init; } = string.Empty;

    public decimal AccessibleDestinationPercent { get; init; }

    public decimal MonopolyDestinationPercent { get; init; }
}