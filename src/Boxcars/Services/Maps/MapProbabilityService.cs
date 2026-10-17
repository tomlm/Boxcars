using System.Reflection;
using Boxcars.Data;
using Boxcars.Engine.Data.Maps;

namespace Boxcars.Services.Maps;

public static class MapProbabilityService
{
    // A destination chart has a slot for each red-die parity (odd/even) and each two-dice total (2-12).
    private const int ChartSlotCount = 22;
    private const int ChartOutcomeCount = 72;
    private static readonly FieldInfo? PayoutChartField = typeof(MapDefinition).GetField("_payoutChart", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly PropertyInfo? MaxPayoutIndexProperty = typeof(MapDefinition).GetProperty(nameof(MapDefinition.MaxPayoutIndex), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    /// <summary>
    /// Random destination odds built the way Rail Baron's tables work: a region chart and, for each region, a city
    /// chart, each with 22 slots (red die odd/even, then the two white dice 2-12). Every region and city gets one slot
    /// in a random place and the rest of the slots go to random regions or cities as repeats. A slot's chance follows
    /// the dice (a 7 comes up six times as often as a 2), so a city's chance is its region's slots times its own.
    /// </summary>
    public static IReadOnlyList<CityProbabilityOverride> GenerateRandomCityProbabilities(MapDefinition mapDefinition, Random? random = null)
    {
        ArgumentNullException.ThrowIfNull(mapDefinition);

        random ??= Random.Shared;
        var regionCodes = mapDefinition.Regions
            .Select(region => region.Code)
            .Where(code => mapDefinition.Cities.Any(city => string.Equals(city.RegionCode, code, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (regionCodes.Count == 0)
        {
            return [];
        }

        var regionChances = BuildRandomChartChances(regionCodes, random, "regions on the map");
        var overrides = new List<CityProbabilityOverride>(mapDefinition.Cities.Count);
        foreach (var regionCode in regionCodes)
        {
            var cities = mapDefinition.Cities
                .Where(city => string.Equals(city.RegionCode, regionCode, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var cityChances = BuildRandomChartChances(cities, random, $"cities in region {regionCode}");
            overrides.AddRange(cities.Select(city => new CityProbabilityOverride
            {
                CityName = city.Name,
                RegionCode = city.RegionCode,
                Probability = regionChances[regionCode] * cityChances[city] * 100d
            }));
        }

        return overrides;
    }

    /// <summary>
    /// Places every entry once in a random slot of a 22-slot chart, fills the remaining slots with random entries, and
    /// returns each entry's chance of being rolled.
    /// </summary>
    private static Dictionary<T, double> BuildRandomChartChances<T>(IReadOnlyList<T> entries, Random random, string description)
        where T : notnull
    {
        if (entries.Count > ChartSlotCount)
        {
            throw new InvalidOperationException(
                $"Cannot build a random destination chart for {entries.Count} {description}: a chart has only {ChartSlotCount} slots.");
        }

        var slots = Enumerable.Range(0, ChartSlotCount).ToArray();
        random.Shuffle(slots);

        var chances = entries.ToDictionary(entry => entry, _ => 0d);
        for (var index = 0; index < ChartSlotCount; index++)
        {
            var entry = index < entries.Count ? entries[index] : entries[random.Next(entries.Count)];
            chances[entry] += (double)CountWaysToRoll(slots[index]) / ChartOutcomeCount;
        }

        return chances;
    }

    /// <summary>Ways two dice make the slot's total (slots 0-10 are odd, 11-21 even, each for totals 2-12).</summary>
    private static int CountWaysToRoll(int slot)
    {
        var total = (slot % 11) + 2;
        return 6 - Math.Abs(total - 7);
    }

    public static MapDefinition ApplyCityProbabilities(
        MapDefinition mapDefinition,
        IReadOnlyList<CityProbabilityOverride>? overrides,
        IReadOnlyList<RailroadPriceOverride>? railroadPriceOverrides = null,
        int? minimumRailroadPrice = null)
    {
        ArgumentNullException.ThrowIfNull(mapDefinition);

        if (overrides is null || overrides.Count == 0)
        {
            return mapDefinition;
        }

        var normalizedOverrides = Normalize(overrides);
        var globalCityProbabilitiesByKey = BuildDefaultGlobalCityProbabilities(mapDefinition);
        foreach (var overrideValue in normalizedOverrides)
        {
            globalCityProbabilitiesByKey[BuildCityKey(overrideValue.RegionCode, overrideValue.CityName)] = overrideValue.Probability;
        }

        globalCityProbabilitiesByKey = NormalizeGlobalProbabilities(globalCityProbabilitiesByKey);
        var cityProbabilitiesByKey = globalCityProbabilitiesByKey.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
        var regionProbabilitiesByCode = mapDefinition.Regions.ToDictionary(
            region => region.Code,
            region => cityProbabilitiesByKey
                .Where(pair => pair.Key.StartsWith(string.Concat(region.Code.Trim(), "::"), StringComparison.OrdinalIgnoreCase))
                .Sum(pair => pair.Value),
            StringComparer.OrdinalIgnoreCase);
        var withinRegionCityProbabilitiesByKey = cityProbabilitiesByKey.ToDictionary(
            pair => pair.Key,
            pair =>
            {
                var separatorIndex = pair.Key.IndexOf("::", StringComparison.Ordinal);
                var regionCode = separatorIndex >= 0 ? pair.Key[..separatorIndex] : string.Empty;
                return regionProbabilitiesByCode.GetValueOrDefault(regionCode, 0d) > 0d
                    ? (pair.Value / regionProbabilitiesByCode[regionCode]) * 100d
                    : 0d;
            },
            StringComparer.OrdinalIgnoreCase);

        var clone = Clone(mapDefinition);

        clone.Cities.Clear();
        foreach (var city in mapDefinition.Cities)
        {
            var probability = withinRegionCityProbabilitiesByKey.TryGetValue(BuildCityKey(city.RegionCode, city.Name), out var overrideProbability)
                ? overrideProbability
                : Math.Max(0d, city.Probability ?? 0d);

            clone.Cities.Add(new CityDefinition
            {
                Name = city.Name,
                RegionCode = city.RegionCode,
                Probability = probability,
                PayoutIndex = city.PayoutIndex,
                MapDotIndex = city.MapDotIndex
            });
        }

        clone.Regions.Clear();
        foreach (var region in mapDefinition.Regions)
        {
            clone.Regions.Add(new RegionDefinition
            {
                Index = region.Index,
                Name = region.Name,
                Code = region.Code,
                Probability = regionProbabilitiesByCode.TryGetValue(region.Code, out var probability) ? probability : 0d
            });
        }

        var updatedPrices = (railroadPriceOverrides is not null && railroadPriceOverrides.Count > 0)
            ? railroadPriceOverrides.ToDictionary(overrideValue => overrideValue.RailroadIndex, overrideValue => overrideValue.PurchasePrice)
            : MapRailroadPricingService.CalculatePurchasePrices(clone, minimumRailroadPrice);
        clone.Railroads.Clear();
        foreach (var railroad in mapDefinition.Railroads)
        {
            clone.Railroads.Add(new RailroadDefinition
            {
                Index = railroad.Index,
                Name = railroad.Name,
                MediumName = railroad.MediumName,
                ShortName = railroad.ShortName,
                PurchasePrice = updatedPrices.GetValueOrDefault(railroad.Index, railroad.PurchasePrice ?? 500),
                ColorIndex = railroad.ColorIndex,
                Red = railroad.Red,
                Green = railroad.Green,
                Blue = railroad.Blue
            });
        }

        return clone;
    }

    private static Dictionary<string, double> BuildDefaultGlobalCityProbabilities(MapDefinition mapDefinition)
    {
        var regionProbabilities = mapDefinition.Regions.ToDictionary(
            region => region.Code,
            region => Math.Max(0d, region.Probability ?? 0d),
            StringComparer.OrdinalIgnoreCase);
        var globalProbabilities = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var city in mapDefinition.Cities)
        {
            var regionProbability = regionProbabilities.GetValueOrDefault(city.RegionCode, 0d);
            var withinRegionProbability = Math.Max(0d, city.Probability ?? 0d);
            globalProbabilities[BuildCityKey(city.RegionCode, city.Name)] = regionProbability * withinRegionProbability / 100d;
        }

        return NormalizeGlobalProbabilities(globalProbabilities);
    }

    private static Dictionary<string, double> NormalizeGlobalProbabilities(IReadOnlyDictionary<string, double> probabilitiesByKey)
    {
        var normalized = probabilitiesByKey.ToDictionary(
            pair => pair.Key,
            pair => Math.Max(double.Epsilon, pair.Value),
            StringComparer.OrdinalIgnoreCase);
        var total = normalized.Values.Sum();

        if (total <= 0d)
        {
            var uniformProbability = normalized.Count == 0 ? 0d : 100d / normalized.Count;
            return normalized.ToDictionary(pair => pair.Key, _ => uniformProbability, StringComparer.OrdinalIgnoreCase);
        }

        return normalized.ToDictionary(
            pair => pair.Key,
            pair => (pair.Value / total) * 100d,
            StringComparer.OrdinalIgnoreCase);
    }

    private static List<CityProbabilityOverride> Normalize(IReadOnlyList<CityProbabilityOverride> overrides)
    {
        var positiveOverrides = overrides
            .Select(overrideValue => new CityProbabilityOverride
            {
                CityName = overrideValue.CityName,
                RegionCode = overrideValue.RegionCode,
                Probability = Math.Max(double.Epsilon, overrideValue.Probability)
            })
            .ToList();

        var total = positiveOverrides.Sum(overrideValue => overrideValue.Probability);
        if (total <= 0d)
        {
            var uniformProbability = 100d / positiveOverrides.Count;
            return positiveOverrides
                .Select(overrideValue => overrideValue with { Probability = uniformProbability })
                .ToList();
        }

        return positiveOverrides
            .Select(overrideValue => overrideValue with { Probability = (overrideValue.Probability / total) * 100d })
            .ToList();
    }

    private static MapDefinition Clone(MapDefinition source)
    {
        var clone = new MapDefinition
        {
            Name = source.Name,
            Version = source.Version,
            ScaleLeft = source.ScaleLeft,
            ScaleTop = source.ScaleTop,
            ScaleWidth = source.ScaleWidth,
            ScaleHeight = source.ScaleHeight,
            BackgroundKey = source.BackgroundKey
        };

        foreach (var region in source.Regions)
        {
            clone.Regions.Add(new RegionDefinition
            {
                Index = region.Index,
                Name = region.Name,
                Code = region.Code,
                Probability = region.Probability
            });
        }

        foreach (var city in source.Cities)
        {
            clone.Cities.Add(new CityDefinition
            {
                Name = city.Name,
                RegionCode = city.RegionCode,
                Probability = city.Probability,
                PayoutIndex = city.PayoutIndex,
                MapDotIndex = city.MapDotIndex
            });
        }

        foreach (var railroad in source.Railroads)
        {
            clone.Railroads.Add(new RailroadDefinition
            {
                Index = railroad.Index,
                Name = railroad.Name,
                MediumName = railroad.MediumName,
                ShortName = railroad.ShortName,
                PurchasePrice = railroad.PurchasePrice,
                ColorIndex = railroad.ColorIndex,
                Red = railroad.Red,
                Green = railroad.Green,
                Blue = railroad.Blue
            });
        }

        foreach (var trainDot in source.TrainDots)
        {
            clone.TrainDots.Add(new TrainDot
            {
                Id = trainDot.Id,
                RegionIndex = trainDot.RegionIndex,
                DotIndex = trainDot.DotIndex,
                X = trainDot.X,
                Y = trainDot.Y,
                ColorIndex = trainDot.ColorIndex
            });
        }

        foreach (var segment in source.RailroadRouteSegments)
        {
            clone.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition
            {
                RailroadIndex = segment.RailroadIndex,
                StartRegionIndex = segment.StartRegionIndex,
                StartDotIndex = segment.StartDotIndex,
                EndRegionIndex = segment.EndRegionIndex,
                EndDotIndex = segment.EndDotIndex
            });
        }

        foreach (var line in source.MapLines)
        {
            clone.MapLines.Add(new LineSegment
            {
                X1 = line.X1,
                Y1 = line.Y1,
                X2 = line.X2,
                Y2 = line.Y2,
                StyleIndex = line.StyleIndex
            });
        }

        foreach (var separator in source.Separators)
        {
            clone.Separators.Add(new LineSegment
            {
                X1 = separator.X1,
                Y1 = separator.Y1,
                X2 = separator.X2,
                Y2 = separator.Y2,
                StyleIndex = separator.StyleIndex
            });
        }

        foreach (var label in source.RegionLabels)
        {
            clone.RegionLabels.Add(new RegionLabelDefinition
            {
                Text = label.Text,
                X = label.X,
                Y = label.Y
            });
        }

        if (PayoutChartField?.GetValue(source) is int[,] payoutChart)
        {
            PayoutChartField.SetValue(clone, payoutChart.Clone());
        }

        MaxPayoutIndexProperty?.SetValue(clone, source.MaxPayoutIndex);
        return clone;
    }

    private static string BuildCityKey(string regionCode, string cityName)
    {
        return string.Concat(regionCode.Trim(), "::", cityName.Trim());
    }
}
