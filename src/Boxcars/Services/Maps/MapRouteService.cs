using Boxcars.Data.Maps;

namespace Boxcars.Services.Maps;

public sealed class MapRouteService
{
    public MapRouteContext BuildContext(MapDefinition mapDefinition)
    {
        var dotLookup = mapDefinition.TrainDots.ToDictionary(
            dot => NodeKey(dot.RegionIndex, dot.DotIndex),
            dot => dot,
            StringComparer.OrdinalIgnoreCase);

        var edges = new List<RouteGraphEdge>();
        var adjacency = new Dictionary<string, List<RouteGraphEdge>>(StringComparer.OrdinalIgnoreCase);

        foreach (var segment in mapDefinition.RailroadRouteSegments)
        {
            var fromNodeId = NodeKey(segment.StartRegionIndex, segment.StartDotIndex);
            var toNodeId = NodeKey(segment.EndRegionIndex, segment.EndDotIndex);

            if (string.Equals(fromNodeId, toNodeId, StringComparison.OrdinalIgnoreCase)
                || !dotLookup.TryGetValue(fromNodeId, out var fromDot)
                || !dotLookup.TryGetValue(toNodeId, out var toDot))
            {
                continue;
            }

            var forward = new RouteGraphEdge
            {
                FromNodeId = fromNodeId,
                ToNodeId = toNodeId,
                RailroadIndex = segment.RailroadIndex,
                X1 = fromDot.X,
                Y1 = fromDot.Y,
                X2 = toDot.X,
                Y2 = toDot.Y
            };

            var reverse = new RouteGraphEdge
            {
                FromNodeId = toNodeId,
                ToNodeId = fromNodeId,
                RailroadIndex = segment.RailroadIndex,
                X1 = toDot.X,
                Y1 = toDot.Y,
                X2 = fromDot.X,
                Y2 = fromDot.Y
            };

            edges.Add(forward);
            edges.Add(reverse);

            AddAdjacency(adjacency, forward);
            AddAdjacency(adjacency, reverse);
        }

        foreach (var node in adjacency.Values)
        {
            node.Sort(static (left, right) =>
            {
                var toCompare = string.Compare(left.ToNodeId, right.ToNodeId, StringComparison.OrdinalIgnoreCase);
                if (toCompare != 0)
                {
                    return toCompare;
                }

                return left.RailroadIndex.CompareTo(right.RailroadIndex);
            });
        }

        return new MapRouteContext
        {
            Adjacency = adjacency,
            DotLookup = dotLookup
        };
    }

    public RouteSelection? FindShortestSelection(
        MapRouteContext context,
        string fromNodeId,
        string toNodeId,
        int? preferredStartingRailroadIndex = null,
        IReadOnlySet<int>? selectedRailroadIndices = null,
        Func<int, bool>? isRailroadOwnedByPlayer = null)
    {
        selectedRailroadIndices ??= new HashSet<int>();
        isRailroadOwnedByPlayer ??= static _ => true;

        var hasPreferenceCriteria = preferredStartingRailroadIndex.HasValue
            || selectedRailroadIndices.Count > 0;

        if (!hasPreferenceCriteria)
        {
            return FindShortestSelectionCore(context, fromNodeId, toNodeId);
        }

        if (string.IsNullOrWhiteSpace(fromNodeId)
            || string.IsNullOrWhiteSpace(toNodeId)
            || !context.Adjacency.TryGetValue(fromNodeId, out var fromEdges)
            || !context.Adjacency.ContainsKey(toNodeId))
        {
            return null;
        }

        if (string.Equals(fromNodeId, toNodeId, StringComparison.OrdinalIgnoreCase))
        {
            return new RouteSelection
            {
                NodeIds = [fromNodeId],
                Segments = []
            };
        }
        RouteSelection? bestSelection = null;
        var bestCost = new RouteSelectionCost(int.MaxValue, int.MaxValue, int.MaxValue);

        foreach (var fromEdge in fromEdges)
        {
            var tailSelection = FindShortestSelectionCoreWithRailroadBias(
                context,
                fromEdge.ToNodeId,
                toNodeId,
                fromEdge.RailroadIndex);

            if (tailSelection is null)
            {
                continue;
            }

            var preferenceRank = CalculateRailroadPreferenceRank(
                fromEdge.RailroadIndex,
                preferredStartingRailroadIndex,
                selectedRailroadIndices,
                isRailroadOwnedByPlayer);
            var switchFromPrevious = preferredStartingRailroadIndex.HasValue
                && fromEdge.RailroadIndex != preferredStartingRailroadIndex.Value
                    ? 1
                    : 0;
            var cost = new RouteSelectionCost(
                SegmentCount: 1 + tailSelection.Segments.Count,
                PreferenceRank: preferenceRank,
                RailroadSwitchCount: switchFromPrevious + CountRailroadSwitches(tailSelection.Segments, fromEdge.RailroadIndex));

            if (!IsBetterCost(cost, bestCost))
            {
                continue;
            }

            bestCost = cost;
            bestSelection = new RouteSelection
            {
                NodeIds = [fromNodeId, .. tailSelection.NodeIds],
                Segments = [fromEdge, .. tailSelection.Segments]
            };
        }

        return bestSelection;
    }

