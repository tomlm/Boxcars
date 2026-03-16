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
