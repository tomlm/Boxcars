using Boxcars.Data;
using Boxcars.Engine.Data.Maps;

namespace Boxcars.Services;

public sealed class NetworkCoverageService
{
    public NetworkCoverageSnapshot BuildSnapshot(MapDefinition mapDefinition, IEnumerable<int> ownedRailroadIndices)
    {
        ArgumentNullException.ThrowIfNull(mapDefinition);
        ArgumentNullException.ThrowIfNull(ownedRailroadIndices);

        var ownedIndices = ownedRailroadIndices.ToHashSet();
        var cityCoverage = BuildCityCoverage(mapDefinition);
        var totalCityCount = cityCoverage.Count;

        if (totalCityCount == 0)
        {
            return NetworkCoverageSnapshot.Empty;
        }

        var accessibleCityCount = cityCoverage.Count(entry => entry.Value.Overlaps(ownedIndices));
        var monopolyCityCount = cityCoverage.Count(entry => entry.Value.Count > 0 && entry.Value.All(ownedIndices.Contains));

        return new NetworkCoverageSnapshot
        {
            AccessibleCityCount = accessibleCityCount,
            TotalCityCount = totalCityCount,
            AccessibleCityPercent = CalculatePercent(accessibleCityCount, totalCityCount),
            MonopolyCityCount = monopolyCityCount,
            MonopolyCityPercent = CalculatePercent(monopolyCityCount, totalCityCount)
        };
    }

    public NetworkCoverageSnapshot BuildProjectedSnapshot(MapDefinition mapDefinition, IEnumerable<int> ownedRailroadIndices, int railroadIndex)
    {
        ArgumentNullException.ThrowIfNull(ownedRailroadIndices);

        var projectedOwnedIndices = ownedRailroadIndices.ToHashSet();
        projectedOwnedIndices.Add(railroadIndex);
        return BuildSnapshot(mapDefinition, projectedOwnedIndices);
    }

    public RailroadOverlayInfo BuildRailroadOverlayInfo(
        MapDefinition mapDefinition,
        IEnumerable<int> ownedRailroadIndices,
        RailroadPurchaseOption railroadOption)
    {
        ArgumentNullException.ThrowIfNull(railroadOption);

        var currentCoverage = BuildSnapshot(mapDefinition, ownedRailroadIndices);
        var projectedCoverage = BuildProjectedSnapshot(mapDefinition, ownedRailroadIndices, railroadOption.RailroadIndex);

        return new RailroadOverlayInfo
        {
            RailroadIndex = railroadOption.RailroadIndex,
            RailroadName = railroadOption.RailroadName,
            PurchasePrice = railroadOption.PurchasePrice,
            AccessChangePercent = projectedCoverage.AccessibleCityPercent - currentCoverage.AccessibleCityPercent,
            MonopolyChangePercent = projectedCoverage.MonopolyCityPercent - currentCoverage.MonopolyCityPercent
        };
    }

    private static Dictionary<string, HashSet<int>> BuildCityCoverage(MapDefinition mapDefinition)
    {
        var regionIndexByCode = mapDefinition.Regions
            .ToDictionary(region => region.Code, region => region.Index, StringComparer.OrdinalIgnoreCase);

        var coverage = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);

        foreach (var city in mapDefinition.Cities)
        {
            if (!city.MapDotIndex.HasValue || !regionIndexByCode.TryGetValue(city.RegionCode, out var regionIndex))
            {
                continue;
            }

            var servingRailroads = mapDefinition.RailroadRouteSegments
                .Where(segment => SegmentTouchesCity(segment, regionIndex, city.MapDotIndex.Value))
                .Select(segment => segment.RailroadIndex)
                .ToHashSet();

            coverage[BuildCityKey(city)] = servingRailroads;
        }

        return coverage;
    }

    private static bool SegmentTouchesCity(RailroadRouteSegmentDefinition segment, int regionIndex, int dotIndex)
    {
        return (segment.StartRegionIndex == regionIndex && segment.StartDotIndex == dotIndex)
            || (segment.EndRegionIndex == regionIndex && segment.EndDotIndex == dotIndex);
    }

    private static string BuildCityKey(CityDefinition city)
    {
        return string.Concat(city.RegionCode, ":", city.Name);
    }

    private static decimal CalculatePercent(int value, int total)
    {
        return total <= 0
            ? 0m
            : Math.Round((decimal)value * 100m / total, 1, MidpointRounding.AwayFromZero);
    }
}