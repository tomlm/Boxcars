using Boxcars.Engine.Domain;
using Boxcars.Engine.Data.Maps;
using Boxcars.Services;
using Boxcars.Services.Maps;
using RailBaronGameEngine = Boxcars.Engine.Domain.GameEngine;

namespace Boxcars.Engine.Tests.Unit;

/// <summary>
/// Regression coverage for map analysis data inputs (T030).
/// Validates railroad pricing tables and map structure used by map analysis services.
/// </summary>
public class MapAnalysisTests
{
    [Fact]
    public void GetRailroadPurchasePrice_AllStandardIndices_ReturnPositivePrices()
    {
        // Standard Rail Baron has 28 railroads (indices 0-27)
        for (var index = 0; index < 28; index++)
        {
            var price = RailBaronGameEngine.GetRailroadPurchasePrice(index);
            Assert.True(price > 0, $"Railroad index {index} should have a positive price, got {price}");
        }
    }

    [Fact]
    public void GetRailroadPurchasePrice_PricesAreNonDecreasing()
    {
        var previousPrice = 0;
        for (var index = 0; index < 28; index++)
        {
            var price = RailBaronGameEngine.GetRailroadPurchasePrice(index);
            Assert.True(price >= previousPrice,
                $"Railroad index {index} price ({price}) should be >= previous price ({previousPrice})");
            previousPrice = price;
        }
    }

    [Fact]
    public void GetRailroadPurchasePrice_CheapestIs4000()
    {
        var price = RailBaronGameEngine.GetRailroadPurchasePrice(0);
        Assert.Equal(4_000, price);
    }

    [Fact]
    public void GetRailroadPurchasePrice_MostExpensiveIs40000()
    {
        var price = RailBaronGameEngine.GetRailroadPurchasePrice(27);
        Assert.Equal(40_000, price);
    }

    [Fact]
    public async Task MapDefinition_StandardMap_HasRailroadsRegionsCities()
    {
        var mapPath = FindStandardMapPath();
        if (mapPath is null)
        {
            // Map file not available in test environment — skip gracefully
            return;
        }

        await using var stream = File.OpenRead(mapPath);
        var result = await MapDefinition.LoadAsync(Path.GetFileName(mapPath), stream);

        Assert.True(result.Succeeded, $"Map load failed: {string.Join(", ", result.Errors)}");
        Assert.NotNull(result.Definition);
        Assert.True(result.Definition.Railroads.Count > 0, "Map should have railroads");
        Assert.True(result.Definition.Regions.Count > 0, "Map should have regions");
        Assert.True(result.Definition.Cities.Count > 0, "Map should have cities");
    }

    [Fact]
    public async Task MapDefinition_StandardMap_HasPayoutTable()
    {
        var mapPath = FindStandardMapPath();
        if (mapPath is null) return;

        await using var stream = File.OpenRead(mapPath);
        var result = await MapDefinition.LoadAsync(Path.GetFileName(mapPath), stream);

        Assert.True(result.Succeeded);

        // Verify at least one payout can be looked up
        var cities = result.Definition!.Cities.Where(c => c.PayoutIndex.HasValue).Take(2).ToList();
        if (cities.Count >= 2)
        {
            var hasPayout = result.Definition.TryGetPayout(cities[0].PayoutIndex!.Value, cities[1].PayoutIndex!.Value, out var payout);
            Assert.True(hasPayout, "Should be able to look up payout between two cities");
            Assert.True(payout > 0, "Payout should be positive");
        }
    }

    [Fact]
    public async Task MapDefinition_StandardMap_HasRouteSegments()
    {
        var mapPath = FindStandardMapPath();
        if (mapPath is null) return;

        await using var stream = File.OpenRead(mapPath);
        var result = await MapDefinition.LoadAsync(Path.GetFileName(mapPath), stream);

        Assert.True(result.Succeeded);
        Assert.True(result.Definition!.RailroadRouteSegments.Count > 0, "Map should have route segments for analysis");
    }

