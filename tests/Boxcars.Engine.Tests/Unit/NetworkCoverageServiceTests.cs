using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Tests.Fixtures;
using Boxcars.Services;

namespace Boxcars.Engine.Tests.Unit;

public class NetworkCoverageServiceTests
{
    [Fact]
    public async Task BuildProjectedSnapshot_OverlapUsesRealDeltaInsteadOfNaiveAddition()
    {
        var mapPath = FindStandardMapPath();
        if (mapPath is null)
        {
            return;
        }

        await using var stream = File.OpenRead(mapPath);
        var result = await MapDefinition.LoadAsync(Path.GetFileName(mapPath), stream);

        Assert.True(result.Succeeded, $"Map load failed: {string.Join(", ", result.Errors)}");

        var mapDefinition = result.Definition!;
        var service = new NetworkCoverageService();
        var foundOverlapCase = false;

        for (var ownedIndex = 0; ownedIndex < mapDefinition.Railroads.Count && !foundOverlapCase; ownedIndex++)
        {
            var currentCoverage = service.BuildSnapshot(mapDefinition, [ownedIndex]);

            for (var candidateIndex = 0; candidateIndex < mapDefinition.Railroads.Count; candidateIndex++)
            {
                if (candidateIndex == ownedIndex)
                {
                    continue;
                }

                var candidateCoverage = service.BuildSnapshot(mapDefinition, [candidateIndex]);
                var projectedCoverage = service.BuildProjectedSnapshot(mapDefinition, [ownedIndex], candidateIndex);
                var actualDelta = Math.Round(projectedCoverage.AccessibleDestinationPercent - currentCoverage.AccessibleDestinationPercent, 1, MidpointRounding.AwayFromZero);

                if (actualDelta > 0m && actualDelta < candidateCoverage.AccessibleDestinationPercent)
                {
                    foundOverlapCase = true;
                    Assert.True(actualDelta < candidateCoverage.AccessibleDestinationPercent,
                        $"Owned railroad {ownedIndex} and candidate railroad {candidateIndex} should demonstrate overlap. Actual delta {actualDelta:N1}% must be less than naive candidate-only access {candidateCoverage.AccessibleDestinationPercent:N1}%.");
                    break;
                }
            }
        }

        Assert.True(foundOverlapCase, "Expected to find at least one overlapping railroad pair where projected access gain is smaller than naive candidate-only access.");
    }

    [Fact]
    public void BuildProjectedSnapshotAfterSale_RemovesOwnedCoverage()
    {
        var mapDefinition = GameEngineFixture.CreateTestMap();
        var service = new NetworkCoverageService();

        var currentCoverage = service.BuildSnapshot(mapDefinition, [0, 1]);
        var projectedCoverage = service.BuildProjectedSnapshotAfterSale(mapDefinition, [0, 1], 0);
        var expectedCoverage = service.BuildSnapshot(mapDefinition, [1]);

        Assert.True(projectedCoverage.AccessibleCityPercent < currentCoverage.AccessibleCityPercent);
        Assert.Equal(expectedCoverage.AccessibleCityPercent, projectedCoverage.AccessibleCityPercent);
        Assert.Equal(expectedCoverage.MonopolyCityPercent, projectedCoverage.MonopolyCityPercent);
    }

    [Fact]
    public void BuildSnapshotIncludingPublicRailroads_TreatsUnownedRailroadsAsAccessible()
    {
        var mapDefinition = GameEngineFixture.CreateTestMap();
        var service = new NetworkCoverageService();

        var coverage = service.BuildSnapshotIncludingPublicRailroads(mapDefinition, [], []);

        Assert.Equal(100m, coverage.AccessibleCityPercent);
        Assert.All(coverage.RegionAccess, region => Assert.True(region.AccessibleDestinationPercent > 0m));
        Assert.Equal(0m, coverage.MonopolyCityPercent);
        Assert.Equal(0m, coverage.MonopolyDestinationPercent);
    }

