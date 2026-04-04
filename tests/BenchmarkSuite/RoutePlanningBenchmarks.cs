using System.Reflection;
using BenchmarkDotNet.Attributes;
using Boxcars.Engine;
using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Domain;
using Microsoft.VSDiagnostics;

namespace BenchmarkSuite1;

[CPUUsageDiagnoser]
public class RoutePlanningBenchmarks
{
    private GameEngine _engine = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var map = LoadMapDefinition();
        _engine = new GameEngine(map, ["Player 1", "Player 2", "Player 3", "Player 4", "Player 5"], new DefaultRandomProvider(123));

        AssignRailroadsRoundRobin();
        ConfigureSeattleToMiamiScenario(map);
    }

    [Benchmark]
    public object PlanSeattleToMiami()
    {
        return _engine.SuggestRouteForPlayer(0);
    }

    private void AssignRailroadsRoundRobin()
    {
        for (var index = 0; index < _engine.Railroads.Count; index++)
        {
            var railroad = _engine.Railroads[index];
            var owner = _engine.Players[index % _engine.Players.Count];
            SetProperty(railroad, nameof(Railroad.Owner), owner);
            owner.OwnedRailroads.Add(railroad);
        }
    }

    private void ConfigureSeattleToMiamiScenario(MapDefinition map)
    {
        var player = _engine.Players[0];
        var seattle = map.Cities.First(city => string.Equals(city.Name, "Seattle", StringComparison.OrdinalIgnoreCase));
        var miami = map.Cities.First(city => string.Equals(city.Name, "Miami", StringComparison.OrdinalIgnoreCase));
        var seattleRegionIndex = map.Regions.First(region => string.Equals(region.Code, seattle.RegionCode, StringComparison.OrdinalIgnoreCase)).Index;

        SetProperty(player, nameof(Player.CurrentCity), seattle);
        SetProperty(player, nameof(Player.Destination), miami);
        SetProperty(player, nameof(Player.TripOriginCity), seattle);
        SetProperty(player, "CurrentNodeId", $"{seattleRegionIndex}:{seattle.MapDotIndex}");
    }

    private static MapDefinition LoadMapDefinition()
    {
        var solutionRoot = FindSolutionRoot(AppContext.BaseDirectory);
        var mapPath = Path.Combine(solutionRoot, "src", "Boxcars", "U21MAP.RB3");
        var rawMap = File.ReadAllText(mapPath);
        var loadResult = MapDefinition.Parse(rawMap);
        if (!loadResult.Succeeded || loadResult.Definition is null)
        {
            throw new InvalidOperationException($"Unable to load benchmark map from '{mapPath}'.");
        }

        return loadResult.Definition;
    }

    private static string FindSolutionRoot(string startDirectory)
    {
        var current = new DirectoryInfo(startDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Boxcars.sln"))
                || Directory.Exists(Path.Combine(current.FullName, "src", "Boxcars")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate the Boxcars solution root for benchmark setup.");
    }

    private static void SetProperty(object target, string propertyName, object? value)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Property '{propertyName}' was not found on '{target.GetType().Name}'.");
        property.SetValue(target, value);
    }
}
