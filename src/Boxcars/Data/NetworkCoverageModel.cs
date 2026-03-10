namespace Boxcars.Data;

public sealed class NetworkCoverageSnapshot
{
    public int AccessibleCityCount { get; init; }

    public int TotalCityCount { get; init; }

    public decimal AccessibleCityPercent { get; init; }

    public int MonopolyCityCount { get; init; }

    public decimal MonopolyCityPercent { get; init; }

    public static NetworkCoverageSnapshot Empty { get; } = new();
}