using Boxcars.Engine.Tests.Fixtures;
using global::Boxcars.Data;
using global::Boxcars.Services.Maps;

namespace Boxcars.Engine.Tests.Unit;

public class MapProbabilityServiceTests
{
    [Fact]
    public void GenerateRandomCityProbabilities_EveryCityCanBeDrawn_AndTheOddsAddUpToOneHundredPercent()
    {
        var map = LoadStandardMap();

        var overrides = MapProbabilityService.GenerateRandomCityProbabilities(map, new Random(1234));

        Assert.Equal(map.Cities.Count, overrides.Count);
        Assert.All(overrides, entry => Assert.True(entry.Probability > 0d, $"{entry.CityName} can never be drawn."));
        Assert.InRange(overrides.Sum(entry => entry.Probability), 99.999999d, 100.000001d);
    }

    [Fact]
    public void GenerateRandomCityProbabilities_OddsComeFromTwentyTwoSlotDiceCharts()
    {
        var map = LoadStandardMap();

        for (var seed = 0; seed < 50; seed++)
        {
            var updated = MapProbabilityService.ApplyCityProbabilities(map, MapProbabilityService.GenerateRandomCityProbabilities(map, new Random(seed)));

            // Every region and every city is on its chart at least once (1/72 is the smallest slot, a 2 or a 12), and
            // all odds are whole numbers of the chart's 72 outcomes.
            foreach (var region in updated.Regions)
            {
                AssertWholeChartOutcomes(region.Probability ?? 0d, $"Seed {seed}: region {region.Code}");
                var cities = updated.Cities.Where(city => city.RegionCode == region.Code).ToList();
                Assert.All(cities, city => AssertWholeChartOutcomes(city.Probability ?? 0d, $"Seed {seed}: {city.Name}"));
                Assert.InRange(cities.Sum(city => city.Probability ?? 0d), 99.999999d, 100.000001d);
            }
        }
    }

    [Fact]
    public void GenerateRandomCityProbabilities_RepeatsVaryTheOddsBetweenGames()
    {
        var map = LoadStandardMap();

        var first = MapProbabilityService.GenerateRandomCityProbabilities(map, new Random(1)).Select(entry => entry.Probability).ToList();
        var second = MapProbabilityService.GenerateRandomCityProbabilities(map, new Random(2)).Select(entry => entry.Probability).ToList();

        Assert.NotEqual(first, second);
        // 7 regions on 22 slots: repeats push some regions well above others.
        var regionOdds = MapProbabilityService.ApplyCityProbabilities(map, MapProbabilityService.GenerateRandomCityProbabilities(map, new Random(1)))
            .Regions.Select(region => region.Probability ?? 0d).ToList();
        Assert.True(regionOdds.Max() > regionOdds.Min());
    }

    [Fact]
    public void GenerateRandomCityProbabilities_RegionWithMoreCitiesThanSlots_Throws()
    {
        var map = CreateLargeProbabilityMap(23);

        var exception = Assert.Throws<InvalidOperationException>(() => MapProbabilityService.GenerateRandomCityProbabilities(map, new Random(1)));

        Assert.Contains("23 cities in region TR", exception.Message, StringComparison.Ordinal);
    }

    private static void AssertWholeChartOutcomes(double percent, string what)
    {
        var outcomes = percent * 72d / 100d;
        Assert.True(outcomes >= 1d - 0.000001d, $"{what}: {percent}% is below one chart slot.");
        Assert.True(Math.Abs(outcomes - Math.Round(outcomes)) < 0.000001d, $"{what}: {percent}% is not a whole number of the 72 outcomes.");
    }

    private static global::Boxcars.Engine.Data.Maps.MapDefinition LoadStandardMap()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(dir, "src", "Boxcars", "U21MAP.RB3");
            if (File.Exists(candidate))
            {
                return global::Boxcars.Engine.Data.Maps.MapDefinition.Parse(File.ReadAllText(candidate)).Definition!;
            }