    private static bool IsBetterCost(RouteSelectionCost candidate, RouteSelectionCost current)
    {
        if (candidate.SegmentCount != current.SegmentCount)
        {
            return candidate.SegmentCount < current.SegmentCount;
        }

        if (candidate.PreferenceRank != current.PreferenceRank)
        {
            return candidate.PreferenceRank < current.PreferenceRank;
        }

        return candidate.RailroadSwitchCount < current.RailroadSwitchCount;
    }

    private static int CountRailroadSwitches(IReadOnlyList<RouteGraphEdge> segments, int initialRailroadIndex)
    {
        var switches = 0;
        var previousRailroadIndex = initialRailroadIndex;

        foreach (var segment in segments)
        {
            if (segment.RailroadIndex != previousRailroadIndex)
            {
                switches++;
            }

            previousRailroadIndex = segment.RailroadIndex;
        }

        return switches;
    }

    private static int CalculateRailroadPreferenceRank(
        int railroadIndex,
        int? preferredStartingRailroadIndex,
        IReadOnlySet<int> selectedRailroadIndices,
        Func<int, bool> isRailroadOwnedByPlayer)
    {
        if (preferredStartingRailroadIndex.HasValue
            && railroadIndex == preferredStartingRailroadIndex.Value)
        {
            return 0;
        }

        if (selectedRailroadIndices.Contains(railroadIndex))
        {
            return 1;
        }

        if (!isRailroadOwnedByPlayer(railroadIndex))
        {
            return 2;
        }

        return 3;
    }

