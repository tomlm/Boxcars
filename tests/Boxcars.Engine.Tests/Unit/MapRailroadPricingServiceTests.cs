using Boxcars.Engine.Data.Maps;
using global::Boxcars.Data;
using global::Boxcars.Services.Maps;

namespace Boxcars.Engine.Tests.Unit;

public class MapRailroadPricingServiceTests
{
    [Theory]
    // Income and price pairs published in RBP reports for U21.
    [InlineData(16_852d, 63_000)]
    [InlineData(14_470d, 54_000)]
    [InlineData(638d, 2_000)]
    [InlineData(560d, 2_000)]
    [InlineData(277d, 1_000)]
    [InlineData(59d, 0)]
    public void PriceFromIncome_MatchesRbpPriceRule(double income, int expectedPrice)
    {
        Assert.Equal(expectedPrice, MapRailroadPricingService.PriceFromIncome(income));
    }

    [Fact]
    public void CalculatePurchasePrices_PricesEachRailroadFromItsIncomeAboveTheMinimum()
    {
        var map = CreateProbabilityShiftPricingMap();

        var prices = MapRailroadPricingService.CalculatePurchasePrices(map, minimumRailroadPrice: 4_000);
        var incomes = MapRailroadPricingService.CalculateEstimatedIncome(map);

        Assert.Equal(map.Railroads.Count, prices.Count);
        Assert.All(map.Railroads, railroad =>
        {
            Assert.Equal(Math.Max(4_000, MapRailroadPricingService.PriceFromIncome(incomes[railroad.Index])), prices[railroad.Index]);
            Assert.Equal(0, prices[railroad.Index] % 500);
        });
    }

    [Fact]
    public void CalculatePurchasePrices_MinimumRailroadPriceIsAFloor()
    {
        var map = CreateMonopolySpurPricingMap();

        var unfloored = MapRailroadPricingService.CalculatePurchasePrices(map, minimumRailroadPrice: 0);
        var floor = unfloored.Values.Max();
        var floored = MapRailroadPricingService.CalculatePurchasePrices(map, minimumRailroadPrice: floor);

        // Raising the floor to the most expensive railroad lifts every cheaper one to it.
        Assert.Contains(unfloored.Values, price => price < floor);
        Assert.All(floored.Values, price => Assert.Equal(floor, price));
    }

    [Fact]
    public void CalculatePurchasePrices_PrefersRailroadThatControlsHighProbabilityDirectRoute()
    {
        var map = CreateChokePointPricingMap();

        var prices = MapRailroadPricingService.CalculatePurchasePrices(map);

        Assert.True(prices[0] > prices[1], $"Expected direct choke-point railroad to price above alternate route. RR0={prices[0]}, RR1={prices[1]}");
    }

    [Fact]
    public void CalculatePurchasePrices_DoesNotPriceAMonopolySpurAboveTheBusyTrunkLine()
    {
        var map = CreateMonopolySpurPricingMap();

        var prices = MapRailroadPricingService.CalculatePurchasePrices(map);

        // The spur is the only way into a rarely drawn city; the trunk carries most of the map's traffic.
        Assert.True(prices[0] > prices[2], $"Expected the busy trunk to price above the monopoly spur. Trunk={prices[0]}, Spur={prices[2]}");
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

    private static MapDefinition CreateMonopolySpurPricingMap()
    {
        var map = new MapDefinition
        {
            Name = "Monopoly Spur Pricing Test Map",
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
            Name = "Delta",
            RegionCode = "N",
            Probability = 0.10d,
            PayoutIndex = 3,
            MapDotIndex = 3
        });

        map.Railroads.Add(new RailroadDefinition
        {
            Index = 0,
            Name = "Trunk",
            ShortName = "TRK"
        });
        map.Railroads.Add(new RailroadDefinition
        {
            Index = 1,
            Name = "Bypass",
            ShortName = "BYP"
        });
        map.Railroads.Add(new RailroadDefinition
        {
            Index = 2,
            Name = "Spur",
            ShortName = "SPR"
        });

        map.TrainDots.Add(new TrainDot { Id = "0:0", RegionIndex = 0, DotIndex = 0, X = 0, Y = 0 });
        map.TrainDots.Add(new TrainDot { Id = "0:1", RegionIndex = 0, DotIndex = 1, X = 10, Y = 0 });
        map.TrainDots.Add(new TrainDot { Id = "0:2", RegionIndex = 0, DotIndex = 2, X = 5, Y = 5 });
        map.TrainDots.Add(new TrainDot { Id = "0:3", RegionIndex = 0, DotIndex = 3, X = 20, Y = 0 });

        // Trunk: Alpha -> Beta directly.
        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition
        {
            RailroadIndex = 0,
            StartRegionIndex = 0,
            StartDotIndex = 0,
            EndRegionIndex = 0,
            EndDotIndex = 1
        });

        // Bypass: Alpha -> Beta one dot longer, so the trunk is not a choke point.
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

        // Spur: the only line into Delta.
        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition
        {
            RailroadIndex = 2,
            StartRegionIndex = 0,
            StartDotIndex = 1,
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
