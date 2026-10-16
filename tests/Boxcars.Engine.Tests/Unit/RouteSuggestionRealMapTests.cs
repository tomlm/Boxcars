using System.Runtime.CompilerServices;
using Boxcars.Data.Maps;
using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Domain;
using Boxcars.Engine.Persistence;
using Boxcars.Engine.Tests.TestDoubles;
using Boxcars.Services.Maps;
using Xunit.Abstractions;

namespace Boxcars.Engine.Tests.Unit;

/// <summary>
/// Bot route planning on positions taken from real games on the standard map.
/// </summary>
[Collection(RoutePlanningCollection.Name)]
public class RouteSuggestionRealMapTests(ITestOutputHelper output)
{
    [Fact]
    public void SuggestRoute_LosAngelesToAtlantaWithMostRailroadsOwned_FindsAPricedRoute()
    {
        // All railroads sold. El Cheapo (Superchief) is at Los Angeles with 1 dot of bonus left, has ridden
        // railroad 14 this turn, and is headed to Atlanta, which their SAL serves. The planner used to reject every
        // way into Atlanta and fall back to the fee-blind shortest path, reported as costing $0.
        var engine = CreateEngine(StandardGameOwners);
        var player = PlaceActivePlayer(engine, "El Cheapo", "Los Angeles", "Atlanta", LocomotiveType.Superchief, movementRemaining: 1);
        engine.CurrentTurn.RailroadsRiddenThisTurn.Add(14);

        var route = engine.SuggestRoute();
        WriteRoute(engine, player, route);

        // AT&SF (Pooper) for one turn, then thermous's SP, CRI&P and SOU for two: one owner per turn.
        Assert.Equal(FindCityNode(engine, "Atlanta"), route.NodeIds[^1]);
        Assert.Equal(30_000, route.TotalCost);
    }

    [Fact]
    public void FindCheapestSuggestion_KansasCityToColumbus_StaysOnOwnRailroadsUntilTheLastDot()
    {
        // thermous (Superchief) is on CRI&P west of Kansas City with 8 to move (white 3, red 5), headed to
        // Columbus, which only opponents serve. The suggestion used to exhaust its search budget and fall back to
        // the fee-blind shortest path, hopping onto Pooper's AT&SF for the last dot of the turn.
        var engine = CreateEngine(StandardGameOwners);
        var map = engine.MapDefinition;
        var player = engine.Players.First(candidate => candidate.Name == "thermous");
        // Track already ridden on this trip (from San Diego), which can't be ridden again.
        var traveledSegmentKeys = new[]
        {
            "7:41|7:42|14", "7:42|7:68|14", "7:68|7:43|14", "7:43|7:35|21", "7:35|7:40|21", "7:40|7:39|21",
            "7:39|7:38|21", "7:38|7:37|21", "7:37|7:36|21", "7:36|7:17|21", "7:17|7:15|21", "7:15|7:14|21",
            "7:14|7:7|21", "7:7|7:8|21", "7:8|7:9|21", "7:9|7:2|21", "7:2|4:68|23", "4:68|4:66|11"
        }.Select(NormalizeSegmentKey).ToList();

        RailroadOwnershipCategory Ownership(int railroadIndex)
        {
            var owner = engine.Railroads.First(railroad => railroad.Index == railroadIndex).Owner;
            return owner is null ? RailroadOwnershipCategory.Public
                : owner == player ? RailroadOwnershipCategory.Friendly
                : RailroadOwnershipCategory.Unfriendly;
        }

        var service = new MapRouteService();
        var suggestion = service.FindCheapestSuggestion(
            service.BuildContext(map),
            new RouteSuggestionRequest
            {
                PlayerId = player.Name,
                StartNodeId = "4:66",
                DestinationNodeId = FindCityNode(engine, "Columbus"),
                MovementType = PlayerMovementType.ThreeDie,
                MovementCapacity = 8,
                AverageFutureMovement = 10.5,
                TraveledSegmentKeys = traveledSegmentKeys,
                PlayerColor = "blue",
                ResolveRailroadOwnership = Ownership,
                ResolveRailroadFee = railroadIndex => Ownership(railroadIndex) == RailroadOwnershipCategory.Unfriendly ? 10_000 : 1_000,
                ResolveRailroadOwnerPlayerIndex = railroadIndex => engine.Railroads.First(railroad => railroad.Index == railroadIndex).Owner?.Index,
                CurrentWhiteDiceMovement = 3,
                CurrentFixedBonusMovement = 5,
                BonusOutRequiresWhiteDiceArrival = true
            });

        output.WriteLine($"{suggestion.Status} {suggestion.Message} {suggestion.Segments.Count} dots, cost {suggestion.TotalCost:N0}");
        foreach (var segment in suggestion.Segments)
        {
            output.WriteLine($"  {segment.FromNodeId}->{segment.ToNodeId} {engine.Railroads.First(railroad => railroad.Index == segment.RailroadIndex).ShortName} ({segment.OwnershipCategory})");
        }

        // All 8 dots this turn stay on thermous's own railroads; the only opponent fee is the final dot into Columbus.
        Assert.Equal(RouteSuggestionStatus.Success, suggestion.Status);
        Assert.Empty(suggestion.Message);
        Assert.All(suggestion.Segments.Take(8), segment => Assert.Equal(RailroadOwnershipCategory.Friendly, segment.OwnershipCategory));
        Assert.Single(suggestion.Segments, segment => segment.OwnershipCategory == RailroadOwnershipCategory.Unfriendly);
        Assert.Equal(12_000, suggestion.TotalCost);
    }

