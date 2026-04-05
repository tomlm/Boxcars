using Boxcars.Data.Maps;
using Boxcars.Engine.Data.Maps;
using Boxcars.Services.Maps;
using System.Globalization;
using System.Threading;

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
                ResolveRailroadOwnership = static _ => RailroadOwnershipCategory.Unfriendly
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
                ResolveRailroadOwnership = static _ => RailroadOwnershipCategory.Unfriendly
            });

        Assert.Equal(["A", "Z", "D"], lowCapacitySuggestion.NodeIds);
        Assert.Equal(5000, lowCapacitySuggestion.TotalCost);

        Assert.Equal(["A", "Z", "D"], highCapacitySuggestion.NodeIds);
        Assert.Equal(5000, highCapacitySuggestion.TotalCost);
    }

    [Fact]
    public void FindCheapestSuggestion_PrefersMoreDirectRouteWhenDetourOnlySavesOneThousand()
    {
        var service = new MapRouteService();

        var adjacency = new Dictionary<string, List<RouteGraphEdge>>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = [], ["B"] = [], ["C"] = [], ["D"] = [], ["E"] = [], ["F"] = [], ["G"] = []
        };
        var dotLookup = new Dictionary<string, TrainDot>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = CreateDot("A", 0, 0),
            ["B"] = CreateDot("B", 0, 1),
            ["C"] = CreateDot("C", 0, 2),
            ["D"] = CreateDot("D", 0, 3),
            ["E"] = CreateDot("E", 0, 4),
            ["F"] = CreateDot("F", 0, 5),
            ["G"] = CreateDot("G", 0, 6)
        };

        AddEdge(adjacency, "A", "B", 1);
        AddEdge(adjacency, "B", "C", 1);
        AddEdge(adjacency, "C", "D", 1);
        AddEdge(adjacency, "D", "E", 1);
        AddEdge(adjacency, "E", "F", 1);
        AddEdge(adjacency, "F", "G", 1);
        AddEdge(adjacency, "A", "G", 2);

        var context = new MapRouteContext { Adjacency = adjacency, DotLookup = dotLookup };

        var suggestion = service.FindCheapestSuggestion(
            context,
            new RouteSuggestionRequest
            {
                PlayerId = "player-1",
                StartNodeId = "A",
                DestinationNodeId = "G",
                MovementType = PlayerMovementType.ThreeDie,
                MovementCapacity = 10,
                PlayerColor = "#000000",
                MaximumSearchMilliseconds = 5000,
                ResolveRailroadOwnership = rr => rr == 1
                    ? RailroadOwnershipCategory.Friendly
                    : RailroadOwnershipCategory.Public,
                ResolveRailroadFee = rr => rr == 1 ? 1000 : 2000
            });

        Assert.Equal(RouteSuggestionStatus.Success, suggestion.Status);
        Assert.Equal(["A", "G"], suggestion.NodeIds);
        Assert.Equal(2000, suggestion.TotalCost);
    }

    [Fact]
    public void FindCheapestSuggestion_RejectsRoutesLongerThanMaximumSuggestedSegments()
    {
        var service = new MapRouteService();

        var adjacency = new Dictionary<string, List<RouteGraphEdge>>(StringComparer.OrdinalIgnoreCase);
        var dotLookup = new Dictionary<string, TrainDot>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index <= MapRouteService.RoutePlanningMaximumSuggestedSegments; index++)
        {
            var nodeId = $"N{index}";
            adjacency[nodeId] = [];
            dotLookup[nodeId] = CreateDot(nodeId, 0, index);
        }

        for (var index = 0; index < MapRouteService.RoutePlanningMaximumSuggestedSegments; index++)
        {
            AddEdge(adjacency, $"N{index}", $"N{index + 1}", 1);
        }

        var overflowNodeId = $"N{MapRouteService.RoutePlanningMaximumSuggestedSegments + 1}";
        adjacency[overflowNodeId] = [];
        dotLookup[overflowNodeId] = CreateDot(overflowNodeId, 0, MapRouteService.RoutePlanningMaximumSuggestedSegments + 1);
        AddEdge(adjacency, $"N{MapRouteService.RoutePlanningMaximumSuggestedSegments}", overflowNodeId, 1);

        var context = new MapRouteContext { Adjacency = adjacency, DotLookup = dotLookup };

        var suggestion = service.FindCheapestSuggestion(
            context,
            new RouteSuggestionRequest
            {
                PlayerId = "player-1",
                StartNodeId = "N0",
                DestinationNodeId = overflowNodeId,
                MovementType = PlayerMovementType.TwoDie,
                MovementCapacity = 10,
                PlayerColor = "#000000",
                ResolveRailroadOwnership = static _ => RailroadOwnershipCategory.Public
            });

        Assert.Equal(RouteSuggestionStatus.NoRoute, suggestion.Status);
    }

    [Fact]
    public void FindCheapestSuggestion_StopsWhenExplorationBudgetIsExceeded()
    {
        var service = new MapRouteService();

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

        AddBidirectionalEdge(adjacency, "A", "B", 1);
        AddBidirectionalEdge(adjacency, "B", "C", 1);

        var context = new MapRouteContext { Adjacency = adjacency, DotLookup = dotLookup };

        var suggestion = service.FindCheapestSuggestion(
            context,
            new RouteSuggestionRequest
            {
                PlayerId = "player-1",
                StartNodeId = "A",
                DestinationNodeId = "C",
                MovementType = PlayerMovementType.TwoDie,
                MovementCapacity = 2,
                PlayerColor = "#000000",
                ResolveRailroadOwnership = static _ => RailroadOwnershipCategory.Public,
                MaximumExploredStates = 1
            });

        Assert.Equal(RouteSuggestionStatus.NoRoute, suggestion.Status);
        Assert.Contains("exploration budget", suggestion.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindCheapestSuggestion_StopsWhenSearchTimesOut()
    {
        var service = new MapRouteService();

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

        AddBidirectionalEdge(adjacency, "A", "B", 1);
        AddBidirectionalEdge(adjacency, "B", "C", 1);

        var context = new MapRouteContext { Adjacency = adjacency, DotLookup = dotLookup };

        var suggestion = service.FindCheapestSuggestion(
            context,
            new RouteSuggestionRequest
            {
                PlayerId = "player-1",
                StartNodeId = "A",
                DestinationNodeId = "C",
                MovementType = PlayerMovementType.TwoDie,
                MovementCapacity = 2,
                PlayerColor = "#000000",
                ResolveRailroadOwnership = static railroadIndex =>
                {
                    Thread.Sleep(25);
                    return RailroadOwnershipCategory.Public;
                },
                MaximumSearchMilliseconds = 1
            });

        Assert.Equal(RouteSuggestionStatus.NoRoute, suggestion.Status);
        Assert.Contains("timed out", suggestion.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindCheapestSuggestion_PrefersFriendlyExitWhenHostileRailroadCanBeLeftForDestinationPath()
    {
        var service = new MapRouteService();

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

        AddBidirectionalEdge(adjacency, "A", "B", 1);
        AddBidirectionalEdge(adjacency, "B", "D", 1);
        AddBidirectionalEdge(adjacency, "B", "C", 2);
        AddBidirectionalEdge(adjacency, "C", "D", 2);

        var context = new MapRouteContext { Adjacency = adjacency, DotLookup = dotLookup };

        var suggestion = service.FindCheapestSuggestion(
            context,
            new RouteSuggestionRequest
            {
                PlayerId = "player-1",
                StartNodeId = "A",
                DestinationNodeId = "D",
                MovementType = PlayerMovementType.ThreeDie,
                MovementCapacity = 10,
                PlayerColor = "#000000",
                ResolveRailroadOwnership = rr => rr == 1
                    ? RailroadOwnershipCategory.Unfriendly
                    : RailroadOwnershipCategory.Public
            });

        Assert.Equal(RouteSuggestionStatus.Success, suggestion.Status);
        Assert.Equal(["A", "B", "C", "D"], suggestion.NodeIds);
        Assert.Equal([1, 2, 2], suggestion.Segments.Select(segment => segment.RailroadIndex).ToArray());
    }

    [Fact]
    public void FindCheapestSuggestion_CapacityAffectsTurnCount()
    {
        var service = new MapRouteService();

        // Linear path A→B→C→D on RR1 (public, $1000/turn)
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
                ResolveRailroadOwnership = static _ => RailroadOwnershipCategory.Public
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
                ResolveRailroadOwnership = static _ => RailroadOwnershipCategory.Public
            });

        // First turn uses the current remaining movement; later turns use normal two-die capacity.
        Assert.Equal(["A", "B", "C", "D"], lowCap.NodeIds);
        Assert.Equal(2, lowCap.TotalTurns);
        Assert.Equal(2000, lowCap.TotalCost);

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
                ResolveRailroadOwnership = static _ => RailroadOwnershipCategory.Unfriendly
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
                ResolveRailroadOwnership = static _ => RailroadOwnershipCategory.Unfriendly
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
                ResolveRailroadOwnership = static _ => RailroadOwnershipCategory.Unfriendly
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

        // RR3 is unfriendly ($5000), RR1 and RR2 are public ($1000 each)
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
                    ? RailroadOwnershipCategory.Unfriendly
                    : RailroadOwnershipCategory.Public
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
                ResolveRailroadOwnership = static _ => RailroadOwnershipCategory.Public
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
                ResolveRailroadOwnership = static _ => RailroadOwnershipCategory.Public
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
                ResolveRailroadOwnership = static _ => RailroadOwnershipCategory.Public
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
                ResolveRailroadOwnership = static _ => RailroadOwnershipCategory.Public
            });

        Assert.Equal(RouteSuggestionStatus.Success, suggestion.Status);
        Assert.Equal(["A", "B", "C", "D", "E", "F"], suggestion.NodeIds);
        // Last node must be the destination
        Assert.Equal("F", suggestion.NodeIds[^1]);
        Assert.Equal(3, suggestion.TotalTurns);  // 5 segments, capacity 2 → 3 turns
        Assert.Equal(3000, suggestion.TotalCost);
    }

    [Fact]
    public void FindCheapestSuggestion_UnfriendlyDestination_PrefersLowerExitCostRoute()
    {
        var service = new MapRouteService();

        var adjacency = new Dictionary<string, List<RouteGraphEdge>>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = [],
            ["B"] = [],
            ["C"] = [],
            ["D"] = [],
            ["E"] = [],
            ["F"] = [],
            ["G"] = [],
            ["H"] = [],
            ["J"] = [],
            ["K"] = []
        };
        var dotLookup = new Dictionary<string, TrainDot>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = CreateDot("A", 0, 0),
            ["B"] = CreateDot("B", 0, 1),
            ["C"] = CreateDot("C", 0, 2),
            ["D"] = CreateDot("D", 0, 3),
            ["E"] = CreateDot("E", 0, 4),
            ["F"] = CreateDot("F", 0, 5),
            ["G"] = CreateDot("G", 0, 6),
            ["H"] = CreateDot("H", 0, 7),
            ["J"] = CreateDot("J", 0, 8),
            ["K"] = CreateDot("K", 0, 9)
        };

        AddEdge(adjacency, "A", "B", 1);
        AddEdge(adjacency, "B", "D", 2);
        AddEdge(adjacency, "A", "C", 1);
        AddEdge(adjacency, "C", "D", 3);
        AddEdge(adjacency, "D", "E", 2);
        AddEdge(adjacency, "E", "F", 4);
        AddEdge(adjacency, "D", "G", 3);
        AddEdge(adjacency, "G", "H", 3);
        AddEdge(adjacency, "H", "J", 3);
        AddEdge(adjacency, "J", "K", 5);

        var context = new MapRouteContext { Adjacency = adjacency, DotLookup = dotLookup };

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
                ResolveRailroadOwnership = railroadIndex => railroadIndex switch
                {
                    1 or 4 or 5 => RailroadOwnershipCategory.Public,
                    _ => RailroadOwnershipCategory.Unfriendly
                },
                ResolveRailroadFee = railroadIndex => railroadIndex switch
                {
                    1 or 4 or 5 => 1000,
                    _ => 5000
                }
            });

        Assert.Equal(RouteSuggestionStatus.Success, suggestion.Status);
        Assert.Equal([1, 2], suggestion.Segments.Take(2).Select(segment => segment.RailroadIndex).ToArray());
        Assert.Equal(6000, suggestion.TotalCost);
        Assert.Equal(6000, suggestion.Outlook.ExitCost);
        Assert.Equal(12000, suggestion.Outlook.CombinedCost);
    }

    [Fact]
    public void FindCheapestSuggestion_BonusOut_PrefersHigherEscapeProbabilityRoute()
    {
        var service = new MapRouteService();

        var adjacency = new Dictionary<string, List<RouteGraphEdge>>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = [], ["B"] = [], ["C"] = [], ["D"] = [], ["E"] = [], ["F"] = [],
            ["G"] = [], ["H"] = [], ["I"] = [], ["X"] = []
        };
        var dotLookup = new Dictionary<string, TrainDot>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = CreateDot("A", 0, 0),
            ["B"] = CreateDot("B", 0, 1),
            ["C"] = CreateDot("C", 0, 2),
            ["D"] = CreateDot("D", 0, 3),
            ["E"] = CreateDot("E", 0, 4),
            ["F"] = CreateDot("F", 0, 5),
            ["G"] = CreateDot("G", 0, 6),
            ["H"] = CreateDot("H", 0, 7),
            ["I"] = CreateDot("I", 0, 8),
            ["X"] = CreateDot("X", 0, 9)
        };

        AddEdge(adjacency, "A", "B", 1);
        AddEdge(adjacency, "B", "X", 1);
        AddEdge(adjacency, "X", "D", 2);
        AddEdge(adjacency, "A", "C", 1);
        AddEdge(adjacency, "C", "D", 3);
        AddEdge(adjacency, "D", "E", 2);
        AddEdge(adjacency, "E", "F", 4);
        AddEdge(adjacency, "D", "G", 3);
        AddEdge(adjacency, "G", "H", 3);
        AddEdge(adjacency, "H", "I", 5);

        var context = new MapRouteContext { Adjacency = adjacency, DotLookup = dotLookup };

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
                ResolveRailroadOwnership = railroadIndex => railroadIndex switch
                {
                    1 or 4 or 5 => RailroadOwnershipCategory.Public,
                    _ => RailroadOwnershipCategory.Unfriendly
                },
                ResolveRailroadFee = railroadIndex => railroadIndex switch
                {
                    1 or 4 or 5 => 1000,
                    _ => 5000
                },
                BonusOutAvailable = true
            });

        Assert.Equal(RouteSuggestionStatus.Success, suggestion.Status);
        Assert.Equal([1, 1, 2], suggestion.Segments.Take(3).Select(segment => segment.RailroadIndex).ToArray());
        Assert.Equal(6000, suggestion.TotalCost);
        Assert.Equal(6000, suggestion.Outlook.WorstCaseExitCost);
        Assert.Equal(1000d, suggestion.Outlook.ExpectedExitCost, 6);
        Assert.Equal(5d / 6d, suggestion.Outlook.BonusOutProbability, 6);
    }

    [Fact]
    public void FindCheapestSuggestion_TieBreak_PrefersLowerCashOwner()
    {
        var service = new MapRouteService();
        var context = CreateTieBreakContext();

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
                ResolveRailroadOwnership = railroadIndex => railroadIndex switch
                {
                    2 or 3 => RailroadOwnershipCategory.Unfriendly,
                    _ => RailroadOwnershipCategory.Public
                },
                ResolveRailroadFee = railroadIndex => railroadIndex switch
                {
                    2 or 3 => 5000,
                    _ => 1000
                },
                ResolveRailroadOwnerPlayerIndex = railroadIndex => railroadIndex switch
                {
                    2 => 1,
                    3 => 2,
                    _ => null
                },
                ResolvePlayerCash = playerIndex => playerIndex switch
                {
                    1 => 5000,
                    2 => 25000,
                    _ => int.MaxValue
                }
            });

        Assert.Equal(RouteSuggestionStatus.Success, suggestion.Status);
        Assert.Equal([1, 2], suggestion.Segments.Take(2).Select(segment => segment.RailroadIndex).ToArray());
    }

    [Fact]
    public void FindCheapestSuggestion_TieBreak_PrefersWeakerNetworkOwner()
    {
        var service = new MapRouteService();
        var context = CreateTieBreakContext();

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
                ResolveRailroadOwnership = railroadIndex => railroadIndex switch
                {
                    2 or 3 => RailroadOwnershipCategory.Unfriendly,
                    _ => RailroadOwnershipCategory.Public
                },
                ResolveRailroadFee = railroadIndex => railroadIndex switch
                {
                    2 or 3 => 5000,
                    _ => 1000
                },
                ResolveRailroadOwnerPlayerIndex = railroadIndex => railroadIndex switch
                {
                    2 => 1,
                    3 => 2,
                    _ => null
                },
                ResolvePlayerCash = _ => 10000,
                ResolvePlayerAccessibleDestinationPercent = playerIndex => playerIndex switch
                {
                    1 => 12.5,
                    2 => 40.0,
                    _ => double.MaxValue
                },
                ResolvePlayerMonopolyDestinationPercent = playerIndex => playerIndex switch
                {
                    1 => 3.5,
                    2 => 15.0,
                    _ => double.MaxValue
                }
            });

        Assert.Equal(RouteSuggestionStatus.Success, suggestion.Status);
        Assert.Equal([1, 2], suggestion.Segments.Take(2).Select(segment => segment.RailroadIndex).ToArray());
    }

    [Fact]
    public void FindCheapestSuggestion_TieBreak_PrefersSpreadPaymentsAcrossOwners()
    {
        var service = new MapRouteService();

        var adjacency = new Dictionary<string, List<RouteGraphEdge>>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = [], ["B"] = [], ["C"] = [], ["D"] = [], ["E"] = [], ["F"] = [], ["G"] = []
        };
        var dotLookup = new Dictionary<string, TrainDot>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = CreateDot("A", 0, 0),
            ["B"] = CreateDot("B", 0, 1),
            ["C"] = CreateDot("C", 0, 2),
            ["D"] = CreateDot("D", 0, 3),
            ["E"] = CreateDot("E", 0, 4),
            ["F"] = CreateDot("F", 0, 5),
            ["G"] = CreateDot("G", 0, 6)
        };

        AddEdge(adjacency, "A", "B", 1);
        AddEdge(adjacency, "B", "C", 2);
        AddEdge(adjacency, "C", "D", 2);
        AddEdge(adjacency, "A", "E", 1);
        AddEdge(adjacency, "E", "F", 2);
        AddEdge(adjacency, "F", "D", 3);
        AddEdge(adjacency, "D", "G", 4);

        var context = new MapRouteContext { Adjacency = adjacency, DotLookup = dotLookup };

        var suggestion = service.FindCheapestSuggestion(
            context,
            new RouteSuggestionRequest
            {
                PlayerId = "player-1",
                StartNodeId = "A",
                DestinationNodeId = "D",
                MovementType = PlayerMovementType.TwoDie,
                MovementCapacity = 1,
                PlayerColor = "#000000",
                ResolveRailroadOwnership = railroadIndex => railroadIndex switch
                {
                    2 or 3 => RailroadOwnershipCategory.Unfriendly,
                    _ => RailroadOwnershipCategory.Public
                },
                ResolveRailroadFee = railroadIndex => railroadIndex switch
                {
                    2 or 3 => 5000,
                    _ => 1000
                },
                ResolveRailroadOwnerPlayerIndex = railroadIndex => railroadIndex switch
                {
                    2 => 1,
                    3 => 2,
                    _ => null
                },
                ResolvePlayerCash = _ => 10000,
                ResolvePlayerAccessibleDestinationPercent = _ => 20.0,
                ResolvePlayerMonopolyDestinationPercent = _ => 10.0
            });

        Assert.Equal(RouteSuggestionStatus.Success, suggestion.Status);
        Assert.Equal([1, 2, 2], suggestion.Segments.Take(3).Select(segment => segment.RailroadIndex).ToArray());
    }

    [Fact]
    public void FindCheapestSuggestion_SafetyOutranksSpreadPaymentsTieBreak()
    {
        var service = new MapRouteService();

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
        AddEdge(adjacency, "B", "D", 2);
        AddEdge(adjacency, "A", "C", 1);
        AddEdge(adjacency, "C", "E", 2);
        AddEdge(adjacency, "E", "D", 3);
        AddEdge(adjacency, "D", "F", 4);

        var context = new MapRouteContext { Adjacency = adjacency, DotLookup = dotLookup };

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
                ResolveRailroadOwnership = railroadIndex => railroadIndex switch
                {
                    2 or 3 => RailroadOwnershipCategory.Unfriendly,
                    _ => RailroadOwnershipCategory.Public
                },
                ResolveRailroadFee = railroadIndex => railroadIndex switch
                {
                    2 or 3 => 5000,
                    _ => 1000
                },
                ResolveRailroadOwnerPlayerIndex = railroadIndex => railroadIndex switch
                {
                    2 => 1,
                    3 => 2,
                    _ => null
                },
                ResolvePlayerCash = playerIndex => playerIndex switch
                {
                    1 => 1_000,
                    2 => 50_000,
                    _ => int.MaxValue
                },
                ResolvePlayerAccessibleDestinationPercent = playerIndex => playerIndex switch
                {
                    1 => 1.0,
                    2 => 99.0,
                    _ => double.MaxValue
                },
                ResolvePlayerMonopolyDestinationPercent = playerIndex => playerIndex switch
                {
                    1 => 1.0,
                    2 => 99.0,
                    _ => double.MaxValue
                }
            });

        Assert.Equal(RouteSuggestionStatus.Success, suggestion.Status);
        Assert.Equal([1, 2], suggestion.Segments.Select(segment => segment.RailroadIndex).ToArray());
    }

    [Fact]
    public void FindCheapestSuggestion_PrefersLowerTwoTurnCostOverStayingOnHostileRail()
    {
        var service = new MapRouteService();

        var adjacency = new Dictionary<string, List<RouteGraphEdge>>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = [], ["B"] = [], ["C"] = [], ["D"] = [], ["E"] = [], ["F"] = [], ["G"] = [], ["H"] = []
        };
        var dotLookup = new Dictionary<string, TrainDot>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = CreateDot("A", 0, 0),
            ["B"] = CreateDot("B", 0, 1),
            ["C"] = CreateDot("C", 0, 2),
            ["D"] = CreateDot("D", 0, 3),
            ["E"] = CreateDot("E", 0, 4),
            ["F"] = CreateDot("F", 0, 5),
            ["G"] = CreateDot("G", 0, 6),
            ["H"] = CreateDot("H", 0, 7)
        };

        AddBidirectionalEdge(adjacency, "A", "B", 1);
        AddBidirectionalEdge(adjacency, "B", "C", 1);
        AddBidirectionalEdge(adjacency, "C", "D", 1);
        AddBidirectionalEdge(adjacency, "D", "H", 2);

        AddBidirectionalEdge(adjacency, "B", "E", 3);
        AddBidirectionalEdge(adjacency, "E", "F", 3);
        AddBidirectionalEdge(adjacency, "F", "G", 3);
        AddBidirectionalEdge(adjacency, "G", "H", 3);

        var context = new MapRouteContext { Adjacency = adjacency, DotLookup = dotLookup };

        var suggestion = service.FindCheapestSuggestion(
            context,
            new RouteSuggestionRequest
            {
                PlayerId = "player-1",
                StartNodeId = "A",
                DestinationNodeId = "H",
                MovementType = PlayerMovementType.TwoDie,
                MovementCapacity = 2,
                AverageFutureMovement = 7.0,
                PlayerColor = "#000000",
                ResolveRailroadOwnership = railroadIndex => railroadIndex switch
                {
                    1 => RailroadOwnershipCategory.Unfriendly,
                    _ => RailroadOwnershipCategory.Public
                },
                ResolveRailroadFee = railroadIndex => railroadIndex switch
                {
                    1 => 5000,
                    _ => 1000
                }
            });

        Assert.Equal(RouteSuggestionStatus.Success, suggestion.Status);
        Assert.Equal([1, 3, 3, 3, 3], suggestion.Segments.Select(segment => segment.RailroadIndex).ToArray());
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

    private static MapRouteContext CreateTieBreakContext()
    {
        var adjacency = new Dictionary<string, List<RouteGraphEdge>>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = [], ["B"] = [], ["C"] = [], ["D"] = [], ["E"] = []
        };
        var dotLookup = new Dictionary<string, TrainDot>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = CreateDot("A", 0, 0),
            ["B"] = CreateDot("B", 0, 1),
            ["C"] = CreateDot("C", 0, 2),
            ["D"] = CreateDot("D", 0, 3),
            ["E"] = CreateDot("E", 0, 4)
        };

        AddEdge(adjacency, "A", "B", 1);
        AddEdge(adjacency, "B", "D", 2);
        AddEdge(adjacency, "A", "C", 1);
        AddEdge(adjacency, "C", "D", 3);
        AddEdge(adjacency, "D", "E", 4);

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
            SegmentKey = string.Compare(fromNodeId, toNodeId, StringComparison.OrdinalIgnoreCase) <= 0
                ? string.Concat(fromNodeId, "-", toNodeId, ":", railroadIndex.ToString(CultureInfo.InvariantCulture))
                : string.Concat(toNodeId, "-", fromNodeId, ":", railroadIndex.ToString(CultureInfo.InvariantCulture)),
            RailroadIndex = railroadIndex,
            X1 = 0,
            Y1 = 0,
            X2 = 1,
            Y2 = 1
        });
    }

    private static void AddBidirectionalEdge(Dictionary<string, List<RouteGraphEdge>> adjacency, string fromNodeId, string toNodeId, int railroadIndex)
    {
        AddEdge(adjacency, fromNodeId, toNodeId, railroadIndex);
        AddEdge(adjacency, toNodeId, fromNodeId, railroadIndex);
    }
}
