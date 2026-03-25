using Boxcars.Engine.Data.Maps;
using global::Boxcars.Data;
using global::Boxcars.Services.Maps;

namespace Boxcars.Engine.Tests.Unit;

public class MapRailroadPricingServiceTests
{
    [Fact]
    public void CalculatePurchasePrices_ReturnsPositiveRoundedPricesForEveryRailroad()
    {
        var map = CreateProbabilityShiftPricingMap();

        var prices = MapRailroadPricingService.CalculatePurchasePrices(map);

        Assert.Equal(map.Railroads.Count, prices.Count);
        Assert.All(prices.Values, price =>
        {
            Assert.InRange(price, 2_000, 65_000);
            Assert.Equal(0, price % 500);
        });
    }

    [Fact]
    public void CalculatePurchasePrices_ScalesResultsIntoExpectedGameRange()
    {
        var map = CreateProbabilityShiftPricingMap();

        var prices = MapRailroadPricingService.CalculatePurchasePrices(map).Values.OrderBy(price => price).ToArray();

        Assert.Equal(2_000, prices.First());
        Assert.InRange(prices.Last(), 50_000, 65_000);
    }

    [Fact]
    public void CalculatePurchasePrices_PrefersRailroadThatControlsHighProbabilityDirectRoute()
    {
        var map = CreateChokePointPricingMap();

        var prices = MapRailroadPricingService.CalculatePurchasePrices(map);

        Assert.True(prices[0] > prices[1], $"Expected direct choke-point railroad to price above alternate route. RR0={prices[0]}, RR1={prices[1]}");
    }

    [Fact]
    public void ApplyCityProbabilities_RecalculatesRailroadPricesFromUpdatedCityDistribution()
    {
        var map = CreateProbabilityShiftPricingMap();
        var overrides = new[]
        {
            new CityProbabilityOverride { CityName = "Alpha", RegionCode = "N", Probability = 0.05d },
            new CityProbabilityOverride { CityName = "Beta", RegionCode = "N", Probability = 0.05d },
            new CityProbabilityOverride { CityName = "Gamma", RegionCode = "N", Probability = 0.80d },
            new CityProbabilityOverride { CityName = "Delta", RegionCode = "N", Probability = 0.10d }
        };

        var updated = MapProbabilityService.ApplyCityProbabilities(map, overrides);
        var railroadZeroPrice = updated.Railroads.Single(railroad => railroad.Index == 0).PurchasePrice;
        var railroadOnePrice = updated.Railroads.Single(railroad => railroad.Index == 1).PurchasePrice;

        Assert.NotNull(railroadZeroPrice);
        Assert.NotNull(railroadOnePrice);
        Assert.True(railroadOnePrice > railroadZeroPrice, $"Expected Gamma-heavy probabilities to favor the alternate railroad. RR0={railroadZeroPrice}, RR1={railroadOnePrice}");
    }

