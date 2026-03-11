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

    [Fact]
    public void FindCheapestSuggestion_NeverRevisitsNode()
    {
        var service = new MapRouteService();

        // Graph with a tempting loop: A→B→C→A→D vs A→D (direct but expensive)
        //   A --[RR1]--> B --[RR1]--> C --[RR1]--> A (back-edge!)
        //   A --[RR2]--> D
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
        AddEdge(adjacency, "C", "A", 1); // back-edge that could create a loop
        AddEdge(adjacency, "A", "D", 2);

        var context = new MapRouteContext { Adjacency = adjacency, DotLookup = dotLookup };

        var suggestion = service.FindCheapestSuggestion(
            context,
            new RouteSuggestionRequest
            {
                PlayerId = "player-1",
                StartNodeId = "A",
                DestinationNodeId = "D",
                MovementType = PlayerMovementType.TwoDie,
                MovementCapacity = 10,
                PlayerColor = "#000000",
                ResolveRailroadOwnership = static _ => RailroadOwnershipCategory.Unowned
            });

        Assert.Equal(RouteSuggestionStatus.Success, suggestion.Status);
        // Must go A→D directly, never loop through B→C→A
        Assert.Equal(["A", "D"], suggestion.NodeIds);
        // Every node appears exactly once
        Assert.Equal(suggestion.NodeIds.Count, suggestion.NodeIds.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void FindCheapestSuggestion_NoSegmentReusedWithinSuggestion()
    {
        var service = new MapRouteService();

        // Diamond: A→B→D and A→C→D, all on RR1
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
        AddEdge(adjacency, "B", "D", 1);
        AddEdge(adjacency, "A", "C", 1);
        AddEdge(adjacency, "C", "D", 1);

        var context = new MapRouteContext { Adjacency = adjacency, DotLookup = dotLookup };

        var suggestion = service.FindCheapestSuggestion(
            context,
            new RouteSuggestionRequest
            {
                PlayerId = "player-1",
                StartNodeId = "A",
                DestinationNodeId = "D",
                MovementType = PlayerMovementType.TwoDie,
                MovementCapacity = 10,
                PlayerColor = "#000000",
                ResolveRailroadOwnership = static _ => RailroadOwnershipCategory.Unowned
            });

        Assert.Equal(RouteSuggestionStatus.Success, suggestion.Status);
        // Path must be simple — no repeated segments
        var segmentKeys = suggestion.Segments
            .Select(s => $"{s.FromNodeId}-{s.ToNodeId}:{s.RailroadIndex}")
            .ToList();
        Assert.Equal(segmentKeys.Count, segmentKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void FindCheapestSuggestion_SameNodePairOnDifferentRailroad_IsAllowed()
    {
        var service = new MapRouteService();

        // A→B on RR1 is traveled, but A→B on RR2 should still be usable
        var adjacency = new Dictionary<string, List<RouteGraphEdge>>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = [], ["B"] = [], ["C"] = []
        };
        var dotLookup = new Dictionary<string, TrainDot>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = CreateDot("A", 0, 0),
            ["B"] = CreateDot("B", 0, 1),
            ["C"] = CreateDot("C", 0, 2)
        };

        AddEdge(adjacency, "A", "B", 1); // RR1 — will be marked as traveled
        AddEdge(adjacency, "A", "B", 2); // RR2 — same nodes, different railroad
        AddEdge(adjacency, "B", "C", 2);

        var context = new MapRouteContext { Adjacency = adjacency, DotLookup = dotLookup };

        var suggestion = service.FindCheapestSuggestion(
            context,
            new RouteSuggestionRequest
            {
                PlayerId = "player-1",
                StartNodeId = "A",
                DestinationNodeId = "C",
                MovementType = PlayerMovementType.TwoDie,
                MovementCapacity = 5,
                TraveledSegmentKeys = ["A-B:1"], // RR1 blocked
                PlayerColor = "#000000",
                ResolveRailroadOwnership = static _ => RailroadOwnershipCategory.Unowned
            });

        Assert.Equal(RouteSuggestionStatus.Success, suggestion.Status);
        Assert.Equal(["A", "B", "C"], suggestion.NodeIds);
        // Must use RR2 for the A→B segment
        Assert.Equal(2, suggestion.Segments[0].RailroadIndex);
    }

    [Fact]
    public void FindCheapestSuggestion_AlwaysReachesDestination()
    {
        var service = new MapRouteService();

        // Longer chain: A→B→C→D→E→F, single railroad
        var adjacency = new Dictionary<string, List<RouteGraphEdge>>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = [], ["B"] = [], ["C"] = [], ["D"] = [], ["E"] = [], ["F"] = []
        };
        var dotLookup = new Dictionary<string, TrainDot>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = CreateDot("A", 0, 0),
            ["B"] = CreateDot("B", 0, 1),
            ["C"] = CreateDot("C", 0, 2),
            ["D"] = CreateDot("D", 0, 3),
            ["E"] = CreateDot("E", 0, 4),
            ["F"] = CreateDot("F", 0, 5)
        };

        AddEdge(adjacency, "A", "B", 1);
        AddEdge(adjacency, "B", "C", 1);
        AddEdge(adjacency, "C", "D", 1);
        AddEdge(adjacency, "D", "E", 1);
        AddEdge(adjacency, "E", "F", 1);

        var context = new MapRouteContext { Adjacency = adjacency, DotLookup = dotLookup };

        var suggestion = service.FindCheapestSuggestion(
            context,
            new RouteSuggestionRequest
            {
                PlayerId = "player-1",
                StartNodeId = "A",
                DestinationNodeId = "F",
                MovementType = PlayerMovementType.TwoDie,
                MovementCapacity = 2,
                PlayerColor = "#000000",
                ResolveRailroadOwnership = static _ => RailroadOwnershipCategory.Unowned
            });

        Assert.Equal(RouteSuggestionStatus.Success, suggestion.Status);
        Assert.Equal(["A", "B", "C", "D", "E", "F"], suggestion.NodeIds);
        // Last node must be the destination
        Assert.Equal("F", suggestion.NodeIds[^1]);
        Assert.Equal(3, suggestion.TotalTurns);  // 5 segments, capacity 2 → 3 turns
        Assert.Equal(3000, suggestion.TotalCost);
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