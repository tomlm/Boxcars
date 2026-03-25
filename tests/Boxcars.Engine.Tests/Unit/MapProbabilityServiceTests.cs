using Boxcars.Engine.Tests.Fixtures;
using global::Boxcars.Data;
using global::Boxcars.Services.Maps;

namespace Boxcars.Engine.Tests.Unit;

public class MapProbabilityServiceTests
{
    [Fact]
    public void GenerateRandomCityProbabilities_AssignsEveryCityANonZeroProbability_AndSumsToOneHundredPercent()
    {
        var map = GameEngineFixture.CreateTestMap();

        var overrides = MapProbabilityService.GenerateRandomCityProbabilities(map, new Random(1234));

        Assert.Equal(map.Cities.Count, overrides.Count);
        Assert.All(overrides, entry => Assert.True(entry.Probability >= 0.1d));
        Assert.InRange(overrides.Sum(entry => entry.Probability), 99.999999d, 100.000001d);
    }

    [Fact]
    public void GenerateRandomCityProbabilities_ProducesABroaderDistribution()
    {
        var map = CreateLargeProbabilityMap(40);

        var overrides = MapProbabilityService.GenerateRandomCityProbabilities(map, new Random(1234));
        var minProbability = overrides.Min(entry => entry.Probability);
        var maxProbability = overrides.Max(entry => entry.Probability);

        Assert.True(minProbability >= 0.1d, $"Expected min probability to respect the 0.1% floor, got {minProbability:N3}%.");
        Assert.True(maxProbability <= 5d, $"Expected generated probabilities to honor the 5% cap, got {maxProbability:N3}%.");
        Assert.True((maxProbability - minProbability) >= 3.5d, $"Expected a broader spread than the near-uniform distribution. Min={minProbability:N3}%, Max={maxProbability:N3}%.");
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
