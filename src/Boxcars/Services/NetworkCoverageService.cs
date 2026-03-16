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
        return BuildSnapshotCore(mapDefinition, ownedIndices, ownedIndices);
    }

    public NetworkCoverageSnapshot BuildSnapshotIncludingPublicRailroads(
        MapDefinition mapDefinition,
        IEnumerable<int> ownedRailroadIndices,
        IEnumerable<int> otherOwnedRailroadIndices)
    {
        ArgumentNullException.ThrowIfNull(mapDefinition);
        ArgumentNullException.ThrowIfNull(ownedRailroadIndices);
        ArgumentNullException.ThrowIfNull(otherOwnedRailroadIndices);

        var ownedIndices = ownedRailroadIndices.ToHashSet();
        var blockedIndices = otherOwnedRailroadIndices.ToHashSet();
        blockedIndices.ExceptWith(ownedIndices);

        var accessibleIndices = mapDefinition.Railroads
            .Select(railroad => railroad.Index)
            .Where(index => !blockedIndices.Contains(index))
            .ToHashSet();

        return BuildSnapshotCore(mapDefinition, ownedIndices, accessibleIndices);
    }

    private static NetworkCoverageSnapshot BuildSnapshotCore(
        MapDefinition mapDefinition,
        IReadOnlySet<int> ownedIndices,
        IReadOnlySet<int> accessibleRailroadIndices)
    {
        var cityCoverage = BuildCityCoverage(mapDefinition);
        var totalCityCount = cityCoverage.Count;

        if (totalCityCount == 0)
        {
            return NetworkCoverageSnapshot.Empty;
        }

        var accessibleCityCount = cityCoverage.Count(entry => entry.ServingRailroads.Overlaps(accessibleRailroadIndices));
        var monopolyCityCount = cityCoverage.Count(entry => entry.ServingRailroads.Count > 0 && entry.ServingRailroads.All(ownedIndices.Contains));
        var accessibleDestinationPercent = Math.Round(cityCoverage
            .Where(entry => entry.ServingRailroads.Overlaps(accessibleRailroadIndices))
            .Sum(entry => entry.GlobalAccessPercentage), 1, MidpointRounding.AwayFromZero);
        var monopolyDestinationPercent = Math.Round(cityCoverage
            .Where(entry => entry.ServingRailroads.Count > 0 && entry.ServingRailroads.All(ownedIndices.Contains))
            .Sum(entry => entry.GlobalAccessPercentage), 1, MidpointRounding.AwayFromZero);
        var regionAccess = cityCoverage
            .GroupBy(entry => entry.RegionCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => new RegionCoverageSnapshot
            {
                RegionCode = group.Key,
                AccessibleDestinationPercent = Math.Round(group
                    .Where(entry => entry.ServingRailroads.Overlaps(accessibleRailroadIndices))
                    .Sum(entry => entry.WithinRegionPercentage), 1, MidpointRounding.AwayFromZero),
                MonopolyDestinationPercent = Math.Round(group
                    .Where(entry => entry.ServingRailroads.Count > 0 && entry.ServingRailroads.All(ownedIndices.Contains))
                    .Sum(entry => entry.WithinRegionPercentage), 1, MidpointRounding.AwayFromZero)
            })
            .OrderBy(region => region.RegionCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new NetworkCoverageSnapshot
        {
            AccessibleCityCount = accessibleCityCount,
            TotalCityCount = totalCityCount,
            AccessibleCityPercent = CalculatePercent(accessibleCityCount, totalCityCount),
            AccessibleDestinationPercent = accessibleDestinationPercent,
            MonopolyCityCount = monopolyCityCount,
            MonopolyCityPercent = CalculatePercent(monopolyCityCount, totalCityCount),
            MonopolyDestinationPercent = monopolyDestinationPercent,
            RegionAccess = regionAccess
        };
    }

    public NetworkCoverageSnapshot BuildProjectedSnapshot(MapDefinition mapDefinition, IEnumerable<int> ownedRailroadIndices, int railroadIndex)
    {
        ArgumentNullException.ThrowIfNull(ownedRailroadIndices);

        var projectedOwnedIndices = ownedRailroadIndices.ToHashSet();
        projectedOwnedIndices.Add(railroadIndex);
        return BuildSnapshot(mapDefinition, projectedOwnedIndices);
    }

    public NetworkCoverageSnapshot BuildProjectedSnapshotAfterSale(MapDefinition mapDefinition, IEnumerable<int> ownedRailroadIndices, int railroadIndex)
    {
        ArgumentNullException.ThrowIfNull(mapDefinition);
        ArgumentNullException.ThrowIfNull(ownedRailroadIndices);

        var projectedOwnedIndices = ownedRailroadIndices.ToHashSet();
        projectedOwnedIndices.Remove(railroadIndex);
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
            ValueLabel = "Purchase price",
            PurchasePrice = railroadOption.PurchasePrice,
            IsAffordable = railroadOption.IsAffordable,
            ValueKind = railroadOption.IsAffordable ? RailroadOverlayValueKind.Affordable : RailroadOverlayValueKind.TooExpensive,
            MetricRows = BuildOverlayMetricRows(currentCoverage, projectedCoverage, mapDefinition)
        };
    }

    private static List<RailroadOverlayMetricRow> BuildOverlayMetricRows(
        NetworkCoverageSnapshot currentCoverage,
        NetworkCoverageSnapshot projectedCoverage,
        MapDefinition mapDefinition)
    {
        var currentRegionAccessByCode = currentCoverage.RegionAccess.ToDictionary(region => region.RegionCode, region => region.AccessibleDestinationPercent, StringComparer.OrdinalIgnoreCase);
        var projectedRegionAccessByCode = projectedCoverage.RegionAccess.ToDictionary(region => region.RegionCode, region => region.AccessibleDestinationPercent, StringComparer.OrdinalIgnoreCase);
        var currentRegionMonopolyByCode = currentCoverage.RegionAccess.ToDictionary(region => region.RegionCode, region => region.MonopolyDestinationPercent, StringComparer.OrdinalIgnoreCase);
        var projectedRegionMonopolyByCode = projectedCoverage.RegionAccess.ToDictionary(region => region.RegionCode, region => region.MonopolyDestinationPercent, StringComparer.OrdinalIgnoreCase);
        var rows = new List<RailroadOverlayMetricRow>
        {
            new()
            {
                Label = "Total",
                AccessPercent = currentCoverage.AccessibleDestinationPercent,
                ProjectedAccessPercent = projectedCoverage.AccessibleDestinationPercent,
                MonopolyPercent = currentCoverage.MonopolyDestinationPercent,
                ProjectedMonopolyPercent = projectedCoverage.MonopolyDestinationPercent,
                AccessDeltaPercent = Math.Round(projectedCoverage.AccessibleDestinationPercent - currentCoverage.AccessibleDestinationPercent, 1, MidpointRounding.AwayFromZero),
                MonopolyDeltaPercent = Math.Round(projectedCoverage.MonopolyDestinationPercent - currentCoverage.MonopolyDestinationPercent, 1, MidpointRounding.AwayFromZero)
            }
        };

        rows.AddRange(mapDefinition.Regions.Select(region =>
        {
            var currentAccessPercent = currentRegionAccessByCode.TryGetValue(region.Code, out var currentAccessValue) ? currentAccessValue : 0m;
            var projectedAccessPercent = projectedRegionAccessByCode.TryGetValue(region.Code, out var projectedAccessValue) ? projectedAccessValue : 0m;
            var currentMonopolyPercent = currentRegionMonopolyByCode.TryGetValue(region.Code, out var currentMonopolyValue) ? currentMonopolyValue : 0m;
            var projectedMonopolyPercent = projectedRegionMonopolyByCode.TryGetValue(region.Code, out var projectedMonopolyValue) ? projectedMonopolyValue : 0m;
            return new RailroadOverlayMetricRow
            {
                Label = region.Code,
                AccessPercent = currentAccessPercent,
                ProjectedAccessPercent = projectedAccessPercent,
                MonopolyPercent = currentMonopolyPercent,
                ProjectedMonopolyPercent = projectedMonopolyPercent,
                AccessDeltaPercent = Math.Round(projectedAccessPercent - currentAccessPercent, 1, MidpointRounding.AwayFromZero),
                MonopolyDeltaPercent = Math.Round(projectedMonopolyPercent - currentMonopolyPercent, 1, MidpointRounding.AwayFromZero)
            };
        }));

        return rows;
    }

    private static List<CityCoverageEntry> BuildCityCoverage(MapDefinition mapDefinition)
    {
        var regionIndexByCode = mapDefinition.Regions
            .ToDictionary(region => region.Code, region => region.Index, StringComparer.OrdinalIgnoreCase);

        var globalAccessByCity = BuildGlobalCityAccessPercentages(mapDefinition, regionIndexByCode);
        var withinRegionAccessByCity = BuildWithinRegionCityAccessPercentages(mapDefinition, regionIndexByCode);
        var coverage = new List<CityCoverageEntry>();

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

            var cityKey = BuildCityKey(city);
            coverage.Add(new CityCoverageEntry(
                city.RegionCode,
                cityKey,
                servingRailroads,
                withinRegionAccessByCity.TryGetValue(cityKey, out var withinRegionPercent) ? withinRegionPercent : 0m,
                globalAccessByCity.TryGetValue(cityKey, out var globalPercent) ? globalPercent : 0m));
        }

        return coverage;
    }

    private static Dictionary<string, decimal> BuildGlobalCityAccessPercentages(
        MapDefinition mapDefinition,
        IReadOnlyDictionary<string, int> regionIndexByCode)
    {
        var regionWeights = BuildRegionProbabilityByCode(mapDefinition, regionIndexByCode);
        var withinRegionWeights = BuildWithinRegionCityAccessPercentages(mapDefinition, regionIndexByCode);

        return withinRegionWeights.ToDictionary(
            entry => entry.Key,
            entry =>
            {
                var separatorIndex = entry.Key.IndexOf(':');
                var regionCode = separatorIndex >= 0 ? entry.Key[..separatorIndex] : string.Empty;
                return regionWeights.TryGetValue(regionCode, out var regionWeight)
                    ? Math.Round(regionWeight * entry.Value / 100m, 2, MidpointRounding.AwayFromZero)
                    : 0m;
            },
            StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, decimal> BuildWithinRegionCityAccessPercentages(
        MapDefinition mapDefinition,
        IReadOnlyDictionary<string, int> regionIndexByCode)
    {
        return mapDefinition.Cities
            .Where(city => city.MapDotIndex.HasValue && regionIndexByCode.ContainsKey(city.RegionCode))
            .GroupBy(city => city.RegionCode, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group =>
            {
                var weightedCities = group.Where(city => city.Probability.HasValue && city.Probability.Value > 0).ToList();
                if (weightedCities.Count > 0)
                {
                    return group.Select(city => new KeyValuePair<string, decimal>(
                        BuildCityKey(city),
                        city.Probability.HasValue
                            ? Math.Round((decimal)city.Probability.Value, 2, MidpointRounding.AwayFromZero)
                            : 0m));
                }

                var uniformWeight = group.Any()
                    ? Math.Round(100m / group.Count(), 2, MidpointRounding.AwayFromZero)
                    : 0m;
                return group.Select(city => new KeyValuePair<string, decimal>(BuildCityKey(city), uniformWeight));
            })
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, decimal> BuildRegionProbabilityByCode(
        MapDefinition mapDefinition,
        IReadOnlyDictionary<string, int> regionIndexByCode)
    {
        var weightedRegions = mapDefinition.Regions.Where(region => region.Probability.HasValue && region.Probability.Value > 0).ToList();
        if (weightedRegions.Count > 0)
        {
            return weightedRegions.ToDictionary(
                region => region.Code,
                region => Math.Round((decimal)region.Probability!.Value, 3, MidpointRounding.AwayFromZero),
                StringComparer.OrdinalIgnoreCase);
        }

        var cityCountByRegion = mapDefinition.Cities
            .Where(city => city.MapDotIndex.HasValue && regionIndexByCode.ContainsKey(city.RegionCode))
            .GroupBy(city => city.RegionCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var totalCityCount = cityCountByRegion.Values.Sum();

        return cityCountByRegion.ToDictionary(
            entry => entry.Key,
            entry => totalCityCount == 0 ? 0m : Math.Round(entry.Value * 100m / totalCityCount, 3, MidpointRounding.AwayFromZero),
            StringComparer.OrdinalIgnoreCase);
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

    private sealed record CityCoverageEntry(
        string RegionCode,
        string CityKey,
        HashSet<int> ServingRailroads,
        decimal WithinRegionPercentage,
        decimal GlobalAccessPercentage);
}