    [Fact]
    public void FindMonopolyCityNodes_PocketBehindTheOwnersRailroad_IsAMonopoly()
    {
        // Main line A-B-X; the gateway runs X-P1, and the branch P1-P2 joins the map only through it.
        var mapDefinition = CreatePocketMap();

        Assert.Equal(["0:3", "0:4"], NetworkCoverageService.FindMonopolyCityNodes(mapDefinition, [Gateway]).Order());
        // The branch alone leaves P1 reachable on the gateway; only P2, which nothing else serves, is its monopoly.
        Assert.Equal(["0:4"], NetworkCoverageService.FindMonopolyCityNodes(mapDefinition, [Branch]));
        Assert.Equal(40m, new NetworkCoverageService().BuildSnapshot(mapDefinition, [Gateway]).MonopolyCityPercent);
    }

    [Fact]
    public void FindMonopolyCityNodes_OwningEveryRailroad_IsAMonopolyOnEveryCity()
    {
        var mapDefinition = CreatePocketMap();

        Assert.Equal(5, NetworkCoverageService.FindMonopolyCityNodes(mapDefinition, [MainLine, Gateway, Branch]).Count);
    }

    [Fact]
    public async Task FindMonopolyCityNodes_NewYorkCentralAndNewHaven_TollNewEngland()
    {
        var mapPath = FindStandardMapPath();
        if (mapPath is null)
        {
            return;
        }

        await using var stream = File.OpenRead(mapPath);
        var mapDefinition = (await MapDefinition.LoadAsync(Path.GetFileName(mapPath), stream)).Definition!;
        int RailroadIndex(string shortName) => mapDefinition.Railroads.Single(railroad => railroad.ShortName == shortName).Index;
        string CityNode(string name)
        {
            var city = mapDefinition.Cities.Single(candidate => candidate.Name == name);
            var region = mapDefinition.Regions.Single(candidate => candidate.Code == city.RegionCode);
            return $"{region.Index}:{city.MapDotIndex}";
        }

        var monopolies = NetworkCoverageService.FindMonopolyCityNodes(mapDefinition, [RailroadIndex("NYC"), RailroadIndex("NYNH&H")]);

        // The Boston and Maine still serves these, but it reaches the rest of the map only on those two.
        Assert.Contains(CityNode("Portland ME"), monopolies);
        Assert.Contains(CityNode("Boston"), monopolies);
        Assert.Contains(CityNode("Albany"), monopolies);
        Assert.DoesNotContain(CityNode("Chicago"), monopolies);
    }

    private const int MainLine = 0;
    private const int Gateway = 1;
    private const int Branch = 2;

    private static MapDefinition CreatePocketMap()
    {
        var map = new MapDefinition { Name = "Pocket", Version = "1.0" };
        map.Regions.Add(new RegionDefinition { Index = 0, Name = "Only", Code = "ON", Probability = 1 });
        string[] names = ["A", "B", "X", "P1", "P2"];
        for (var dot = 0; dot < names.Length; dot++)
        {
            map.Cities.Add(new CityDefinition { Name = names[dot], RegionCode = "ON", Probability = 20, MapDotIndex = dot });
            map.TrainDots.Add(new TrainDot { Id = $"0:{dot}", RegionIndex = 0, DotIndex = dot, X = dot * 10, Y = 0 });
        }

        foreach (var (railroad, name) in new[] { (MainLine, "Main Line"), (Gateway, "Gateway"), (Branch, "Branch") })
        {
            map.Railroads.Add(new RailroadDefinition { Index = railroad, Name = name, ShortName = name });
        }

        foreach (var (railroad, from, to) in new[] { (MainLine, 0, 1), (MainLine, 1, 2), (Gateway, 2, 3), (Branch, 3, 4) })
        {
            map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition
            {
                RailroadIndex = railroad,
                StartRegionIndex = 0,
                StartDotIndex = from,
                EndRegionIndex = 0,
                EndDotIndex = to
            });
        }

        return map;
    }

    private static string? FindStandardMapPath()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(dir, "src", "Boxcars", "U21MAP.RB3");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            var parent = Directory.GetParent(dir);
            if (parent is null)
            {
                break;
            }

            dir = parent.FullName;
        }

        return null;
    }
}
