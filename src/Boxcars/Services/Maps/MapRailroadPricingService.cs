using Boxcars.Engine.Data.Maps;

namespace Boxcars.Services.Maps;

public static class MapRailroadPricingService
{
    private const double RentalRate = 0.15d;
    private const int CoverageAdjacencyDepth = 1;
    private const double NearShortestPathFactor = 1.10d;
    private const double ExpectedRentalIncomeWeight = 1.25d;
    private const double CoverageWeight = 350d;
    private const double ChokePointWeight = 1000d;
    private const int MinimumRailroadPrice = 4_000;
    private const int MaximumRailroadPrice = 80_000;

    public static IReadOnlyDictionary<int, int> CalculatePurchasePrices(MapDefinition mapDefinition)
    {
        ArgumentNullException.ThrowIfNull(mapDefinition);

        if (mapDefinition.Railroads.Count == 0)
        {
            return new Dictionary<int, int>();
        }

        var routeService = new MapRouteService();
        var routeContext = routeService.BuildContext(mapDefinition);
        var cityEntries = BuildCityEntries(mapDefinition);
        var railroadNodeSets = BuildRailroadNodeSets(mapDefinition);
        var railroadCoverageScores = BuildCoverageScores(cityEntries, railroadNodeSets, routeContext.Adjacency);
        var expectedRentalIncomeScores = mapDefinition.Railroads.ToDictionary(railroad => railroad.Index, _ => 0d);
        var chokePointScores = mapDefinition.Railroads.ToDictionary(railroad => railroad.Index, _ => 0d);

        foreach (var origin in cityEntries)
        {
            foreach (var destination in cityEntries)
            {
                if (string.Equals(origin.NodeId, destination.NodeId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!mapDefinition.TryGetPayout(origin.PayoutIndex, destination.PayoutIndex, out var payout))
                {
                    continue;
                }

                var shortestSelection = routeService.FindShortestSelection(routeContext, origin.NodeId, destination.NodeId);
                if (shortestSelection is null || shortestSelection.Segments.Count == 0)
                {
                    continue;
                }

                var tripProbability = origin.ProbabilityFraction * destination.ProbabilityFraction;
                var totalDistance = shortestSelection.Segments.Count;
                var nearShortestDistanceLimit = (int)Math.Ceiling(totalDistance * NearShortestPathFactor);

                foreach (var railroadGroup in shortestSelection.Segments.GroupBy(segment => segment.RailroadIndex))
                {
                    var routeFractionOnRailroad = (double)railroadGroup.Count() / totalDistance;
                    expectedRentalIncomeScores[railroadGroup.Key] += payout * RentalRate * routeFractionOnRailroad * tripProbability;
                }

                foreach (var railroad in mapDefinition.Railroads)
                {
                    var distanceWithoutRailroad = FindShortestDistanceWithoutRailroad(routeContext.Adjacency, origin.NodeId, destination.NodeId, railroad.Index);
                    if (distanceWithoutRailroad is null || distanceWithoutRailroad.Value > nearShortestDistanceLimit)
                    {
                        chokePointScores[railroad.Index] += tripProbability * 100d;
                    }
                }
            }
        }

        var rawScoresByRailroad = mapDefinition.Railroads.ToDictionary(
            railroad => railroad.Index,
            railroad =>
                (ExpectedRentalIncomeWeight * Positive(expectedRentalIncomeScores[railroad.Index]))
                + (CoverageWeight * Positive(railroadCoverageScores.GetValueOrDefault(railroad.Index)))
                + (ChokePointWeight * Positive(chokePointScores[railroad.Index])));

        return ScaleScoresToPriceBand(rawScoresByRailroad);
    }

    private static List<PricedCityEntry> BuildCityEntries(MapDefinition mapDefinition)
    {
        var regionIndexByCode = mapDefinition.Regions.ToDictionary(region => region.Code, region => region.Index, StringComparer.OrdinalIgnoreCase);
        var probabilitiesByCityKey = BuildGlobalCityProbabilities(mapDefinition);

        return mapDefinition.Cities
            .Where(city => city.MapDotIndex.HasValue
                && city.PayoutIndex.HasValue
                && regionIndexByCode.ContainsKey(city.RegionCode)
                && probabilitiesByCityKey.ContainsKey(BuildCityKey(city.RegionCode, city.Name)))
            .Select(city =>
            {
                var regionIndex = regionIndexByCode[city.RegionCode];
                return new PricedCityEntry(
                    MapRouteService.NodeKey(regionIndex, city.MapDotIndex!.Value),
                    city.PayoutIndex!.Value,
                    probabilitiesByCityKey[BuildCityKey(city.RegionCode, city.Name)]);
            })
            .ToList();
    }

    private static Dictionary<string, double> BuildGlobalCityProbabilities(MapDefinition mapDefinition)
    {
        var rawCityProbabilities = mapDefinition.Cities
            .Where(city => city.Probability.HasValue && city.Probability.Value > 0d)
            .ToDictionary(city => BuildCityKey(city.RegionCode, city.Name), city => city.Probability!.Value, StringComparer.OrdinalIgnoreCase);

        var totalCityProbability = rawCityProbabilities.Values.Sum();
        if (totalCityProbability > 0.999999d && totalCityProbability < 1.000001d)
        {
            return rawCityProbabilities;
        }

        var regionWeights = BuildNormalizedRegionWeights(mapDefinition);
        var groupedCities = mapDefinition.Cities
            .GroupBy(city => city.RegionCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var (regionCode, cities) in groupedCities)
        {
            var regionWeight = regionWeights.GetValueOrDefault(regionCode, 0d);
            if (regionWeight <= 0d)
            {
                continue;
            }

            var positiveCities = cities
                .Select(city => new
                {
                    City = city,
                    Weight = Math.Max(double.Epsilon, city.Probability ?? 0d)
                })
                .ToList();
            var totalWithinRegion = positiveCities.Sum(entry => entry.Weight);

            foreach (var entry in positiveCities)
            {
                var withinRegionProbability = totalWithinRegion > 0d
                    ? entry.Weight / totalWithinRegion
                    : 1d / positiveCities.Count;
                result[BuildCityKey(entry.City.RegionCode, entry.City.Name)] = regionWeight * withinRegionProbability;
            }
        }

        var normalizationTotal = result.Values.Sum();
        if (normalizationTotal <= 0d)
        {
            var uniformProbability = mapDefinition.Cities.Count == 0 ? 0d : 1d / mapDefinition.Cities.Count;
            return mapDefinition.Cities.ToDictionary(
                city => BuildCityKey(city.RegionCode, city.Name),
                _ => uniformProbability,
                StringComparer.OrdinalIgnoreCase);
        }

        return result.ToDictionary(
            pair => pair.Key,
            pair => pair.Value / normalizationTotal,
            StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, double> BuildNormalizedRegionWeights(MapDefinition mapDefinition)
    {
        var positiveRegions = mapDefinition.Regions
            .Where(region => region.Probability.HasValue && region.Probability.Value > 0d)
            .ToList();

        if (positiveRegions.Count == 0)
        {
            var groupedCityCount = mapDefinition.Cities
                .Select(city => city.RegionCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            var uniformWeight = groupedCityCount == 0 ? 0d : 1d / groupedCityCount;
            return mapDefinition.Regions.ToDictionary(region => region.Code, _ => uniformWeight, StringComparer.OrdinalIgnoreCase);
        }

        var total = positiveRegions.Sum(region => region.Probability!.Value);
        return mapDefinition.Regions.ToDictionary(
            region => region.Code,
            region => region.Probability.HasValue && total > 0d
                ? region.Probability.Value / total
                : 0d,
            StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<int, HashSet<string>> BuildRailroadNodeSets(MapDefinition mapDefinition)
    {
        var railroadNodeSets = mapDefinition.Railroads.ToDictionary(
            railroad => railroad.Index,
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        foreach (var segment in mapDefinition.RailroadRouteSegments)
        {
            railroadNodeSets[segment.RailroadIndex].Add(MapRouteService.NodeKey(segment.StartRegionIndex, segment.StartDotIndex));
            railroadNodeSets[segment.RailroadIndex].Add(MapRouteService.NodeKey(segment.EndRegionIndex, segment.EndDotIndex));
        }

        return railroadNodeSets;
    }

    private static Dictionary<int, double> BuildCoverageScores(
        IReadOnlyList<PricedCityEntry> cityEntries,
        IReadOnlyDictionary<int, HashSet<string>> railroadNodeSets,
        IReadOnlyDictionary<string, List<RouteGraphEdge>> adjacency)
    {
        var coverageScores = railroadNodeSets.ToDictionary(pair => pair.Key, _ => 0d);

        foreach (var railroadNodeSet in railroadNodeSets)
        {
            var coveredNodes = ExpandNodes(railroadNodeSet.Value, adjacency, CoverageAdjacencyDepth);
            coverageScores[railroadNodeSet.Key] = cityEntries
                .Where(city => coveredNodes.Contains(city.NodeId))
                .Sum(city => city.ProbabilityFraction * 100d);
        }

        return coverageScores;
    }

    private static HashSet<string> ExpandNodes(
        IReadOnlySet<string> startingNodes,
        IReadOnlyDictionary<string, List<RouteGraphEdge>> adjacency,
        int maxDepth)
    {
        var visited = new HashSet<string>(startingNodes, StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string NodeId, int Depth)>(startingNodes.Select(nodeId => (nodeId, 0)));

        while (queue.Count > 0)
        {
            var (nodeId, depth) = queue.Dequeue();
            if (depth >= maxDepth || !adjacency.TryGetValue(nodeId, out var edges))
            {
                continue;
            }

            foreach (var edge in edges)
            {
                if (!visited.Add(edge.ToNodeId))
                {
                    continue;
                }

                queue.Enqueue((edge.ToNodeId, depth + 1));
            }
        }

        return visited;
    }

    private static int? FindShortestDistanceWithoutRailroad(
        IReadOnlyDictionary<string, List<RouteGraphEdge>> adjacency,
        string startNodeId,
        string destinationNodeId,
        int blockedRailroadIndex)
    {
        if (string.Equals(startNodeId, destinationNodeId, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var queue = new Queue<(string NodeId, int Distance)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { startNodeId };
        queue.Enqueue((startNodeId, 0));

        while (queue.Count > 0)
        {
            var (nodeId, distance) = queue.Dequeue();
            if (!adjacency.TryGetValue(nodeId, out var edges))
            {
                continue;
            }

            foreach (var edge in edges)
            {
                if (edge.RailroadIndex == blockedRailroadIndex || !visited.Add(edge.ToNodeId))
                {
                    continue;
                }

                var nextDistance = distance + 1;
                if (string.Equals(edge.ToNodeId, destinationNodeId, StringComparison.OrdinalIgnoreCase))
                {
                    return nextDistance;
                }

                queue.Enqueue((edge.ToNodeId, nextDistance));
            }
        }

        return null;
    }

    private static Dictionary<int, int> ScaleScoresToPriceBand(IReadOnlyDictionary<int, double> rawScoresByRailroad)
    {
        if (rawScoresByRailroad.Count == 0)
        {
            return new Dictionary<int, int>();
        }

        var minScore = rawScoresByRailroad.Values.Min();
        var maxScore = rawScoresByRailroad.Values.Max();

        if (Math.Abs(maxScore - minScore) < 0.000001d)
        {
            return rawScoresByRailroad.ToDictionary(
                pair => pair.Key,
                _ => MinimumRailroadPrice);
        }

        return rawScoresByRailroad.ToDictionary(
            pair => pair.Key,
            pair =>
            {
                var normalizedScore = (pair.Value - minScore) / (maxScore - minScore);
                var scaledValue = MinimumRailroadPrice + (normalizedScore * (MaximumRailroadPrice - MinimumRailroadPrice));
                return RoundPrice(scaledValue);
            });
    }

    private static int RoundPrice(double value)
    {
        var rounded = (int)(Math.Round(Math.Max(MinimumRailroadPrice, value) / 500d, MidpointRounding.AwayFromZero) * 500d);
        return Math.Clamp(rounded, MinimumRailroadPrice, MaximumRailroadPrice);
    }

    private static double Positive(double value)
    {
        return value > 0d ? value : double.Epsilon;
    }

    private static string BuildCityKey(string regionCode, string cityName)
    {
        return string.Concat(regionCode.Trim(), "::", cityName.Trim());
    }

    private sealed record PricedCityEntry(
        string NodeId,
        int PayoutIndex,
        double ProbabilityFraction);
}