    private static MapDefinition CreateChokePointPricingMap()
    {
        var map = new MapDefinition
        {
            Name = "Choke Point Pricing Test Map",
            Version = "1.0"
        };

        map.Regions.Add(new RegionDefinition
        {
            Index = 0,
            Name = "North",
            Code = "N",
            Probability = 1d
        });

        map.Cities.Add(new CityDefinition
        {
            Name = "Alpha",
            RegionCode = "N",
            Probability = 0.45d,
            PayoutIndex = 1,
            MapDotIndex = 0
        });
        map.Cities.Add(new CityDefinition
        {
            Name = "Beta",
            RegionCode = "N",
            Probability = 0.45d,
            PayoutIndex = 2,
            MapDotIndex = 1
        });
        map.Cities.Add(new CityDefinition
        {
            Name = "Gamma",
            RegionCode = "N",
            Probability = 0.05d,
            PayoutIndex = 3,
            MapDotIndex = 2
        });
        map.Cities.Add(new CityDefinition
        {
            Name = "Delta",
            RegionCode = "N",
            Probability = 0.05d,
            PayoutIndex = 4,
            MapDotIndex = 3
        });

        map.Railroads.Add(new RailroadDefinition
        {
            Index = 0,
            Name = "Direct",
            ShortName = "DIR"
        });
        map.Railroads.Add(new RailroadDefinition
        {
            Index = 1,
            Name = "Scenic",
            ShortName = "SCN"
        });

        map.TrainDots.Add(new TrainDot { Id = "0:0", RegionIndex = 0, DotIndex = 0, X = 0, Y = 0 });
        map.TrainDots.Add(new TrainDot { Id = "0:1", RegionIndex = 0, DotIndex = 1, X = 10, Y = 0 });
        map.TrainDots.Add(new TrainDot { Id = "0:2", RegionIndex = 0, DotIndex = 2, X = 5, Y = 10 });
        map.TrainDots.Add(new TrainDot { Id = "0:3", RegionIndex = 0, DotIndex = 3, X = 15, Y = 10 });

        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition
        {
            RailroadIndex = 0,
            StartRegionIndex = 0,
            StartDotIndex = 0,
            EndRegionIndex = 0,
            EndDotIndex = 1
        });
        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition
        {
            RailroadIndex = 0,
            StartRegionIndex = 0,
            StartDotIndex = 1,
            EndRegionIndex = 0,
            EndDotIndex = 2
        });
        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition
        {
            RailroadIndex = 1,
            StartRegionIndex = 0,
            StartDotIndex = 2,
            EndRegionIndex = 0,
            EndDotIndex = 1
        });
        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition
        {
            RailroadIndex = 1,
            StartRegionIndex = 0,
            StartDotIndex = 2,
            EndRegionIndex = 0,
            EndDotIndex = 3
        });

        return map;
    }

    private static MapDefinition CreateProbabilityShiftPricingMap()
    {
        var map = new MapDefinition
        {
            Name = "Probability Shift Pricing Test Map",
            Version = "1.0"
        };

        map.Regions.Add(new RegionDefinition
        {
            Index = 0,
            Name = "North",
            Code = "N",
            Probability = 1d
        });

        map.Cities.Add(new CityDefinition
        {
            Name = "Alpha",
            RegionCode = "N",
            Probability = 0.45d,
            PayoutIndex = 1,
            MapDotIndex = 0
        });
        map.Cities.Add(new CityDefinition
        {
            Name = "Beta",
            RegionCode = "N",
            Probability = 0.45d,
            PayoutIndex = 2,
            MapDotIndex = 1
        });
        map.Cities.Add(new CityDefinition
        {
            Name = "Gamma",
            RegionCode = "N",
            Probability = 0.05d,
            PayoutIndex = 3,
            MapDotIndex = 2
        });
        map.Cities.Add(new CityDefinition
        {
            Name = "Delta",
            RegionCode = "N",
            Probability = 0.05d,
            PayoutIndex = 4,
            MapDotIndex = 3
        });

        map.Railroads.Add(new RailroadDefinition
        {
            Index = 0,
            Name = "Direct",
            ShortName = "DIR"
        });
        map.Railroads.Add(new RailroadDefinition
        {
            Index = 1,
            Name = "Scenic",
            ShortName = "SCN"
        });

        map.TrainDots.Add(new TrainDot { Id = "0:0", RegionIndex = 0, DotIndex = 0, X = 0, Y = 0 });
        map.TrainDots.Add(new TrainDot { Id = "0:1", RegionIndex = 0, DotIndex = 1, X = 10, Y = 0 });
        map.TrainDots.Add(new TrainDot { Id = "0:2", RegionIndex = 0, DotIndex = 2, X = 5, Y = 10 });
        map.TrainDots.Add(new TrainDot { Id = "0:3", RegionIndex = 0, DotIndex = 3, X = 15, Y = 10 });

        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition
        {
            RailroadIndex = 0,
            StartRegionIndex = 0,
            StartDotIndex = 0,
            EndRegionIndex = 0,
            EndDotIndex = 1
        });
        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition
        {
            RailroadIndex = 1,
            StartRegionIndex = 0,
            StartDotIndex = 0,
            EndRegionIndex = 0,
            EndDotIndex = 2
        });
        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition
        {
            RailroadIndex = 1,
            StartRegionIndex = 0,
            StartDotIndex = 2,
            EndRegionIndex = 0,
            EndDotIndex = 1
        });
        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition
        {
            RailroadIndex = 1,
            StartRegionIndex = 0,
            StartDotIndex = 2,
            EndRegionIndex = 0,
            EndDotIndex = 3
        });

        return map;
    }
}
