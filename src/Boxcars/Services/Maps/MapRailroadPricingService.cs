using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Persistence;

namespace Boxcars.Services.Maps;

/// <summary>
/// Prices railroads the way Rail Baron Player (RBP) does: a railroad's price is 3.75 times its estimated income,
/// rounded down to a multiple of $500. RBP's income formula isn't published; this estimates it from the two things
/// that best reproduce RBP's published incomes on U21 (R^2 ~= 0.8 across four RBP reports):
/// <list type="bullet">
/// <item>destination share: each city's destination probability, split evenly among the railroads serving it;</item>
/// <item>route share: over every trip, weighted by its probability, the fraction of its shortest route on the railroad.</item>
/// </list>
/// </summary>
public static class MapRailroadPricingService
{
    // Income = weight x component, least-squares fit to RBP's INCOME column across four U21 reports (112 railroads).
    private const double DestinationShareIncomeWeight = 88_325d;
    private const double RouteShareIncomeWeight = 43_375d;
    private const double PricePerDollarOfIncome = 3.75d;
    private const int PriceIncrement = 500;

    /// <param name="minimumRailroadPrice">Floor for every computed price (the game's minimum railroad price setting).</param>
    public static IReadOnlyDictionary<int, int> CalculatePurchasePrices(MapDefinition mapDefinition, int? minimumRailroadPrice = null)
    {
        ArgumentNullException.ThrowIfNull(mapDefinition);

        var floor = Math.Max(0, minimumRailroadPrice ?? GameSettings.Default.MinimumRailroadPrice);
        return CalculateEstimatedIncome(mapDefinition).ToDictionary(
            pair => pair.Key,
            pair => Math.Max(floor, PriceFromIncome(pair.Value)));
    }

    /// <summary>RBP's price rule: 3.75 times income, rounded down to a multiple of $500.</summary>
    public static int PriceFromIncome(double income)
    {
        return (int)(Math.Floor(Math.Max(0d, income) * PricePerDollarOfIncome / PriceIncrement) * PriceIncrement);
    }

    public static IReadOnlyDictionary<int, double> CalculateEstimatedIncome(MapDefinition mapDefinition)
    {
        return CalculateIncomeComponents(mapDefinition).ToDictionary(
            pair => pair.Key,
            pair => (DestinationShareIncomeWeight * pair.Value.DestinationShare) + (RouteShareIncomeWeight * pair.Value.RouteShare));
    }

    public static IReadOnlyDictionary<int, RailroadIncomeComponents> CalculateIncomeComponents(MapDefinition mapDefinition)
    {
        ArgumentNullException.ThrowIfNull(mapDefinition);

        var cityEntries = BuildCityEntries(mapDefinition);
        var servingRailroadsByNode = BuildServingRailroadsByNode(mapDefinition);
        var destinationShares = mapDefinition.Railroads.ToDictionary(railroad => railroad.Index, _ => 0d);
        var routeShares = mapDefinition.Railroads.ToDictionary(railroad => railroad.Index, _ => 0d);

        foreach (var city in cityEntries)
        {
            if (!servingRailroadsByNode.TryGetValue(city.NodeId, out var servingRailroads) || servingRailroads.Count == 0)
            {
                continue;
            }

            foreach (var railroadIndex in servingRailroads.Where(destinationShares.ContainsKey))
            {
                destinationShares[railroadIndex] += city.ProbabilityFraction / servingRailroads.Count;
            }
        }

        var routeService = new MapRouteService();
        var routeContext = routeService.BuildContext(mapDefinition);
        var totalTripProbability = 0d;
        foreach (var origin in cityEntries)
        {
            foreach (var destination in cityEntries)
            {
                var tripProbability = origin.ProbabilityFraction * destination.ProbabilityFraction;
                if (tripProbability <= 0d || string.Equals(origin.NodeId, destination.NodeId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var shortestSelection = routeService.FindShortestSelection(routeContext, origin.NodeId, destination.NodeId);
                if (shortestSelection is null || shortestSelection.Segments.Count == 0)
                {
                    continue;
                }

                totalTripProbability += tripProbability;
                foreach (var railroadGroup in shortestSelection.Segments.GroupBy(segment => segment.RailroadIndex))
                {
                    if (routeShares.ContainsKey(railroadGroup.Key))
                    {
                        routeShares[railroadGroup.Key] += tripProbability * railroadGroup.Count() / shortestSelection.Segments.Count;
                    }
                }
            }
        }

        return mapDefinition.Railroads.ToDictionary(
            railroad => railroad.Index,
            railroad => new RailroadIncomeComponents(
                destinationShares[railroad.Index],
                totalTripProbability > 0d ? routeShares[railroad.Index] / totalTripProbability : 0d));
    }

    private static Dictionary<string, HashSet<int>> BuildServingRailroadsByNode(MapDefinition mapDefinition)
    {
        var servingRailroads = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var segment in mapDefinition.RailroadRouteSegments)
        {
            foreach (var nodeId in new[]
                     {
                         MapRouteService.NodeKey(segment.StartRegionIndex, segment.StartDotIndex),
                         MapRouteService.NodeKey(segment.EndRegionIndex, segment.EndDotIndex)
                     })
            {
                if (!servingRailroads.TryGetValue(nodeId, out var railroads))
                {
                    railroads = [];
                    servingRailroads[nodeId] = railroads;
                }

                railroads.Add(segment.RailroadIndex);
            }
        }

        return servingRailroads;
    }

    private static List<PricedCityEntry> BuildCityEntries(MapDefinition mapDefinition)
    {
        var regionIndexByCode = mapDefinition.Regions.ToDictionary(region => region.Code, region => region.Index, StringComparer.OrdinalIgnoreCase);
        var probabilitiesByCityKey = BuildGlobalCityProbabilities(mapDefinition);

        return mapDefinition.Cities
            .Where(city => city.MapDotIndex.HasValue
                && regionIndexByCode.ContainsKey(city.RegionCode)
                && probabilitiesByCityKey.ContainsKey(BuildCityKey(city.RegionCode, city.Name)))
            .Select(city =>
            {
                var regionIndex = regionIndexByCode[city.RegionCode];
                return new PricedCityEntry(
                    MapRouteService.NodeKey(regionIndex, city.MapDotIndex!.Value),
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

    private static string BuildCityKey(string regionCode, string cityName)
    {
        return string.Concat(regionCode.Trim(), "::", cityName.Trim());
    }

    private sealed record PricedCityEntry(
        string NodeId,
        double ProbabilityFraction);
}

/// <param name="DestinationShare">Sum of each served city's destination probability divided by its number of railroads (0-1).</param>
/// <param name="RouteShare">Probability-weighted fraction of every trip's shortest route on the railroad (0-1).</param>
public sealed record RailroadIncomeComponents(double DestinationShare, double RouteShare);