            dir = Directory.GetParent(dir)?.FullName ?? dir;
        }

        throw new FileNotFoundException("U21MAP.RB3 was not found above the test output folder.");
    }

    [Fact]
    public void ApplyCityProbabilities_RollsCityProbabilitiesIntoTheirRegions()
    {
        var map = GameEngineFixture.CreateTestMap();
        var overrides = new[]
        {
            new CityProbabilityOverride { CityName = "New York", RegionCode = "NE", Probability = 10.0 },
            new CityProbabilityOverride { CityName = "Boston", RegionCode = "NE", Probability = 20.0 },
            new CityProbabilityOverride { CityName = "Miami", RegionCode = "SE", Probability = 30.0 },
            new CityProbabilityOverride { CityName = "Atlanta", RegionCode = "SE", Probability = 40.0 }
        };

        var updated = MapProbabilityService.ApplyCityProbabilities(map, overrides);

        Assert.InRange(updated.Cities.Single(city => city.Name == "New York").Probability ?? 0d, 33.333332d, 33.333334d);
        Assert.InRange(updated.Cities.Single(city => city.Name == "Boston").Probability ?? 0d, 66.666665d, 66.666668d);
        Assert.InRange(updated.Cities.Single(city => city.Name == "Miami").Probability ?? 0d, 42.857142d, 42.857144d);
        Assert.InRange(updated.Cities.Single(city => city.Name == "Atlanta").Probability ?? 0d, 57.142856d, 57.142858d);
        Assert.InRange(updated.Regions.Single(region => region.Code == "NE").Probability ?? 0d, 29.999999d, 30.000001d);
        Assert.InRange(updated.Regions.Single(region => region.Code == "SE").Probability ?? 0d, 69.999999d, 70.000001d);
    }

    [Fact]
    public void ApplyCityProbabilities_NormalizesOverridePayloadToOneHundredPercent()
    {
        var map = GameEngineFixture.CreateTestMap();
        var overrides = new[]
        {
            new CityProbabilityOverride { CityName = "New York", RegionCode = "NE", Probability = 5 },
            new CityProbabilityOverride { CityName = "Boston", RegionCode = "NE", Probability = 5 },
            new CityProbabilityOverride { CityName = "Miami", RegionCode = "SE", Probability = 10 },
            new CityProbabilityOverride { CityName = "Atlanta", RegionCode = "SE", Probability = 20 }
        };

        var updated = MapProbabilityService.ApplyCityProbabilities(map, overrides);
        var totalProbability = updated.Cities.Sum(city =>
        {
            var regionProbability = updated.Regions.Single(region => string.Equals(region.Code, city.RegionCode, StringComparison.OrdinalIgnoreCase)).Probability ?? 0d;
            return regionProbability * (city.Probability ?? 0d) / 100d;
        });

        Assert.InRange(totalProbability, 99.999999d, 100.000001d);
        Assert.All(updated.Cities, city => Assert.True((city.Probability ?? 0d) > 0d));
    }

    private static global::Boxcars.Engine.Data.Maps.MapDefinition CreateLargeProbabilityMap(int cityCount)
    {
        var map = new global::Boxcars.Engine.Data.Maps.MapDefinition
        {
            Name = "Probability Spread Test",
            Version = "1.0"
        };

        map.Regions.Add(new global::Boxcars.Engine.Data.Maps.RegionDefinition
        {
            Index = 0,
            Name = "Test Region",
            Code = "TR",
            Probability = 100d
        });

        for (var index = 0; index < cityCount; index++)
        {
            map.Cities.Add(new global::Boxcars.Engine.Data.Maps.CityDefinition
            {
                Name = $"City {index}",
                RegionCode = "TR",
                Probability = 100d / cityCount,
                PayoutIndex = index + 1,
                MapDotIndex = index
            });

            map.TrainDots.Add(new global::Boxcars.Engine.Data.Maps.TrainDot
            {
                Id = $"0:{index}",
                RegionIndex = 0,
                DotIndex = index,
                X = index * 10,
                Y = 0
            });
        }

        return map;
    }
}