    // Owners in the game both positions come from (all railroads sold).
    private static readonly Dictionary<string, string[]> StandardGameOwners = new()
    {
        ["thermous"] = ["B&O", "SOU", "CB&Q", "T&P", "SP", "CRI&P", "WP", "GN"],
        ["Balenco"] = ["MP", "C&NW", "L&N", "UP", "SLSF"],
        ["El Cheapo"] = ["NYC", "PA", "B&M", "NYNH&H", "SAL", "N&W", "GM&O", "NP"],
        ["Pooper"] = ["C&O", "ACL", "RF&P", "IC", "AT&SF", "CMStP&P", "D&RGW"]
    };

    private static string NormalizeSegmentKey(string selectedSegmentKey)
    {
        var parts = selectedSegmentKey.Split('|');
        return string.Compare(parts[0], parts[1], StringComparison.OrdinalIgnoreCase) <= 0
            ? $"{parts[0]}-{parts[1]}:{parts[2]}"
            : $"{parts[1]}-{parts[0]}:{parts[2]}";
    }

    private void WriteRoute(Boxcars.Engine.Domain.GameEngine engine, Player player, Route route)
    {
        output.WriteLine($"{route.Segments.Count} dots, cost {route.TotalCost:N0}");
        var index = 0;
        while (index < route.Segments.Count)
        {
            var railroad = engine.Railroads.First(candidate => candidate.Index == route.Segments[index].RailroadIndex);
            var runEnd = index;
            while (runEnd < route.Segments.Count && route.Segments[runEnd].RailroadIndex == railroad.Index)
            {
                runEnd++;
            }

            var owner = railroad.Owner is null ? "bank" : railroad.Owner == player ? "own" : railroad.Owner.Name;
            output.WriteLine($"  {railroad.ShortName} ({owner}) x{runEnd - index}");
            index = runEnd;
        }
    }

    private static Boxcars.Engine.Domain.GameEngine CreateEngine(Dictionary<string, string[]> railroadsByOwner)
    {
        var map = MapDefinition.Parse(File.ReadAllText(FindStandardMapPath())).Definition
            ?? throw new InvalidOperationException("The standard map could not be parsed.");
        var engine = new Boxcars.Engine.Domain.GameEngine(
            map,
            railroadsByOwner.Keys.ToArray(),
            new FixedRandomProvider(),
            GameSettings.Default with { HomeCityChoice = false, HomeSwapping = false });

        foreach (var (ownerName, shortNames) in railroadsByOwner)
        {
            var owner = engine.Players.First(candidate => candidate.Name == ownerName);
            foreach (var shortName in shortNames)
            {
                var railroad = engine.Railroads.First(candidate => candidate.ShortName == shortName);
                railroad.Owner = owner;
                owner.OwnedRailroads.Add(railroad);
            }
        }

        // AllRailroadsSold has a private setter; every railroad is owned in these positions.
        typeof(Boxcars.Engine.Domain.GameEngine).GetProperty(nameof(engine.AllRailroadsSold))!.SetValue(engine, engine.Railroads.All(railroad => railroad.Owner is not null));
        return engine;
    }

    private static Player PlaceActivePlayer(
        Boxcars.Engine.Domain.GameEngine engine,
        string playerName,
        string cityName,
        string destinationName,
        LocomotiveType locomotiveType,
        int movementRemaining)
    {
        var player = engine.Players.First(candidate => candidate.Name == playerName);
        player.LocomotiveType = locomotiveType;
        player.CurrentCity = engine.MapDefinition.Cities.First(city => city.Name == cityName);
        player.CurrentNodeId = FindCityNode(engine, cityName);
        player.Destination = engine.MapDefinition.Cities.First(city => city.Name == destinationName);
        player.TripOriginCity = player.CurrentCity;

        engine.CurrentTurn.ActivePlayer = player;
        engine.CurrentTurn.Phase = TurnPhase.Move;
        engine.CurrentTurn.MovementRemaining = movementRemaining;
        return player;
    }

    private static string FindCityNode(Boxcars.Engine.Domain.GameEngine engine, string cityName)
    {
        var city = engine.MapDefinition.Cities.First(candidate => candidate.Name == cityName);
        var region = engine.MapDefinition.Regions.First(candidate => candidate.Code == city.RegionCode);
        return $"{region.Index}:{city.MapDotIndex}";
    }

    private static string FindStandardMapPath([CallerFilePath] string sourceFilePath = "")
    {
        // tests/Boxcars.Engine.Tests/Unit/<this file> -> src/Boxcars/U21MAP.RB3
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFilePath)!, "..", "..", "..", "src", "Boxcars", "U21MAP.RB3"));
    }
}

/// <summary>
/// Route planning tests that must not run in parallel: one of them sets the planner's shared time limit to zero.
/// </summary>
[CollectionDefinition(Name)]
public sealed class RoutePlanningCollection
{
    public const string Name = "Route planning";
}