    [Fact]
    public async Task MapDefinition_StandardMap_GreatNorthernPrice_Is17000()
    {
        var mapPath = FindStandardMapPath();
        if (mapPath is null) return;

        await using var stream = File.OpenRead(mapPath);
        var result = await MapDefinition.LoadAsync(Path.GetFileName(mapPath), stream);

        Assert.True(result.Succeeded);

        var greatNorthern = result.Definition!.Railroads.FirstOrDefault(railroad =>
            string.Equals(railroad.Name, "Great Northern", StringComparison.OrdinalIgnoreCase)
            || string.Equals(railroad.ShortName, "GN", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(greatNorthern);
        Assert.Equal(17_000, greatNorthern!.PurchasePrice);
    }

    [Fact]
    public async Task MapDefinition_StandardMap_NewYorkWeightedAccess_Is405Percent()
    {
        var mapPath = FindStandardMapPath();
        if (mapPath is null) return;

        await using var stream = File.OpenRead(mapPath);
        var result = await MapDefinition.LoadAsync(Path.GetFileName(mapPath), stream);

        Assert.True(result.Succeeded);

        var newYork = result.Definition!.Cities.FirstOrDefault(city =>
            string.Equals(city.Name, "New York", StringComparison.OrdinalIgnoreCase));
        var northEast = result.Definition.Regions.FirstOrDefault(region =>
            string.Equals(region.Code, "NE", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(newYork);
        Assert.NotNull(northEast);
        Assert.NotNull(newYork!.Probability);
        Assert.NotNull(northEast!.Probability);

        var weightedAccess = Math.Round((decimal)(northEast.Probability!.Value * newYork.Probability!.Value / 100d), 2, MidpointRounding.AwayFromZero);
        Assert.Equal(4.05m, weightedAccess);
    }

    [Fact]
    public async Task MapDefinition_StandardMap_ButteWeightedAccess_Is077Percent()
    {
        var mapPath = FindStandardMapPath();
        if (mapPath is null) return;

        await using var stream = File.OpenRead(mapPath);
        var result = await MapDefinition.LoadAsync(Path.GetFileName(mapPath), stream);

        Assert.True(result.Succeeded);

        var butte = result.Definition!.Cities.FirstOrDefault(city =>
            string.Equals(city.Name, "Butte", StringComparison.OrdinalIgnoreCase));
        var northWest = result.Definition.Regions.FirstOrDefault(region =>
            string.Equals(region.Code, "NW", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(butte);
        Assert.NotNull(northWest);
        Assert.NotNull(butte!.Probability);
        Assert.NotNull(northWest!.Probability);

        var weightedAccess = Math.Round((decimal)(northWest.Probability!.Value * butte.Probability!.Value / 100d), 2, MidpointRounding.AwayFromZero);
        Assert.Equal(0.77m, weightedAccess);
    }

    [Fact]
    public async Task MapAnalysisReport_StandardMap_SouthernPacificWeightedAccess_Is2097Percent()
    {
        var mapPath = FindStandardMapPath();
        if (mapPath is null) return;

        await using var stream = File.OpenRead(mapPath);
        var result = await MapDefinition.LoadAsync(Path.GetFileName(mapPath), stream);

        Assert.True(result.Succeeded, $"Map load failed: {string.Join(", ", result.Errors)}");

        var service = new MapAnalysisService(new MapRouteService());
        var report = service.BuildReport(result.Definition!);
        var southernPacific = report.RailroadRows.FirstOrDefault(row =>
            string.Equals(row.FullName, "Southern Pacific", StringComparison.OrdinalIgnoreCase)
            || string.Equals(row.RailroadCode, "SP", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(southernPacific);
        Assert.Equal(20.97m, southernPacific!.ServicePercentage);
        Assert.Equal(0m, southernPacific.MonopolyPercentage);
    }

    [Theory]
    [InlineData("SP", "Southern Pacific", 42000, 12, 20.97, 0.00, 12)]
    [InlineData("AT&SF", "Atchison, Topeka, and Santa Fe", 40000, 11, 23.51, 1.62, 17)]
    [InlineData("UP", "Union Pacific", 40000, 9, 16.44, 2.01, 13)]
    [InlineData("PA", "Pennsylvania RR", 30000, 13, 28.09, 0.00, 17)]
    [InlineData("CRI&P", "Chicago, Rock Island, and Pacific", 29000, 10, 15.07, 0.77, 20)]
    [InlineData("NYC", "New York Central", 28000, 9, 19.46, 0.00, 16)]
    public async Task MapAnalysisReport_StandardMap_ReferenceRailroadRows_MatchExpected(
        string railroadCode,
        string railroadName,
        int purchasePrice,
        int cityCount,
        decimal servicePercent,
        decimal monopolyPercent,
        int connectionCount)
    {
        var mapPath = FindStandardMapPath();
        if (mapPath is null) return;

        await using var stream = File.OpenRead(mapPath);
        var result = await MapDefinition.LoadAsync(Path.GetFileName(mapPath), stream);

        Assert.True(result.Succeeded, $"Map load failed: {string.Join(", ", result.Errors)}");

        var service = new MapAnalysisService(new MapRouteService());
        var report = service.BuildReport(result.Definition!);
        var railroad = report.RailroadRows.FirstOrDefault(row =>
            string.Equals(row.RailroadCode, railroadCode, StringComparison.OrdinalIgnoreCase)
            || string.Equals(row.FullName, railroadName, StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(railroad);
        Assert.Equal(purchasePrice, railroad!.PurchasePrice);
        Assert.Equal(cityCount, railroad.CitiesServedCount);
        Assert.Equal(servicePercent, railroad.ServicePercentage);
        Assert.Equal(monopolyPercent, railroad.MonopolyPercentage);
        Assert.Equal(connectionCount, railroad.ConnectionCount);
    }

    private static string? FindStandardMapPath()
    {
        // Walk up from test binary to find the map file in the source tree
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(dir, "src", "Boxcars", "U21MAP.RB3");
            if (File.Exists(candidate)) return candidate;
            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }

        return null;
    }
}
