using Boxcars.Data.Maps;
using Boxcars.Engine.Data.Maps;
using Boxcars.Services.Maps;

namespace Boxcars.Engine.Tests.Unit;

public class MapRouteServiceTests
{
    [Fact]
    public void FindCheapestSuggestion_UsesMovementCapacityForSameRailroadPaths()
    {
        var service = new MapRouteService();
        var context = CreateContext();

        var lowCapacitySuggestion = service.FindCheapestSuggestion(
            context,
            new RouteSuggestionRequest
            {
                PlayerId = "player-1",
                StartNodeId = "A",
                DestinationNodeId = "D",
                MovementType = PlayerMovementType.ThreeDie,
                MovementCapacity = 2,
                PlayerColor = "#000000",
                ResolveRailroadOwnership = static _ => RailroadOwnershipCategory.OwnedByOtherPlayer
            });

        var highCapacitySuggestion = service.FindCheapestSuggestion(
            context,
            new RouteSuggestionRequest
            {
                PlayerId = "player-1",
                StartNodeId = "A",
                DestinationNodeId = "D",
                MovementType = PlayerMovementType.ThreeDie,
                MovementCapacity = 5,
                PlayerColor = "#000000",
                ResolveRailroadOwnership = static _ => RailroadOwnershipCategory.OwnedByOtherPlayer
            });

        Assert.Equal(["A", "Z", "D"], lowCapacitySuggestion.NodeIds);
        Assert.Equal(["A", "B", "C", "E", "D"], highCapacitySuggestion.NodeIds);
    }

    [Fact]
    public void FindCheapestSuggestion_TwoDieMovement_IgnoresMovementCapacityOverride()
    {
        var service = new MapRouteService();
        var context = CreateContext();

        var suggestion = service.FindCheapestSuggestion(
            context,
            new RouteSuggestionRequest
            {
                PlayerId = "player-1",
                StartNodeId = "A",
                DestinationNodeId = "D",
                MovementType = PlayerMovementType.TwoDie,
                MovementCapacity = 5,
                PlayerColor = "#000000",
                ResolveRailroadOwnership = static _ => RailroadOwnershipCategory.OwnedByOtherPlayer
            });

        Assert.Equal(["A", "Z", "D"], suggestion.NodeIds);
    }

    [Fact]
    public void FindCheapestSuggestion_DoesNotReusePreviouslyTraveledSegment()
    {
        var service = new MapRouteService();
        var context = CreateContext();

        var suggestion = service.FindCheapestSuggestion(
            context,
            new RouteSuggestionRequest
            {
                PlayerId = "player-1",
                StartNodeId = "A",
                DestinationNodeId = "D",
                MovementType = PlayerMovementType.ThreeDie,
                MovementCapacity = 5,
                TraveledSegmentKeys = ["A-B"],
                PlayerColor = "#000000",
                ResolveRailroadOwnership = static _ => RailroadOwnershipCategory.OwnedByOtherPlayer
            });

        Assert.Equal(["A", "Z", "D"], suggestion.NodeIds);
    }

    private static MapRouteContext CreateContext()
    {
        var adjacency = new Dictionary<string, List<RouteGraphEdge>>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = [],
            ["B"] = [],
            ["C"] = [],
            ["D"] = [],
            ["E"] = [],
            ["Z"] = []
        };
        var dotLookup = new Dictionary<string, TrainDot>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = CreateDot("A", 0, 0),
            ["B"] = CreateDot("B", 0, 1),
            ["C"] = CreateDot("C", 0, 2),
            ["D"] = CreateDot("D", 0, 3),
            ["E"] = CreateDot("E", 0, 4),
            ["Z"] = CreateDot("Z", 0, 5)
        };

        AddEdge(adjacency, "A", "B", 1);
        AddEdge(adjacency, "B", "C", 1);
        AddEdge(adjacency, "C", "E", 1);
        AddEdge(adjacency, "E", "D", 1);
        AddEdge(adjacency, "A", "Z", 2);
        AddEdge(adjacency, "Z", "D", 2);

        return new MapRouteContext
        {
            Adjacency = adjacency,
            DotLookup = dotLookup
        };
    }

    private static TrainDot CreateDot(string id, int regionIndex, int dotIndex)
    {
        return new TrainDot
        {
            Id = id,
            RegionIndex = regionIndex,
            DotIndex = dotIndex,
            X = dotIndex,
            Y = regionIndex
        };
    }

    private static void AddEdge(Dictionary<string, List<RouteGraphEdge>> adjacency, string fromNodeId, string toNodeId, int railroadIndex)
    {
        if (!adjacency.TryGetValue(fromNodeId, out var edges))
        {
            edges = new List<RouteGraphEdge>();
            adjacency[fromNodeId] = edges;
        }

        edges.Add(new RouteGraphEdge
        {
            FromNodeId = fromNodeId,
            ToNodeId = toNodeId,
            RailroadIndex = railroadIndex,
            X1 = 0,
            Y1 = 0,
            X2 = 1,
            Y2 = 1
        });
    }
}