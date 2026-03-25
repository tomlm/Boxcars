using System.Reflection;
using Boxcars.Data;
using Boxcars.Engine.Data.Maps;

namespace Boxcars.Services.Maps;

public static class MapProbabilityService
{
    private const double MinimumGeneratedCityProbabilityPercent = 0.1d;
    private const double MaximumGeneratedCityProbabilityPercent = 5.0d;
    private const double GeneratedWeightRangeLog10 = 3d;
    private static readonly FieldInfo? PayoutChartField = typeof(MapDefinition).GetField("_payoutChart", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly PropertyInfo? MaxPayoutIndexProperty = typeof(MapDefinition).GetProperty(nameof(MapDefinition.MaxPayoutIndex), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    public static IReadOnlyList<CityProbabilityOverride> GenerateRandomCityProbabilities(MapDefinition mapDefinition, Random? random = null)
    {
        ArgumentNullException.ThrowIfNull(mapDefinition);

        random ??= Random.Shared;
        if (mapDefinition.Cities.Count == 0)
        {
            return [];
        }

        if ((MinimumGeneratedCityProbabilityPercent * mapDefinition.Cities.Count) > 100d)
        {
            throw new InvalidOperationException(
                $"Cannot generate city probabilities with a minimum of {MinimumGeneratedCityProbabilityPercent:N1}% for {mapDefinition.Cities.Count} cities because the total would exceed 100%.");
        }
        var effectiveMaximumGeneratedCityProbabilityPercent = Math.Max(
            MaximumGeneratedCityProbabilityPercent,
            100d / mapDefinition.Cities.Count);

        var weights = new double[mapDefinition.Cities.Count];
        var totalWeight = 0d;
        for (var index = 0; index < weights.Length; index++)
        {
            // Log-scale weights create a healthier long-tail distribution than flat uniform draws.
            weights[index] = Math.Pow(10d, random.NextDouble() * GeneratedWeightRangeLog10);
            totalWeight += weights[index];
        }

        var remainingProbabilityPercent = 100d - (MinimumGeneratedCityProbabilityPercent * mapDefinition.Cities.Count);

        var generated = mapDefinition.Cities
            .Select((city, index) => new CityProbabilityOverride
            {
                CityName = city.Name,
                RegionCode = city.RegionCode,
                Probability = MinimumGeneratedCityProbabilityPercent + ((weights[index] / totalWeight) * remainingProbabilityPercent)
            })
            .ToList();

        return CapAndRenormalizeGeneratedProbabilities(generated, effectiveMaximumGeneratedCityProbabilityPercent);
    }

    private static List<CityProbabilityOverride> CapAndRenormalizeGeneratedProbabilities(
        IReadOnlyList<CityProbabilityOverride> generated,
        double maximumGeneratedCityProbabilityPercent)
    {
        var adjusted = generated
            .Select(entry => entry with
            {
                Probability = Math.Clamp(entry.Probability, MinimumGeneratedCityProbabilityPercent, maximumGeneratedCityProbabilityPercent)
            })
            .ToList();

        var iteration = 0;
        while (iteration < 256)
        {
            iteration++;

            var total = adjusted.Sum(entry => entry.Probability);
            var difference = 100d - total;
            if (Math.Abs(difference) < 0.000001d)
            {
                break;
            }

            var adjustableEntries = adjusted
                .Select((entry, index) => new { Entry = entry, Index = index })
                .Where(item => difference > 0d
                    ? item.Entry.Probability < maximumGeneratedCityProbabilityPercent - 0.000001d
                    : item.Entry.Probability > MinimumGeneratedCityProbabilityPercent + 0.000001d)
                .ToList();

            if (adjustableEntries.Count == 0)
            {
                break;
            }

            var deltaPerEntry = difference / adjustableEntries.Count;
            foreach (var item in adjustableEntries)
            {
                adjusted[item.Index] = item.Entry with
                {
                    Probability = Math.Clamp(
                        item.Entry.Probability + deltaPerEntry,
                        MinimumGeneratedCityProbabilityPercent,
                        maximumGeneratedCityProbabilityPercent)
                };
            }
        }

        var finalDifference = 100d - adjusted.Sum(entry => entry.Probability);
        if (Math.Abs(finalDifference) >= 0.000001d)
        {
            var candidateIndex = adjusted.FindIndex(entry =>
                finalDifference > 0d
                    ? entry.Probability < maximumGeneratedCityProbabilityPercent - 0.000001d
                    : entry.Probability > MinimumGeneratedCityProbabilityPercent + 0.000001d);
            if (candidateIndex >= 0)
            {
                var candidate = adjusted[candidateIndex];
                adjusted[candidateIndex] = candidate with
                {
                    Probability = Math.Clamp(
                        candidate.Probability + finalDifference,
                        MinimumGeneratedCityProbabilityPercent,
                        maximumGeneratedCityProbabilityPercent)
                };
            }
        }

        return adjusted;
    }

    public static MapDefinition ApplyCityProbabilities(
        MapDefinition mapDefinition,
        IReadOnlyList<CityProbabilityOverride>? overrides,
        IReadOnlyList<RailroadPriceOverride>? railroadPriceOverrides = null)
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
            : MapRailroadPricingService.CalculatePurchasePrices(clone);
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