    private static RouteSelection? FindShortestSelectionCore(MapRouteContext context, string fromNodeId, string toNodeId)
    {
        if (string.IsNullOrWhiteSpace(fromNodeId)
            || string.IsNullOrWhiteSpace(toNodeId)
            || !context.Adjacency.ContainsKey(fromNodeId)
            || !context.Adjacency.ContainsKey(toNodeId))
        {
            return null;
        }

        if (string.Equals(fromNodeId, toNodeId, StringComparison.OrdinalIgnoreCase))
        {
            return new RouteSelection
            {
                NodeIds = [fromNodeId],
                Segments = []
            };
        }

        var queue = new Queue<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var previous = new Dictionary<string, RouteGraphEdge>(StringComparer.OrdinalIgnoreCase);

        queue.Enqueue(fromNodeId);
        visited.Add(fromNodeId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (string.Equals(current, toNodeId, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (!context.Adjacency.TryGetValue(current, out var nextEdges))
            {
                continue;
            }

            foreach (var edge in nextEdges)
            {
                if (!visited.Add(edge.ToNodeId))
                {
                    continue;
                }

                previous[edge.ToNodeId] = edge;
                queue.Enqueue(edge.ToNodeId);
            }
        }

        if (!visited.Contains(toNodeId))
        {
            return null;
        }

        var segmentStack = new Stack<RouteGraphEdge>();
        var currentNode = toNodeId;

        while (!string.Equals(currentNode, fromNodeId, StringComparison.OrdinalIgnoreCase))
        {
            if (!previous.TryGetValue(currentNode, out var edge))
            {
                return null;
            }

            segmentStack.Push(edge);
            currentNode = edge.FromNodeId;
        }

        var nodeIds = new List<string> { fromNodeId };
        var segments = new List<RouteGraphEdge>();

        while (segmentStack.Count > 0)
        {
            var edge = segmentStack.Pop();
            segments.Add(edge);
            nodeIds.Add(edge.ToNodeId);
        }

        return new RouteSelection
        {
            NodeIds = nodeIds,
            Segments = segments
        };
    }

    private static RouteSelection? FindShortestSelectionCoreWithRailroadBias(
        MapRouteContext context,
        string fromNodeId,
        string toNodeId,
        int initialRailroadIndex)
    {
        if (string.IsNullOrWhiteSpace(fromNodeId)
            || string.IsNullOrWhiteSpace(toNodeId)
            || !context.Adjacency.ContainsKey(fromNodeId)
            || !context.Adjacency.ContainsKey(toNodeId))
        {
            return null;
        }

        if (string.Equals(fromNodeId, toNodeId, StringComparison.OrdinalIgnoreCase))
        {
            return new RouteSelection
            {
                NodeIds = [fromNodeId],
                Segments = []
            };
        }

        var queue = new PriorityQueue<RouteSearchState, (int SegmentCount, int RailroadSwitchCount)>();
        var bestCosts = new Dictionary<RouteSearchState, (int SegmentCount, int RailroadSwitchCount)>();
        var previous = new Dictionary<RouteSearchState, (RouteSearchState PreviousState, RouteGraphEdge Edge)>();

        var startState = new RouteSearchState(fromNodeId, initialRailroadIndex);
        bestCosts[startState] = (0, 0);
        queue.Enqueue(startState, (0, 0));

        RouteSearchState? targetState = null;

        while (queue.Count > 0)
        {
            var state = queue.Dequeue();
            var stateCost = bestCosts[state];

            if (string.Equals(state.NodeId, toNodeId, StringComparison.OrdinalIgnoreCase))
            {
                targetState = state;
                break;
            }

            if (!context.Adjacency.TryGetValue(state.NodeId, out var nextEdges))
            {
                continue;
            }

            foreach (var edge in nextEdges)
            {
                var nextState = new RouteSearchState(edge.ToNodeId, edge.RailroadIndex);
                var nextCost = (
                    SegmentCount: stateCost.SegmentCount + 1,
                    RailroadSwitchCount: stateCost.RailroadSwitchCount + (state.LastRailroadIndex == edge.RailroadIndex ? 0 : 1));

                if (bestCosts.TryGetValue(nextState, out var existingCost)
                    && (existingCost.SegmentCount < nextCost.SegmentCount
                        || (existingCost.SegmentCount == nextCost.SegmentCount
                            && existingCost.RailroadSwitchCount <= nextCost.RailroadSwitchCount)))
                {
                    continue;
                }

                bestCosts[nextState] = nextCost;
                previous[nextState] = (state, edge);
                queue.Enqueue(nextState, nextCost);
            }
        }

        if (targetState is null)
        {
            return null;
        }

        var segmentStack = new Stack<RouteGraphEdge>();
        var current = targetState.Value;

        while (current != startState)
        {
            if (!previous.TryGetValue(current, out var previousEntry))
            {
                return null;
            }

            segmentStack.Push(previousEntry.Edge);
            current = previousEntry.PreviousState;
        }

        var nodeIds = new List<string> { fromNodeId };
        var segments = new List<RouteGraphEdge>();

        while (segmentStack.Count > 0)
        {
            var edge = segmentStack.Pop();
            segments.Add(edge);
            nodeIds.Add(edge.ToNodeId);
        }

        return new RouteSelection
        {
            NodeIds = nodeIds,
            Segments = segments
        };
    }

    private readonly record struct RouteSelectionCost(int SegmentCount, int PreferenceRank, int RailroadSwitchCount);
    private readonly record struct RouteSearchState(string NodeId, int LastRailroadIndex);

    public RouteSelection TruncateToNode(RouteSelection selection, string nodeId)
    {
        var index = selection.NodeIds.FindIndex(existing => string.Equals(existing, nodeId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return selection;
        }

        if (index == selection.NodeIds.Count - 1)
        {
            return selection;
        }

        return new RouteSelection
        {
            NodeIds = selection.NodeIds.Take(index + 1).ToList(),
            Segments = selection.Segments.Take(index).ToList()
        };
    }

    public static string NodeKey(int regionIndex, int dotIndex)
    {
        return $"{regionIndex}:{dotIndex}";
    }

    private static void AddAdjacency(Dictionary<string, List<RouteGraphEdge>> adjacency, RouteGraphEdge edge)
    {
        if (!adjacency.TryGetValue(edge.FromNodeId, out var list))
        {
            list = new List<RouteGraphEdge>();
            adjacency[edge.FromNodeId] = list;
        }

        list.Add(edge);
    }
}

public sealed class MapRouteContext
{
    public required IReadOnlyDictionary<string, List<RouteGraphEdge>> Adjacency { get; init; }
    public required IReadOnlyDictionary<string, TrainDot> DotLookup { get; init; }
}

public sealed class RouteGraphEdge
{
    public required string FromNodeId { get; init; }
    public required string ToNodeId { get; init; }
    public required int RailroadIndex { get; init; }
    public required double X1 { get; init; }
    public required double Y1 { get; init; }
    public required double X2 { get; init; }
    public required double Y2 { get; init; }
}

public sealed class RouteSelection
{
    public required List<string> NodeIds { get; init; }
    public required List<RouteGraphEdge> Segments { get; init; }
}
