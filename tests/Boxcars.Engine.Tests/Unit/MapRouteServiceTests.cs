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

        // With capacity 2: A→B→C→E→D (4 segs RR1) = 2 turns × $5000 = $10000
        //                  A→Z→D (2 segs RR2) = 1 turn × $5000 = $5000 → wins
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

        // With capacity 5: both paths fit in 1 turn ($5000 each),
        // shorter path (A→Z→D, 2 segments) wins the tiebreaker
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
        Assert.Equal(5000, lowCapacitySuggestion.TotalCost);

        Assert.Equal(["A", "Z", "D"], highCapacitySuggestion.NodeIds);
        Assert.Equal(5000, highCapacitySuggestion.TotalCost);
    }

    [Fact]
    public void FindCheapestSuggestion_CapacityAffectsTurnCount()
    {
        var service = new MapRouteService();

        // Linear path A→B→C→D on RR1 (unowned, $1000/turn)
        var adjacency = new Dictionary<string, List<RouteGraphEdge>>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = [], ["B"] = [], ["C"] = [], ["D"] = []
        };
        var dotLookup = new Dictionary<string, TrainDot>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = CreateDot("A", 0, 0),
            ["B"] = CreateDot("B", 0, 1),
            ["C"] = CreateDot("C", 0, 2),
            ["D"] = CreateDot("D", 0, 3)
        };
        AddEdge(adjacency, "A", "B", 1);
        AddEdge(adjacency, "B", "C", 1);
        AddEdge(adjacency, "C", "D", 1);
        var context = new MapRouteContext { Adjacency = adjacency, DotLookup = dotLookup };

        var lowCap = service.FindCheapestSuggestion(
            context,
            new RouteSuggestionRequest
            {
                PlayerId = "player-1",
                StartNodeId = "A",
                DestinationNodeId = "D",
                MovementType = PlayerMovementType.TwoDie,
                MovementCapacity = 1,
                PlayerColor = "#000000",
                ResolveRailroadOwnership = static _ => RailroadOwnershipCategory.Unowned
            });

        var highCap = service.FindCheapestSuggestion(
            context,
            new RouteSuggestionRequest
            {
                PlayerId = "player-1",
                StartNodeId = "A",
                DestinationNodeId = "D",
                MovementType = PlayerMovementType.TwoDie,
                MovementCapacity = 3,
                PlayerColor = "#000000",
                ResolveRailroadOwnership = static _ => RailroadOwnershipCategory.Unowned
            });

        // Same route, but capacity changes the number of turns and total cost
        Assert.Equal(["A", "B", "C", "D"], lowCap.NodeIds);
        Assert.Equal(3, lowCap.TotalTurns);
        Assert.Equal(3000, lowCap.TotalCost);

        Assert.Equal(["A", "B", "C", "D"], highCap.NodeIds);
        Assert.Equal(1, highCap.TotalTurns);
        Assert.Equal(1000, highCap.TotalCost);
    }

    [Fact]
    public void FindCheapestSuggestion_TwoDieMovement_UsesCurrentTurnMovementCapacity()
    {
        var service = new MapRouteService();
        var context = CreateContext();

        // With capacity 5: A→Z→D (2 segs) = 1 turn at $5000 = $5000
        // A→B→C→E→D (4 segs) = 1 turn at $5000 = $5000 (tie, fewer segments wins)
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
        Assert.Equal(5000, suggestion.TotalCost);
    }

    [Fact]
    public void FindCheapestSuggestion_TwoDieMovement_WithoutCurrentCapacityDefaultsToTwoDieTurns()
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
                MovementCapacity = 0,
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
                TraveledSegmentKeys = ["A-B:1"],
                PlayerColor = "#000000",
                ResolveRailroadOwnership = static _ => RailroadOwnershipCategory.OwnedByOtherPlayer
            });

        Assert.Equal(["A", "Z", "D"], suggestion.NodeIds);
    }

    [Fact]
    public void FindCheapestSuggestion_MidTurnRailroadSwitch_DoesNotResetMovement()
    {
        var service = new MapRouteService();

        // A --[RR1]--> B --[RR2]--> C --[RR2]--> D (switch mid-turn, 3 edges)
        // A --[RR3]--> D (direct, 1 edge, but on expensive owned railroad)
        var adjacency = new Dictionary<string, List<RouteGraphEdge>>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = [], ["B"] = [], ["C"] = [], ["D"] = []
        };
        var dotLookup = new Dictionary<string, TrainDot>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = CreateDot("A", 0, 0),
            ["B"] = CreateDot("B", 0, 1),
            ["C"] = CreateDot("C", 0, 2),
            ["D"] = CreateDot("D", 0, 3)
        };

        AddEdge(adjacency, "A", "B", 1);
        AddEdge(adjacency, "B", "C", 2);
        AddEdge(adjacency, "C", "D", 2);
        AddEdge(adjacency, "A", "D", 3);

        var context = new MapRouteContext { Adjacency = adjacency, DotLookup = dotLookup };

        // RR3 is owned by another player ($5000), RR1 and RR2 are unowned ($1000 each)
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
                ResolveRailroadOwnership = rr => rr == 3
                    ? RailroadOwnershipCategory.OwnedByOtherPlayer
                    : RailroadOwnershipCategory.Unowned
            });

        // Should prefer A→B→C→D ($1000 for RR1 + $1000 for RR2 = $2000, 1 turn)
        // over A→D ($5000 for RR3, 1 turn)
        Assert.Equal(RouteSuggestionStatus.Success, suggestion.Status);
        Assert.Equal(["A", "B", "C", "D"], suggestion.NodeIds);
        Assert.Equal(2000, suggestion.TotalCost);
        Assert.Equal(1, suggestion.TotalTurns);
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