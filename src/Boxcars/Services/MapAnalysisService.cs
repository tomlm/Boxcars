using Boxcars.Data;
using Boxcars.Engine.Data.Maps;
using Boxcars.Services.Maps;
using RailBaronGameEngine = Boxcars.Engine.Domain.GameEngine;

namespace Boxcars.Services;

public sealed class MapAnalysisService(MapRouteService mapRouteService)
{
    public MapAnalysisReport BuildReport(MapDefinition mapDefinition)
    {
        ArgumentNullException.ThrowIfNull(mapDefinition);

        var cityEntries = BuildCityEntries(mapDefinition);
        var railroadCityCoverage = BuildRailroadCityCoverage(mapDefinition, cityEntries);
        var mapContext = mapRouteService.BuildContext(mapDefinition);

        return new MapAnalysisReport
        {
            MapName = mapDefinition.Name ?? "Unknown Map",
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            RailroadRows = BuildRailroadRows(mapDefinition, cityEntries, railroadCityCoverage),
            CityAccessRows = BuildCityAccessRows(mapDefinition, cityEntries),
            RegionProbabilityRows = BuildRegionProbabilityRows(mapDefinition, cityEntries),
            AverageTripLengthDots = BuildAverageTripLength(mapDefinition, cityEntries, mapContext),
            AveragePayoff = BuildAveragePayoff(mapDefinition, cityEntries),
            AveragePayoffPerDot = BuildAveragePayoffPerDot(mapDefinition, cityEntries, mapContext)
        };
    }

    private static List<RailroadAnalysisRow> BuildRailroadRows(
        MapDefinition mapDefinition,
        List<CityEntry> cityEntries,
        Dictionary<int, HashSet<string>> railroadCityCoverage)
    {
        var cityCount = cityEntries.Count;
        var allRailroadCityCoverage = railroadCityCoverage
            .SelectMany(entry => entry.Value.Select(cityKey => new { entry.Key, CityKey = cityKey }))
            .GroupBy(entry => entry.CityKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(entry => entry.Key).ToHashSet(), StringComparer.OrdinalIgnoreCase);
        var nodeRailroadCoverage = BuildNodeRailroadCoverage(mapDefinition);

        return mapDefinition.Railroads
            .Select(railroad =>
            {
                railroadCityCoverage.TryGetValue(railroad.Index, out var servedCities);
                servedCities ??= [];

                var monopolyCityCount = servedCities.Count(cityKey =>
                    allRailroadCityCoverage.TryGetValue(cityKey, out var servingRailroads)
                    && servingRailroads.Count == 1
                    && servingRailroads.Contains(railroad.Index));

                var connectionCount = CountRailroadConnections(railroad.Index, mapDefinition, nodeRailroadCoverage);

                return new RailroadAnalysisRow
                {
                    RailroadCode = string.IsNullOrWhiteSpace(railroad.ShortName) ? railroad.Name : railroad.ShortName,
                    FullName = railroad.Name,
                    PurchasePrice = railroad.PurchasePrice ?? RailBaronGameEngine.GetRailroadPurchasePrice(railroad.Index),
                    CitiesServedCount = servedCities.Count,
                    ServicePercentage = CalculatePercent(servedCities.Count, cityCount),
                    MonopolyPercentage = CalculatePercent(monopolyCityCount, cityCount),
                    ConnectionCount = connectionCount,
                    ExpectedIncome = BuildExpectedIncome(mapDefinition, cityEntries, servedCities)
                };
            })
            .OrderByDescending(row => row.PurchasePrice)
            .ThenBy(row => row.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<CityAccessRow> BuildCityAccessRows(MapDefinition mapDefinition, List<CityEntry> cityEntries)
    {
        var cityWeights = NormalizeCityWeights(mapDefinition, cityEntries);
        var regionNameByCode = mapDefinition.Regions.ToDictionary(region => region.Code, region => region.Name, StringComparer.OrdinalIgnoreCase);

        return cityEntries
            .Select(city => new CityAccessRow
            {
                RegionCode = city.RegionCode,
                RegionName = regionNameByCode.TryGetValue(city.RegionCode, out var regionName) ? regionName : city.RegionCode,
                CityName = city.Name,
                WithinRegionPercentage = city.Probability.HasValue
                    ? Math.Round((decimal)city.Probability.Value, 2, MidpointRounding.AwayFromZero)
                    : 0m,
                GlobalAccessPercentage = cityWeights.TryGetValue(BuildCityKey(city.RegionCode, city.Name), out var weight)
                    ? weight
                    : 0m
            })
            .OrderBy(row => row.RegionCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.CityName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<RegionProbabilityRow> BuildRegionProbabilityRows(MapDefinition mapDefinition, List<CityEntry> cityEntries)
    {
        var regionWeights = NormalizeRegionWeights(mapDefinition, cityEntries);

        return mapDefinition.Regions
            .Select(region => new RegionProbabilityRow
            {
                RegionCode = region.Code,
                RegionName = region.Name,
                ProbabilityPercentage = regionWeights.TryGetValue(region.Code, out var weight) ? weight : 0m
            })
            .OrderBy(row => row.RegionName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static decimal BuildAverageTripLength(MapDefinition mapDefinition, List<CityEntry> cityEntries, MapRouteContext context)
    {
        var tripLengths = BuildTripLengths(cityEntries, context);
        if (tripLengths.Count == 0)
        {
            return 0m;
        }

        return Math.Round(tripLengths.Average(), 1, MidpointRounding.AwayFromZero);
    }

    private static decimal BuildAveragePayoff(MapDefinition mapDefinition, List<CityEntry> cityEntries)
    {
        var payoffs = BuildPayoffs(mapDefinition, cityEntries);
        if (payoffs.Count == 0)
        {
            return 0m;
        }

        return Math.Round(payoffs.Average(), 1, MidpointRounding.AwayFromZero);
    }

    private static decimal BuildAveragePayoffPerDot(MapDefinition mapDefinition, List<CityEntry> cityEntries, MapRouteContext context)
    {
        var payoffs = BuildPayoffs(mapDefinition, cityEntries);
        var tripLengths = BuildTripLengths(cityEntries, context);

        if (payoffs.Count == 0 || tripLengths.Count == 0)
        {
            return 0m;
        }

        var pairedCount = Math.Min(payoffs.Count, tripLengths.Count);
        var values = new List<decimal>(pairedCount);
        for (var index = 0; index < pairedCount; index++)
        {
            if (tripLengths[index] <= 0)
            {
                continue;
            }

            values.Add(payoffs[index] / tripLengths[index]);
        }

        return values.Count == 0
            ? 0m
            : Math.Round(values.Average(), 2, MidpointRounding.AwayFromZero);
    }

    private static List<decimal> BuildPayoffs(MapDefinition mapDefinition, List<CityEntry> cityEntries)
    {
        var payoffs = new List<decimal>();

        for (var left = 0; left < cityEntries.Count; left++)
        {
            for (var right = left + 1; right < cityEntries.Count; right++)
            {
                var origin = cityEntries[left];
                var destination = cityEntries[right];

                if (!origin.PayoutIndex.HasValue || !destination.PayoutIndex.HasValue)
                {
                    continue;
                }

                if (!mapDefinition.TryGetPayout(origin.PayoutIndex.Value, destination.PayoutIndex.Value, out var payout))
                {
                    continue;
                }

                payoffs.Add(payout);
            }
        }

        return payoffs;
    }

    private static List<decimal> BuildTripLengths(List<CityEntry> cityEntries, MapRouteContext context)
    {
        var tripLengths = new List<decimal>();

        for (var left = 0; left < cityEntries.Count; left++)
        {
            for (var right = left + 1; right < cityEntries.Count; right++)
            {
                var selection = context.Adjacency.ContainsKey(cityEntries[left].NodeId)
                    && context.Adjacency.ContainsKey(cityEntries[right].NodeId)
                    ? new MapRouteService().FindShortestSelection(context, cityEntries[left].NodeId, cityEntries[right].NodeId)
                    : null;

                if (selection is null)
                {
                    continue;
                }

                tripLengths.Add(selection.Segments.Count);
            }
        }

        return tripLengths;
    }

    private static decimal BuildExpectedIncome(MapDefinition mapDefinition, List<CityEntry> cityEntries, HashSet<string> servedCities)
    {
        var payoffs = new List<decimal>();

        foreach (var origin in cityEntries.Where(city => servedCities.Contains(BuildCityKey(city.RegionCode, city.Name))))
        {
            foreach (var destination in cityEntries)
            {
                if (string.Equals(origin.Name, destination.Name, StringComparison.OrdinalIgnoreCase)
                    || !origin.PayoutIndex.HasValue
                    || !destination.PayoutIndex.HasValue)
                {
                    continue;
                }

                if (mapDefinition.TryGetPayout(origin.PayoutIndex.Value, destination.PayoutIndex.Value, out var payout))
                {
                    payoffs.Add(payout);
                }
            }
        }

        return payoffs.Count == 0
            ? 0m
            : Math.Round(payoffs.Average(), 1, MidpointRounding.AwayFromZero);
    }

    private static Dictionary<string, HashSet<int>> BuildNodeRailroadCoverage(MapDefinition mapDefinition)
    {
        var coverage = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);

        foreach (var segment in mapDefinition.RailroadRouteSegments)
        {
            AddNodeCoverage(coverage, MapRouteService.NodeKey(segment.StartRegionIndex, segment.StartDotIndex), segment.RailroadIndex);
            AddNodeCoverage(coverage, MapRouteService.NodeKey(segment.EndRegionIndex, segment.EndDotIndex), segment.RailroadIndex);
        }

        return coverage;
    }

    private static int CountRailroadConnections(int railroadIndex, MapDefinition mapDefinition, Dictionary<string, HashSet<int>> nodeRailroadCoverage)
    {
        var railroadNodes = mapDefinition.RailroadRouteSegments
            .Where(segment => segment.RailroadIndex == railroadIndex)
            .SelectMany(segment => new[]
            {
                MapRouteService.NodeKey(segment.StartRegionIndex, segment.StartDotIndex),
                MapRouteService.NodeKey(segment.EndRegionIndex, segment.EndDotIndex)
            })
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return railroadNodes
            .Where(nodeRailroadCoverage.ContainsKey)
            .SelectMany(nodeId => nodeRailroadCoverage[nodeId])
            .Where(otherRailroadIndex => otherRailroadIndex != railroadIndex)
            .Distinct()
            .Count();
    }

    private static Dictionary<int, HashSet<string>> BuildRailroadCityCoverage(MapDefinition mapDefinition, List<CityEntry> cityEntries)
    {
        var coverage = mapDefinition.Railroads.ToDictionary(railroad => railroad.Index, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        foreach (var city in cityEntries)
        {
            var cityKey = BuildCityKey(city.RegionCode, city.Name);
            foreach (var segment in mapDefinition.RailroadRouteSegments.Where(segment => SegmentTouchesCity(segment, city.RegionIndex, city.MapDotIndex)))
            {
                coverage[segment.RailroadIndex].Add(cityKey);
            }
        }

        return coverage;
    }

    private static List<CityEntry> BuildCityEntries(MapDefinition mapDefinition)
    {
        var regionIndexByCode = mapDefinition.Regions
            .ToDictionary(region => region.Code, region => region.Index, StringComparer.OrdinalIgnoreCase);

        return mapDefinition.Cities
            .Where(city => city.MapDotIndex.HasValue && regionIndexByCode.ContainsKey(city.RegionCode))
            .Select(city => new CityEntry(
                city.Name,
                city.RegionCode,
                regionIndexByCode[city.RegionCode],
                city.MapDotIndex!.Value,
                MapRouteService.NodeKey(regionIndexByCode[city.RegionCode], city.MapDotIndex!.Value),
                city.Probability,
                city.PayoutIndex))
            .ToList();
    }

    private static Dictionary<string, decimal> NormalizeCityWeights(MapDefinition mapDefinition, List<CityEntry> cityEntries)
    {
        var regionWeights = mapDefinition.Regions
            .Where(region => region.Probability.HasValue && region.Probability.Value > 0)
            .ToDictionary(region => region.Code, region => region.Probability!.Value, StringComparer.OrdinalIgnoreCase);
        var weightedCities = cityEntries
            .Where(city => city.Probability.HasValue
                && city.Probability.Value > 0
                && regionWeights.ContainsKey(city.RegionCode))
            .ToList();

        if (weightedCities.Count == 0)
        {
            var uniformWeight = cityEntries.Count == 0 ? 0m : Math.Round(100m / cityEntries.Count, 1, MidpointRounding.AwayFromZero);
            return cityEntries.ToDictionary(city => BuildCityKey(city.RegionCode, city.Name), _ => uniformWeight, StringComparer.OrdinalIgnoreCase);
        }

        return cityEntries.ToDictionary(
            city => BuildCityKey(city.RegionCode, city.Name),
            city => city.Probability.HasValue
                && city.Probability.Value > 0
                && regionWeights.TryGetValue(city.RegionCode, out var regionWeight)
                ? Math.Round((decimal)(regionWeight * city.Probability.Value / 100d), 2, MidpointRounding.AwayFromZero)
                : 0m,
            StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, decimal> NormalizeRegionWeights(MapDefinition mapDefinition, List<CityEntry> cityEntries)
    {
        var weightedRegions = mapDefinition.Regions.Where(region => region.Probability.HasValue && region.Probability.Value > 0).ToList();
        if (weightedRegions.Count > 0)
        {
            var totalWeight = weightedRegions.Sum(region => region.Probability!.Value);
            return mapDefinition.Regions.ToDictionary(
                region => region.Code,
                region => region.Probability.HasValue && totalWeight > 0
                    ? Math.Round((decimal)(region.Probability.Value / totalWeight) * 100m, 1, MidpointRounding.AwayFromZero)
                    : 0m,
                StringComparer.OrdinalIgnoreCase);
        }

        return cityEntries
            .GroupBy(city => city.RegionCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => Math.Round(100m * group.Count() / Math.Max(1, cityEntries.Count), 1, MidpointRounding.AwayFromZero),
                StringComparer.OrdinalIgnoreCase);
    }

    private static void AddNodeCoverage(Dictionary<string, HashSet<int>> coverage, string nodeId, int railroadIndex)
    {
        if (!coverage.TryGetValue(nodeId, out var railroadIndices))
        {
            railroadIndices = [];
            coverage[nodeId] = railroadIndices;
        }

        railroadIndices.Add(railroadIndex);
    }

    private static bool SegmentTouchesCity(RailroadRouteSegmentDefinition segment, int regionIndex, int dotIndex)
    {
        return (segment.StartRegionIndex == regionIndex && segment.StartDotIndex == dotIndex)
            || (segment.EndRegionIndex == regionIndex && segment.EndDotIndex == dotIndex);
    }

    private static string BuildCityKey(string regionCode, string cityName)
    {
        return string.Concat(regionCode, ":", cityName);
    }

    private static decimal CalculatePercent(int value, int total)
    {
        return total <= 0
            ? 0m
            : Math.Round((decimal)value * 100m / total, 1, MidpointRounding.AwayFromZero);
    }

    private sealed record CityEntry(
        string Name,
        string RegionCode,
        int RegionIndex,
        int MapDotIndex,
        string NodeId,
        double? Probability,
        int? PayoutIndex);
}