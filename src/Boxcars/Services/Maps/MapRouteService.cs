using Boxcars.Data.Maps;
using Boxcars.Engine.Data.Maps;
using System.Globalization;
using System.Diagnostics;

namespace Boxcars.Services.Maps;

public sealed class MapRouteService
{
    public const int RoutePlanningMaximumSuggestedSegments = 40;
    private const int RoutePlanningDirectnessPenaltyPerSegment = 250;
    private const int RoutePlanningHostileExitPenalty = 1500;
    private const int RoutePlanningMaximumExploredStates = 500000;
    private const int RoutePlanningMaximumSearchMilliseconds = 3000;

    public MapRouteContext BuildContext(MapDefinition mapDefinition)
    {
        var dotLookup = mapDefinition.TrainDots.ToDictionary(
            dot => NodeKey(dot.RegionIndex, dot.DotIndex),
            dot => dot,
            StringComparer.OrdinalIgnoreCase);

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

            var segmentKey = BuildSegmentKey(fromNodeId, toNodeId, segment.RailroadIndex);

            var forward = new RouteGraphEdge
            {
                FromNodeId = fromNodeId,
                ToNodeId = toNodeId,
                SegmentKey = segmentKey,
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
                SegmentKey = segmentKey,
                RailroadIndex = segment.RailroadIndex,
                X1 = toDot.X,
                Y1 = toDot.Y,
                X2 = fromDot.X,
                Y2 = fromDot.Y
            };

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

    public RouteSuggestionResult FindCheapestSuggestion(MapRouteContext context, RouteSuggestionRequest request)
    {
        var cachedRequest = CreateCachedRouteSuggestionRequest(request);
        var searchBudget = CreateRouteSearchBudget(cachedRequest);

        return FindCheapestSuggestionCore(
            context,
            cachedRequest,
            requireCurrentTurnContinuation: true,
            emitDebug: true,
            searchBudget);
    }

    private static RouteSearchBudget CreateRouteSearchBudget(RouteSuggestionRequest request)
    {
        var maximumExploredStates = request.MaximumExploredStates > 0
            ? request.MaximumExploredStates
            : RoutePlanningMaximumExploredStates;
        var maximumSearchMilliseconds = request.MaximumSearchMilliseconds > 0
            ? request.MaximumSearchMilliseconds
            : RoutePlanningMaximumSearchMilliseconds;

        return new RouteSearchBudget(maximumExploredStates, TimeSpan.FromMilliseconds(maximumSearchMilliseconds));
    }

    private static RouteSuggestionRequest CreateCachedRouteSuggestionRequest(RouteSuggestionRequest request)
    {
        Dictionary<int, RailroadOwnershipCategory>? ownershipCache = null;

        RailroadOwnershipCategory ResolveRailroadOwnershipCached(int railroadIndex)
        {
            ownershipCache ??= [];
            if (!ownershipCache.TryGetValue(railroadIndex, out var ownershipCategory))
            {
                ownershipCategory = request.ResolveRailroadOwnership(railroadIndex);
                ownershipCache[railroadIndex] = ownershipCategory;
            }

            return ownershipCategory;
        }

        return new RouteSuggestionRequest
        {
            PlayerId = request.PlayerId,
            StartNodeId = request.StartNodeId,
            DestinationNodeId = request.DestinationNodeId,
            MovementType = request.MovementType,
            MovementCapacity = request.MovementCapacity,
            AverageFutureMovement = request.AverageFutureMovement,
            TraveledSegmentKeys = request.TraveledSegmentKeys,
            PlayerColor = request.PlayerColor,
            ResolveRailroadOwnership = ResolveRailroadOwnershipCached,
            ResolveRailroadFee = request.ResolveRailroadFee,
            ResolveRailroadOwnerPlayerIndex = request.ResolveRailroadOwnerPlayerIndex,
            ResolvePlayerCash = request.ResolvePlayerCash,
            ResolvePlayerAccessibleDestinationPercent = request.ResolvePlayerAccessibleDestinationPercent,
            ResolvePlayerMonopolyDestinationPercent = request.ResolvePlayerMonopolyDestinationPercent,
            MaximumExploredStates = request.MaximumExploredStates,
            MaximumSearchMilliseconds = request.MaximumSearchMilliseconds,
            BonusOutAvailable = request.BonusOutAvailable,
            CurrentWhiteDiceMovement = request.CurrentWhiteDiceMovement,
            CurrentFixedBonusMovement = request.CurrentFixedBonusMovement,
            BonusOutRequiresWhiteDiceArrival = request.BonusOutRequiresWhiteDiceArrival
        };
    }

    private RouteSuggestionResult FindCheapestSuggestionCore(
        MapRouteContext context,
        RouteSuggestionRequest request,
        bool requireCurrentTurnContinuation,
        bool emitDebug,
        RouteSearchBudget searchBudget)
    {
        if (string.IsNullOrWhiteSpace(request.StartNodeId)
            || string.IsNullOrWhiteSpace(request.DestinationNodeId)
            || !context.Adjacency.ContainsKey(request.StartNodeId)
            || !context.Adjacency.ContainsKey(request.DestinationNodeId))
        {
            return new RouteSuggestionResult
            {
                Status = RouteSuggestionStatus.Error,
                Message = "Invalid start or destination node.",
                StartNodeId = request.StartNodeId,
                DestinationNodeId = request.DestinationNodeId
            };
        }

        if (string.Equals(request.StartNodeId, request.DestinationNodeId, StringComparison.OrdinalIgnoreCase))
        {
            return new RouteSuggestionResult
            {
                Status = RouteSuggestionStatus.Success,
                StartNodeId = request.StartNodeId,
                DestinationNodeId = request.DestinationNodeId,
                NodeIds = [request.StartNodeId],
                Segments = [],
                TotalTurns = 0,
                TotalCost = 0
            };
        }

        var traveledSegmentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var segmentKey in request.TraveledSegmentKeys)
        {
            if (!string.IsNullOrWhiteSpace(segmentKey))
            {
                traveledSegmentKeys.Add(segmentKey);
            }
        }
        var friendlyDestinationReachableNodes = BuildReachableNodesForRoutePlanning(
            context,
            request.DestinationNodeId,
            edge => request.ResolveRailroadOwnership(edge.RailroadIndex) != RailroadOwnershipCategory.Unfriendly,
            searchBudget);

        if (searchBudget.IsExceeded)
        {
            return CreateBudgetExceededSuggestion(request, searchBudget);
        }

        var futureTurnMovementCapacity = request.AverageFutureMovement > 0
            ? (int)Math.Round(request.AverageFutureMovement, MidpointRounding.AwayFromZero)
            : request.MovementType == PlayerMovementType.ThreeDie ? 11 : 7;
        var firstTurnMovementCapacity = request.MovementCapacity > 0
            ? request.MovementCapacity
            : futureTurnMovementCapacity;

        var startState = new RouteSuggestionState(
            request.StartNodeId,
            LastRailroadIndex: -1,
            PointsUsedInCurrentTurn: 0);
        var priorityQueue = new PriorityQueue<RouteSuggestionState, (int WeightedCost, int TotalCost, int TotalTurns, int TotalSegments)>();
        var bestCosts = new Dictionary<RouteSuggestionState, RouteSuggestionCostKey>();
        var previous = new Dictionary<RouteSuggestionState, RouteSuggestionPrevious>();
        var settledStates = new HashSet<RouteSuggestionState>();
        RouteSuggestionDestinationChoice? bestDestination = null;
#if DEBUG
        var debugCandidates = new List<RouteSuggestionDestinationChoice>();
        var debugStats = new RouteSuggestionDebugStats();
#endif

        var startCost = new RouteSuggestionCostKey(
            TotalCost: 0,
            WeightedCost: 0,
            TotalTurns: 0,
            TotalSegments: 0);

        bestCosts[startState] = startCost;
        priorityQueue.Enqueue(startState, (0, 0, 0, 0));

        while (priorityQueue.Count > 0)
        {
            if (!searchBudget.TryStartState())
            {
#if DEBUG
                ApplyBudgetExceededDebugStats(debugStats, searchBudget);
#endif
                return CreateBudgetExceededSuggestion(request, searchBudget);
            }

            var state = priorityQueue.Dequeue();

#if DEBUG
            debugStats.DequeuedStates++;
#endif

            if (!bestCosts.TryGetValue(state, out var stateCost))
            {
#if DEBUG
                debugStats.MissingStateCostSkips++;
#endif
                continue;
            }

            if (!settledStates.Add(state))
            {
#if DEBUG
                debugStats.AlreadySettledSkips++;
#endif
                continue;
            }

#if DEBUG
            debugStats.SettledStates++;
#endif

            if (bestDestination is not null && stateCost.TotalCost > bestDestination.Value.CombinedCost)
            {
#if DEBUG
                debugStats.StoppedByBestCostBound = true;
#endif
                break;
            }

            if (string.Equals(state.NodeId, request.DestinationNodeId, StringComparison.OrdinalIgnoreCase))
            {
                if (stateCost.TotalSegments > RoutePlanningMaximumSuggestedSegments)
                {
#if DEBUG
                    debugStats.DestinationRejectedBySegmentCap++;
#endif
                    continue;
                }

                if (requireCurrentTurnContinuation
                    && !HasCurrentTurnContinuation(
                        context,
                        request,
                        startState,
                        state,
                        previous,
                        firstTurnMovementCapacity,
                        futureTurnMovementCapacity,
                        searchBudget))
                {
#if DEBUG
                    if (searchBudget.IsExceeded)
                    {
                        ApplyBudgetExceededDebugStats(debugStats, searchBudget);
                        return CreateBudgetExceededSuggestion(request, searchBudget);
                    }

                    debugStats.DestinationRejectedByContinuation++;
#endif
                    continue;
                }

                var exitAnalysis = CalculateDestinationExitAnalysis(context, request, state, stateCost, searchBudget);
                if (searchBudget.IsExceeded)
                {
#if DEBUG
                    ApplyBudgetExceededDebugStats(debugStats, searchBudget);
#endif
                    return CreateBudgetExceededSuggestion(request, searchBudget);
                }

                var lookaheadPenalty = CalculateTwoTurnHostileExposurePenalty(
                    context,
                    request,
                    startState,
                    state,
                    previous,
                    friendlyDestinationReachableNodes,
                    firstTurnMovementCapacity,
                    futureTurnMovementCapacity,
                    searchBudget);
                if (searchBudget.IsExceeded)
                {
#if DEBUG
                    ApplyBudgetExceededDebugStats(debugStats, searchBudget);
#endif
                    return CreateBudgetExceededSuggestion(request, searchBudget);
                }

                var tieBreakAnalysis = CalculateTieBreakAnalysis(request, startState, state, previous);
                var destinationChoice = new RouteSuggestionDestinationChoice(state, stateCost, exitAnalysis, tieBreakAnalysis, lookaheadPenalty);
#if DEBUG
                debugCandidates.Add(destinationChoice);
#endif

                if (bestDestination is null || IsBetterDestination(destinationChoice, bestDestination.Value))
                {
                    bestDestination = destinationChoice;
                }

                continue;
            }

            if (!context.Adjacency.TryGetValue(state.NodeId, out var outgoingEdges))
            {
#if DEBUG
                debugStats.StatesWithoutOutgoingEdges++;
#endif
                continue;
            }

            foreach (var edge in outgoingEdges)
            {
                if (!searchBudget.TryContinue())
                {
#if DEBUG
                    ApplyBudgetExceededDebugStats(debugStats, searchBudget);
#endif
                    return CreateBudgetExceededSuggestion(request, searchBudget);
                }

#if DEBUG
                debugStats.EdgesConsidered++;
#endif
                if (traveledSegmentKeys.Contains(edge.SegmentKey))
                {
#if DEBUG
                    debugStats.TraveledSegmentPrunes++;
#endif
                    continue;
                }

                var ownershipCategory = request.ResolveRailroadOwnership(edge.RailroadIndex);
                var costPerTurn = ResolveRailroadFee(request, edge.RailroadIndex, ownershipCategory);

                var currentTurnMovementCapacity = stateCost.TotalTurns <= 1
                    ? firstTurnMovementCapacity
                    : futureTurnMovementCapacity;
                var isFirstEdge = state.LastRailroadIndex < 0;
                var exhaustedTurnCapacity = !isFirstEdge && state.PointsUsedInCurrentTurn >= currentTurnMovementCapacity;
                var startsNewTurn = isFirstEdge || exhaustedTurnCapacity;
                var switchedRailroad = !isFirstEdge && state.LastRailroadIndex != edge.RailroadIndex;
                var hostileExitPenalty = CalculateHostileExitPenalty(request, outgoingEdges, edge, friendlyDestinationReachableNodes);

                // Rail Baron cost model: $1000/turn for public/own RR, $5000/turn for other player's RR.
                // Fee charged when starting a new turn OR switching to a different railroad mid-turn.
                // Switching railroads does NOT end the turn or reset movement points.
                var turnsAdded = startsNewTurn ? 1 : 0;
                var additionalCost = (startsNewTurn || switchedRailroad) ? costPerTurn : 0;
                var nextPointsUsed = startsNewTurn ? 1 : state.PointsUsedInCurrentTurn + 1;

                var nextState = new RouteSuggestionState(
                    edge.ToNodeId,
                    edge.RailroadIndex,
                    nextPointsUsed);
                var candidateCost = new RouteSuggestionCostKey(
                    TotalCost: stateCost.TotalCost + additionalCost,
                    WeightedCost: stateCost.WeightedCost + additionalCost + RoutePlanningDirectnessPenaltyPerSegment + hostileExitPenalty,
                    TotalTurns: stateCost.TotalTurns + turnsAdded,
                    TotalSegments: stateCost.TotalSegments + 1);

                if (candidateCost.TotalSegments > RoutePlanningMaximumSuggestedSegments)
                {
#if DEBUG
                    debugStats.SegmentCapPrunes++;
#endif
                    continue;
                }

                if (bestCosts.TryGetValue(nextState, out var existingCost)
                    && !IsBetterCost(candidateCost, existingCost))
                {
#if DEBUG
                    debugStats.DominatedCostPrunes++;
#endif
                    continue;
                }

                bestCosts[nextState] = candidateCost;
                previous[nextState] = new RouteSuggestionPrevious(
                    PreviousState: state,
                    Edge: edge,
                    OwnershipCategory: ownershipCategory,
                    TurnsAdded: turnsAdded,
                    CostPerTurn: costPerTurn,
                    TotalCostAdded: additionalCost);
                priorityQueue.Enqueue(nextState, (candidateCost.WeightedCost, candidateCost.TotalCost, candidateCost.TotalTurns, candidateCost.TotalSegments));
            }
        }

        if (bestDestination is not null)
        {
#if DEBUG
            if (emitDebug)
            {
                WriteDebugCandidateTrace(request, startState, bestDestination.Value, debugCandidates, previous, debugStats);
            }
#endif
            return ReconstructSuggestion(
                request,
                startState,
                bestDestination.Value.DestinationState,
                bestDestination.Value.ArrivalCost,
                bestDestination.Value.ExitAnalysis,
                previous);
        }

#if DEBUG
        if (emitDebug)
        {
            WriteDebugCandidateTrace(request, startState, bestDestination, debugCandidates, previous, debugStats);

            var shortestSelection = FindShortestSelection(context, request.StartNodeId, request.DestinationNodeId);
            if (shortestSelection is null)
            {
                Debug.WriteLine("[MapRouteService] Baseline shortest path: none (destination unreachable even without route-suggestion constraints).");
            }
            else
            {
                var shortestRailroads = shortestSelection.Segments
                    .Select(segment => segment.RailroadIndex.ToString(CultureInfo.InvariantCulture))
                    .ToArray();
                Debug.WriteLine(
                    $"[MapRouteService] Baseline shortest path: segments={shortestSelection.Segments.Count}, nodes={string.Join("->", shortestSelection.NodeIds)}, rr={string.Join(",", shortestRailroads)}");
            }
        }
#endif

        return new RouteSuggestionResult
        {
            Status = RouteSuggestionStatus.NoRoute,
            Message = "No valid route available to destination.",
            StartNodeId = request.StartNodeId,
            DestinationNodeId = request.DestinationNodeId,
            NodeIds = [request.StartNodeId],
            Segments = [],
            TotalCost = 0,
            TotalTurns = 0
        };
    }

    private bool HasCurrentTurnContinuation(
        MapRouteContext context,
        RouteSuggestionRequest request,
        RouteSuggestionState startState,
        RouteSuggestionState destinationState,
        Dictionary<RouteSuggestionState, RouteSuggestionPrevious> previous,
        int firstTurnMovementCapacity,
        int futureTurnMovementCapacity,
        RouteSearchBudget searchBudget)
    {
        var orderedEntries = ReconstructPathEntries(startState, destinationState, previous);
        if (orderedEntries.Count == 0)
        {
            return false;
        }

        var currentTurnProgress = GetCurrentTurnProgress(orderedEntries, firstTurnMovementCapacity, futureTurnMovementCapacity);
        if (currentTurnProgress is null || currentTurnProgress.Value.ReachedDestinationThisTurn)
        {
            return true;
        }

        var augmentedTraveledSegmentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var segmentKey in request.TraveledSegmentKeys)
        {
            if (!string.IsNullOrWhiteSpace(segmentKey))
            {
                augmentedTraveledSegmentKeys.Add(segmentKey);
            }
        }

        foreach (var segmentKey in currentTurnProgress.Value.ConsumedSegmentKeys)
        {
            if (!string.IsNullOrWhiteSpace(segmentKey))
            {
                augmentedTraveledSegmentKeys.Add(segmentKey);
            }
        }

        var continuation = FindCheapestSuggestionCore(
            context,
            new RouteSuggestionRequest
            {
                PlayerId = request.PlayerId,
                StartNodeId = currentTurnProgress.Value.EndpointNodeId,
                DestinationNodeId = request.DestinationNodeId,
                MovementType = request.MovementType,
                MovementCapacity = 0,
                AverageFutureMovement = request.AverageFutureMovement,
                TraveledSegmentKeys = [.. augmentedTraveledSegmentKeys],
                PlayerColor = request.PlayerColor,
                ResolveRailroadOwnership = request.ResolveRailroadOwnership,
                ResolveRailroadFee = request.ResolveRailroadFee,
                ResolveRailroadOwnerPlayerIndex = request.ResolveRailroadOwnerPlayerIndex,
                ResolvePlayerCash = request.ResolvePlayerCash,
                ResolvePlayerAccessibleDestinationPercent = request.ResolvePlayerAccessibleDestinationPercent,
                ResolvePlayerMonopolyDestinationPercent = request.ResolvePlayerMonopolyDestinationPercent,
                MaximumExploredStates = request.MaximumExploredStates,
                MaximumSearchMilliseconds = request.MaximumSearchMilliseconds,
                BonusOutAvailable = request.BonusOutAvailable,
                CurrentWhiteDiceMovement = request.CurrentWhiteDiceMovement,
                CurrentFixedBonusMovement = request.CurrentFixedBonusMovement,
                BonusOutRequiresWhiteDiceArrival = request.BonusOutRequiresWhiteDiceArrival
            },
            requireCurrentTurnContinuation: false,
            emitDebug: false,
            searchBudget);

        return continuation.Status == RouteSuggestionStatus.Success;
    }

    private static RouteSuggestionResult CreateBudgetExceededSuggestion(RouteSuggestionRequest request, RouteSearchBudget searchBudget)
    {
        return new RouteSuggestionResult
        {
            Status = RouteSuggestionStatus.NoRoute,
            Message = searchBudget.StopReason switch
            {
                RouteSearchBudgetStopReason.ExplorationBudget => "Route search exceeded the exploration budget before a safe suggestion could be found.",
                RouteSearchBudgetStopReason.Timeout => "Route search timed out before a safe suggestion could be found.",
                _ => "Route search stopped before a safe suggestion could be found."
            },
            StartNodeId = request.StartNodeId,
            DestinationNodeId = request.DestinationNodeId,
            NodeIds = [request.StartNodeId],
            Segments = [],
            TotalCost = 0,
            TotalTurns = 0
        };
    }

    private static RouteSuggestionCurrentTurnProgress? GetCurrentTurnProgress(
        List<RouteSuggestionPrevious> orderedEntries,
        int firstTurnMovementCapacity,
        int futureTurnMovementCapacity)
    {
        if (orderedEntries.Count == 0)
        {
            return null;
        }

        var lastNodeId = orderedEntries[0].PreviousState.NodeId;
        var lastRailroadIndex = orderedEntries[0].PreviousState.LastRailroadIndex;
        var pointsUsedInCurrentTurn = 0;
        var turnsStarted = 0;
        var consumedSegmentKeys = new List<string>();
        var consumedEntryCount = 0;

        foreach (var entry in orderedEntries)
        {
            var currentTurnCapacity = turnsStarted <= 1 ? firstTurnMovementCapacity : futureTurnMovementCapacity;
            var isFirstEdge = lastRailroadIndex < 0;
            var exhaustedTurnCapacity = !isFirstEdge && pointsUsedInCurrentTurn >= currentTurnCapacity;
            var startsNewTurn = isFirstEdge || exhaustedTurnCapacity;

            if (turnsStarted >= 1 && startsNewTurn)
            {
                break;
            }

            if (startsNewTurn)
            {
                turnsStarted++;
                pointsUsedInCurrentTurn = 1;
            }
            else
            {
                pointsUsedInCurrentTurn++;
            }

            consumedEntryCount++;
            consumedSegmentKeys.Add(entry.Edge.SegmentKey);
            lastNodeId = entry.Edge.ToNodeId;
            lastRailroadIndex = entry.Edge.RailroadIndex;
        }

        return new RouteSuggestionCurrentTurnProgress(
            lastNodeId,
            consumedSegmentKeys,
            consumedEntryCount >= orderedEntries.Count);
    }

    private static RouteSuggestionResult ReconstructSuggestion(
        RouteSuggestionRequest request,
        RouteSuggestionState startState,
        RouteSuggestionState destinationState,
        RouteSuggestionCostKey destinationCost,
        RouteSuggestionExitAnalysis exitAnalysis,
        Dictionary<RouteSuggestionState, RouteSuggestionPrevious> previous)
    {
        var nodeStack = new Stack<string>();
        var segmentStack = new Stack<RouteSuggestionSegment>();
        var currentState = destinationState;

        nodeStack.Push(currentState.NodeId);

        while (currentState != startState)
        {
            if (!previous.TryGetValue(currentState, out var previousEntry))
            {
                return new RouteSuggestionResult
                {
                    Status = RouteSuggestionStatus.Error,
                    Message = "Unable to reconstruct route suggestion.",
                    StartNodeId = request.StartNodeId,
                    DestinationNodeId = request.DestinationNodeId,
                    NodeIds = [request.StartNodeId],
                    Segments = []
                };
            }

            if (segmentStack.Count >= RoutePlanningMaximumSuggestedSegments)
            {
                return new RouteSuggestionResult
                {
                    Status = RouteSuggestionStatus.NoRoute,
                    Message = "Route suggestion exceeded the maximum supported depth.",
                    StartNodeId = request.StartNodeId,
                    DestinationNodeId = request.DestinationNodeId,
                    NodeIds = [request.StartNodeId],
                    Segments = []
                };
            }

            segmentStack.Push(new RouteSuggestionSegment
            {
                FromNodeId = previousEntry.Edge.FromNodeId,
                ToNodeId = previousEntry.Edge.ToNodeId,
                RailroadIndex = previousEntry.Edge.RailroadIndex,
                OwnershipCategory = previousEntry.OwnershipCategory,
                Turns = previousEntry.TurnsAdded,
                CostPerTurn = previousEntry.CostPerTurn,
                TotalCost = previousEntry.TotalCostAdded
            });
            currentState = previousEntry.PreviousState;
            nodeStack.Push(currentState.NodeId);
        }

        return new RouteSuggestionResult
        {
            Status = RouteSuggestionStatus.Success,
            StartNodeId = request.StartNodeId,
            DestinationNodeId = request.DestinationNodeId,
            NodeIds = nodeStack.ToList(),
            Segments = segmentStack.ToList(),
            TotalCost = destinationCost.TotalCost,
            TotalTurns = destinationCost.TotalTurns,
            Outlook = new RouteSuggestionOutlook
            {
                ArrivalCost = destinationCost.TotalCost,
                ExitCost = exitAnalysis.WorstCaseExitCost,
                CombinedCost = destinationCost.TotalCost + exitAnalysis.WorstCaseExitCost,
                WorstCaseExitCost = exitAnalysis.WorstCaseExitCost,
                WorstCaseCombinedCost = destinationCost.TotalCost + exitAnalysis.WorstCaseExitCost,
                ExpectedExitCost = exitAnalysis.ExpectedExitCost,
                ExpectedCombinedCost = destinationCost.TotalCost + exitAnalysis.ExpectedExitCost,
                BonusOutProbability = exitAnalysis.BonusOutProbability
            }
        };
    }

    private static bool IsBetterDestination(RouteSuggestionDestinationChoice candidate, RouteSuggestionDestinationChoice current)
    {
        if (!candidate.CombinedWeightedCost.Equals(current.CombinedWeightedCost))
        {
            return candidate.CombinedWeightedCost < current.CombinedWeightedCost;
        }

        if (!candidate.CombinedCost.Equals(current.CombinedCost))
        {
            return candidate.CombinedCost < current.CombinedCost;
        }

        if (candidate.ExitAnalysis.WorstCaseExitCost != current.ExitAnalysis.WorstCaseExitCost)
        {
            return candidate.ExitAnalysis.WorstCaseExitCost < current.ExitAnalysis.WorstCaseExitCost;
        }

        if (!candidate.ExitAnalysis.BonusOutProbability.Equals(current.ExitAnalysis.BonusOutProbability))
        {
            return candidate.ExitAnalysis.BonusOutProbability > current.ExitAnalysis.BonusOutProbability;
        }

        if (IsBetterCost(candidate.ArrivalCost, current.ArrivalCost))
        {
            return true;
        }

        if (IsBetterCost(current.ArrivalCost, candidate.ArrivalCost))
        {
            return false;
        }

        var cashComparison = CompareVectors(candidate.TieBreakAnalysis.PaidOwnerCashValues, current.TieBreakAnalysis.PaidOwnerCashValues);
        if (cashComparison != 0)
        {
            return cashComparison < 0;
        }

        var accessibleComparison = CompareVectors(candidate.TieBreakAnalysis.PaidOwnerAccessibleDestinationPercentages, current.TieBreakAnalysis.PaidOwnerAccessibleDestinationPercentages);
        if (accessibleComparison != 0)
        {
            return accessibleComparison < 0;
        }

        var monopolyComparison = CompareVectors(candidate.TieBreakAnalysis.PaidOwnerMonopolyDestinationPercentages, current.TieBreakAnalysis.PaidOwnerMonopolyDestinationPercentages);
        if (monopolyComparison != 0)
        {
            return monopolyComparison < 0;
        }

        if (candidate.TieBreakAnalysis.DistinctPaidOwnerCount != current.TieBreakAnalysis.DistinctPaidOwnerCount)
        {
            return candidate.TieBreakAnalysis.DistinctPaidOwnerCount > current.TieBreakAnalysis.DistinctPaidOwnerCount;
        }

        if (candidate.TieBreakAnalysis.MaxPaymentsToSingleOwner != current.TieBreakAnalysis.MaxPaymentsToSingleOwner)
        {
            return candidate.TieBreakAnalysis.MaxPaymentsToSingleOwner < current.TieBreakAnalysis.MaxPaymentsToSingleOwner;
        }

        return false;
    }

    private static RouteSuggestionTieBreakAnalysis CalculateTieBreakAnalysis(
        RouteSuggestionRequest request,
        RouteSuggestionState startState,
        RouteSuggestionState destinationState,
        Dictionary<RouteSuggestionState, RouteSuggestionPrevious> previous)
    {
        if (request.ResolveRailroadOwnerPlayerIndex is null)
        {
            return RouteSuggestionTieBreakAnalysis.Empty;
        }

        var ownerCounts = new Dictionary<int, int>();
        var currentState = destinationState;

        while (currentState != startState)
        {
            if (!previous.TryGetValue(currentState, out var previousEntry))
            {
                break;
            }

            if (previousEntry.OwnershipCategory == RailroadOwnershipCategory.Unfriendly
                && previousEntry.TotalCostAdded > 0)
            {
                var ownerPlayerIndex = request.ResolveRailroadOwnerPlayerIndex(previousEntry.Edge.RailroadIndex);
                if (ownerPlayerIndex.HasValue)
                {
                    ownerCounts[ownerPlayerIndex.Value] = ownerCounts.GetValueOrDefault(ownerPlayerIndex.Value) + 1;
                }
            }

            currentState = previousEntry.PreviousState;
        }

        if (ownerCounts.Count == 0)
        {
            return RouteSuggestionTieBreakAnalysis.Empty;
        }

        var paidOwnerCashValues = ownerCounts.Keys
            .Select(ownerPlayerIndex => request.ResolvePlayerCash?.Invoke(ownerPlayerIndex) ?? int.MaxValue)
            .OrderBy(value => value)
            .ToArray();
        var paidOwnerAccessibleDestinationPercentages = ownerCounts.Keys
            .Select(ownerPlayerIndex => request.ResolvePlayerAccessibleDestinationPercent?.Invoke(ownerPlayerIndex) ?? double.MaxValue)
            .OrderBy(value => value)
            .ToArray();
        var paidOwnerMonopolyDestinationPercentages = ownerCounts.Keys
            .Select(ownerPlayerIndex => request.ResolvePlayerMonopolyDestinationPercent?.Invoke(ownerPlayerIndex) ?? double.MaxValue)
            .OrderBy(value => value)
            .ToArray();

        return new RouteSuggestionTieBreakAnalysis(
            paidOwnerCashValues,
            paidOwnerAccessibleDestinationPercentages,
            paidOwnerMonopolyDestinationPercentages,
            ownerCounts.Count,
            ownerCounts.Values.Max());
    }

    private static int CompareVectors<T>(IReadOnlyList<T> left, IReadOnlyList<T> right) where T : IComparable<T>
    {
        var count = Math.Min(left.Count, right.Count);
        for (var index = 0; index < count; index++)
        {
            var comparison = left[index].CompareTo(right[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    private static RouteSuggestionExitAnalysis CalculateDestinationExitAnalysis(
        MapRouteContext context,
        RouteSuggestionRequest request,
        RouteSuggestionState destinationState,
        RouteSuggestionCostKey arrivalCost,
        RouteSearchBudget searchBudget)
    {
        var worstCaseExitCost = CalculateDestinationExitCost(context, request, destinationState, searchBudget);
        if (worstCaseExitCost <= 0)
        {
            return new RouteSuggestionExitAnalysis(0d, 0, 0d);
        }

        var bonusOutProbability = CalculateBonusOutProbability(context, request, destinationState, arrivalCost, searchBudget);
        var expectedExitCost = worstCaseExitCost * (1d - bonusOutProbability);
        return new RouteSuggestionExitAnalysis(expectedExitCost, worstCaseExitCost, bonusOutProbability);
    }

    private static int CalculateDestinationExitCost(
        MapRouteContext context,
        RouteSuggestionRequest request,
        RouteSuggestionState destinationState,
        RouteSearchBudget searchBudget)
    {
        if (destinationState.LastRailroadIndex < 0
            || request.ResolveRailroadOwnership(destinationState.LastRailroadIndex) != RailroadOwnershipCategory.Unfriendly
            || !IsUnfriendlyDestination(context, request, destinationState.NodeId))
        {
            return 0;
        }

        var movementPointsPerTurn = request.MovementType == PlayerMovementType.ThreeDie ? 3 : 2;
        var startState = new RouteSuggestionState(destinationState.NodeId, destinationState.LastRailroadIndex, movementPointsPerTurn);
        var queue = new PriorityQueue<RouteSuggestionState, (int WeightedCost, int TotalCost, int TotalTurns, int TotalSegments)>();
        var bestCosts = new Dictionary<RouteSuggestionState, RouteSuggestionCostKey>
        {
            [startState] = new RouteSuggestionCostKey(0, 0, 0, 0)
        };
        var settledStates = new HashSet<RouteSuggestionState>();

        queue.Enqueue(startState, (0, 0, 0, 0));

        while (queue.Count > 0)
        {
            if (!searchBudget.TryStartState())
            {
                return int.MaxValue / 4;
            }

            var state = queue.Dequeue();
            if (!bestCosts.TryGetValue(state, out var stateCost) || !settledStates.Add(state))
            {
                continue;
            }

            if (state != startState
                && state.LastRailroadIndex >= 0
                && request.ResolveRailroadOwnership(state.LastRailroadIndex) != RailroadOwnershipCategory.Unfriendly)
            {
                return stateCost.TotalCost;
            }

            if (!context.Adjacency.TryGetValue(state.NodeId, out var outgoingEdges))
            {
                continue;
            }

            foreach (var edge in outgoingEdges)
            {
                if (!searchBudget.TryContinue())
                {
                    return int.MaxValue / 4;
                }

                var ownershipCategory = request.ResolveRailroadOwnership(edge.RailroadIndex);
                var costPerTurn = ResolveRailroadFee(request, edge.RailroadIndex, ownershipCategory);
                var exhaustedTurnCapacity = state.PointsUsedInCurrentTurn >= movementPointsPerTurn;
                var startsNewTurn = exhaustedTurnCapacity;
                var switchedRailroad = state.LastRailroadIndex != edge.RailroadIndex;
                var turnsAdded = startsNewTurn ? 1 : 0;
                var additionalCost = (startsNewTurn || switchedRailroad) ? costPerTurn : 0;
                var nextPointsUsed = startsNewTurn ? 1 : state.PointsUsedInCurrentTurn + 1;
                var nextState = new RouteSuggestionState(edge.ToNodeId, edge.RailroadIndex, nextPointsUsed);
                var candidateCost = new RouteSuggestionCostKey(
                    TotalCost: stateCost.TotalCost + additionalCost,
                    WeightedCost: stateCost.WeightedCost + additionalCost,
                    TotalTurns: stateCost.TotalTurns + turnsAdded,
                    TotalSegments: stateCost.TotalSegments + 1);

                if (bestCosts.TryGetValue(nextState, out var existingCost)
                    && !IsBetterCost(candidateCost, existingCost))
                {
                    continue;
                }

                bestCosts[nextState] = candidateCost;
                queue.Enqueue(nextState, (candidateCost.WeightedCost, candidateCost.TotalCost, candidateCost.TotalTurns, candidateCost.TotalSegments));
            }
        }

        return int.MaxValue / 4;
    }

    private static double CalculateBonusOutProbability(
        MapRouteContext context,
        RouteSuggestionRequest request,
        RouteSuggestionState destinationState,
        RouteSuggestionCostKey arrivalCost,
        RouteSearchBudget searchBudget)
    {
        if (arrivalCost.TotalTurns != 1)
        {
            return 0d;
        }

        if (request.CurrentFixedBonusMovement <= 0 && !request.BonusOutAvailable)
        {
            return 0d;
        }

        if (request.BonusOutRequiresWhiteDiceArrival && arrivalCost.TotalSegments > request.CurrentWhiteDiceMovement)
        {
            return 0d;
        }

        var minimumEscapeSegments = CalculateMinimumBonusOutSegments(context, request, destinationState, searchBudget);
        if (!minimumEscapeSegments.HasValue)
        {
            return 0d;
        }

        if (request.CurrentFixedBonusMovement > 0)
        {
            return request.CurrentFixedBonusMovement >= minimumEscapeSegments.Value ? 1d : 0d;
        }

        if (minimumEscapeSegments.Value <= 1)
        {
            return 1d;
        }

        if (minimumEscapeSegments.Value > 6)
        {
            return 0d;
        }

        return (7 - minimumEscapeSegments.Value) / 6d;
    }

    private static int? CalculateMinimumBonusOutSegments(
        MapRouteContext context,
        RouteSuggestionRequest request,
        RouteSuggestionState destinationState,
        RouteSearchBudget searchBudget)
    {
        if (destinationState.LastRailroadIndex < 0
            || request.ResolveRailroadOwnership(destinationState.LastRailroadIndex) != RailroadOwnershipCategory.Unfriendly)
        {
            return 0;
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            BuildNodeRailroadKey(destinationState.NodeId, destinationState.LastRailroadIndex)
        };
        var queue = new Queue<(string NodeId, int RailroadIndex, int SegmentsUsed)>();
        queue.Enqueue((destinationState.NodeId, destinationState.LastRailroadIndex, 0));

        while (queue.Count > 0)
        {
            if (!searchBudget.TryStartState())
            {
                return null;
            }

            var state = queue.Dequeue();
            if (!context.Adjacency.TryGetValue(state.NodeId, out var outgoingEdges))
            {
                continue;
            }

            foreach (var edge in outgoingEdges)
            {
                if (!searchBudget.TryContinue())
                {
                    return null;
                }

                var nextSegmentsUsed = state.SegmentsUsed + 1;
                var ownershipCategory = request.ResolveRailroadOwnership(edge.RailroadIndex);
                if (ownershipCategory != RailroadOwnershipCategory.Unfriendly)
                {
                    return nextSegmentsUsed;
                }

                if (edge.RailroadIndex != destinationState.LastRailroadIndex)
                {
                    continue;
                }

                var nextStateKey = BuildNodeRailroadKey(edge.ToNodeId, edge.RailroadIndex);
                if (visited.Add(nextStateKey))
                {
                    queue.Enqueue((edge.ToNodeId, edge.RailroadIndex, nextSegmentsUsed));
                }
            }
        }

        return null;
    }

    private static bool IsUnfriendlyDestination(MapRouteContext context, RouteSuggestionRequest request, string nodeId)
    {
        if (!context.Adjacency.TryGetValue(nodeId, out var outgoingEdges)
            || outgoingEdges.Count == 0)
        {
            return false;
        }

        foreach (var edge in outgoingEdges)
        {
            if (request.ResolveRailroadOwnership(edge.RailroadIndex) != RailroadOwnershipCategory.Unfriendly)
            {
                return false;
            }
        }

        return true;
    }

    private static int ResolveRailroadFee(
        RouteSuggestionRequest request,
        int railroadIndex,
        RailroadOwnershipCategory ownershipCategory)
    {
        if (request.ResolveRailroadFee is not null)
        {
            return request.ResolveRailroadFee(railroadIndex);
        }

        return ownershipCategory == RailroadOwnershipCategory.Unfriendly ? 5000 : 1000;
    }

    private static HashSet<string> BuildReachableNodesForRoutePlanning(
        MapRouteContext context,
        string destinationNodeId,
        Func<RouteGraphEdge, bool> canTraverse,
        RouteSearchBudget searchBudget)
    {
        var reachableNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(destinationNodeId))
        {
            return reachableNodes;
        }

        var queue = new Queue<string>();
        if (reachableNodes.Add(destinationNodeId))
        {
            queue.Enqueue(destinationNodeId);
        }

        while (queue.Count > 0)
        {
            if (!searchBudget.TryStartState())
            {
                return reachableNodes;
            }

            var nodeId = queue.Dequeue();
            if (!context.Adjacency.TryGetValue(nodeId, out var outgoingEdges))
            {
                continue;
            }

            foreach (var edge in outgoingEdges)
            {
                if (!searchBudget.TryContinue())
                {
                    return reachableNodes;
                }

                if (!canTraverse(edge) || !reachableNodes.Add(edge.ToNodeId))
                {
                    continue;
                }

                queue.Enqueue(edge.ToNodeId);
            }
        }

        return reachableNodes;
    }

    private static int CalculateHostileExitPenalty(
        RouteSuggestionRequest request,
        IReadOnlyList<RouteGraphEdge> outgoingEdges,
        RouteGraphEdge candidateEdge,
        HashSet<string> friendlyDestinationReachableNodes)
    {
        if (friendlyDestinationReachableNodes.Count == 0
            || request.ResolveRailroadOwnership(candidateEdge.RailroadIndex) != RailroadOwnershipCategory.Unfriendly)
        {
            return 0;
        }

        var hasFriendlyConnectedExit = false;
        foreach (var edge in outgoingEdges)
        {
            if (request.ResolveRailroadOwnership(edge.RailroadIndex) != RailroadOwnershipCategory.Unfriendly
                && friendlyDestinationReachableNodes.Contains(edge.ToNodeId))
            {
                hasFriendlyConnectedExit = true;
                break;
            }
        }

        return hasFriendlyConnectedExit ? RoutePlanningHostileExitPenalty : 0;
    }

    private static double CalculateTwoTurnHostileExposurePenalty(
        MapRouteContext context,
        RouteSuggestionRequest request,
        RouteSuggestionState startState,
        RouteSuggestionState destinationState,
        Dictionary<RouteSuggestionState, RouteSuggestionPrevious> previous,
        HashSet<string> friendlyDestinationReachableNodes,
        int firstTurnMovementCapacity,
        int futureTurnMovementCapacity,
        RouteSearchBudget searchBudget)
    {
        if (request.AverageFutureMovement <= 0d)
        {
            return 0d;
        }

        var orderedEntries = ReconstructPathEntries(startState, destinationState, previous);
        if (orderedEntries.Count == 0)
        {
            return 0d;
        }

        var currentTurnEndpoint = GetCurrentTurnEndpoint(orderedEntries, firstTurnMovementCapacity, futureTurnMovementCapacity);
        if (currentTurnEndpoint is null
            || request.ResolveRailroadOwnership(currentTurnEndpoint.Value.RailroadIndex) != RailroadOwnershipCategory.Unfriendly)
        {
            return 0d;
        }

        var averageNextTurnSegments = Math.Max(1, (int)Math.Round(request.AverageFutureMovement, MidpointRounding.AwayFromZero));
        var estimatedEscapeCost = EstimateNextTurnEscapeCost(
            context,
            request,
            currentTurnEndpoint.Value.NodeId,
            currentTurnEndpoint.Value.RailroadIndex,
            averageNextTurnSegments,
            friendlyDestinationReachableNodes,
            searchBudget);

        return estimatedEscapeCost;
    }

    private static List<RouteSuggestionPrevious> ReconstructPathEntries(
        RouteSuggestionState startState,
        RouteSuggestionState destinationState,
        Dictionary<RouteSuggestionState, RouteSuggestionPrevious> previous)
    {
        var entries = new List<RouteSuggestionPrevious>();
        var currentState = destinationState;

        while (currentState != startState)
        {
            if (!previous.TryGetValue(currentState, out var previousEntry))
            {
                return [];
            }

            entries.Add(previousEntry);
            currentState = previousEntry.PreviousState;
        }

        entries.Reverse();
        return entries;
    }

    private static (string NodeId, int RailroadIndex)? GetCurrentTurnEndpoint(
        List<RouteSuggestionPrevious> orderedEntries,
        int firstTurnMovementCapacity,
        int futureTurnMovementCapacity)
    {
        if (orderedEntries.Count == 0)
        {
            return null;
        }

        var lastNodeId = orderedEntries[0].PreviousState.NodeId;
        var lastRailroadIndex = orderedEntries[0].PreviousState.LastRailroadIndex;
        var pointsUsedInCurrentTurn = 0;
        var turnsStarted = 0;

        foreach (var entry in orderedEntries)
        {
            var currentTurnCapacity = turnsStarted <= 1 ? firstTurnMovementCapacity : futureTurnMovementCapacity;
            var isFirstEdge = lastRailroadIndex < 0;
            var exhaustedTurnCapacity = !isFirstEdge && pointsUsedInCurrentTurn >= currentTurnCapacity;
            var startsNewTurn = isFirstEdge || exhaustedTurnCapacity;

            if (turnsStarted >= 1 && startsNewTurn)
            {
                break;
            }

            if (startsNewTurn)
            {
                turnsStarted++;
                pointsUsedInCurrentTurn = 1;
            }
            else
            {
                pointsUsedInCurrentTurn++;
            }

            lastNodeId = entry.Edge.ToNodeId;
            lastRailroadIndex = entry.Edge.RailroadIndex;
        }

        return (lastNodeId, lastRailroadIndex);
    }

    private static double EstimateNextTurnEscapeCost(
        MapRouteContext context,
        RouteSuggestionRequest request,
        string startNodeId,
        int startingRailroadIndex,
        int averageNextTurnSegments,
        HashSet<string> friendlyDestinationReachableNodes,
        RouteSearchBudget searchBudget)
    {
        var startState = new RouteSearchLookaheadState(startNodeId, startingRailroadIndex, 0);
        var queue = new PriorityQueue<RouteSearchLookaheadState, (int TotalCost, int SegmentsUsed)>();
        var bestCosts = new Dictionary<RouteSearchLookaheadState, (int TotalCost, int SegmentsUsed)>
        {
            [startState] = (0, 0)
        };

        queue.Enqueue(startState, (0, 0));

        while (queue.Count > 0)
        {
            if (!searchBudget.TryStartState())
            {
                return ResolveRailroadFee(request, startingRailroadIndex, RailroadOwnershipCategory.Unfriendly) + RoutePlanningHostileExitPenalty;
            }

            var state = queue.Dequeue();
            if (!bestCosts.TryGetValue(state, out var stateCost))
            {
                continue;
            }

            if (!context.Adjacency.TryGetValue(state.NodeId, out var outgoingEdges))
            {
                continue;
            }

            foreach (var edge in outgoingEdges)
            {
                if (!searchBudget.TryContinue())
                {
                    return ResolveRailroadFee(request, startingRailroadIndex, RailroadOwnershipCategory.Unfriendly) + RoutePlanningHostileExitPenalty;
                }

                var nextSegmentsUsed = state.SegmentsUsed + 1;
                if (nextSegmentsUsed > averageNextTurnSegments)
                {
                    continue;
                }

                var ownershipCategory = request.ResolveRailroadOwnership(edge.RailroadIndex);
                var costPerTurn = ResolveRailroadFee(request, edge.RailroadIndex, ownershipCategory);
                var additionalCost = (state.SegmentsUsed == 0 || state.LastRailroadIndex != edge.RailroadIndex)
                    ? costPerTurn
                    : 0;
                var totalCost = stateCost.TotalCost + additionalCost;

                if (ownershipCategory != RailroadOwnershipCategory.Unfriendly
                    && friendlyDestinationReachableNodes.Contains(edge.ToNodeId))
                {
                    return totalCost;
                }

                var nextState = new RouteSearchLookaheadState(edge.ToNodeId, edge.RailroadIndex, nextSegmentsUsed);
                var nextCost = (TotalCost: totalCost, SegmentsUsed: nextSegmentsUsed);
                if (bestCosts.TryGetValue(nextState, out var existingCost)
                    && (existingCost.TotalCost < nextCost.TotalCost
                        || (existingCost.TotalCost == nextCost.TotalCost && existingCost.SegmentsUsed <= nextCost.SegmentsUsed)))
                {
                    continue;
                }

                bestCosts[nextState] = nextCost;
                queue.Enqueue(nextState, nextCost);
            }
        }

        return ResolveRailroadFee(request, startingRailroadIndex, RailroadOwnershipCategory.Unfriendly) + RoutePlanningHostileExitPenalty;
    }



    private static bool IsBetterCost(RouteSuggestionCostKey candidate, RouteSuggestionCostKey current)
    {
        if (candidate.TotalCost != current.TotalCost)
        {
            return candidate.TotalCost < current.TotalCost;
        }

        if (candidate.WeightedCost != current.WeightedCost)
        {
            return candidate.WeightedCost < current.WeightedCost;
        }

        if (candidate.TotalTurns != current.TotalTurns)
        {
            return candidate.TotalTurns < current.TotalTurns;
        }

        return candidate.TotalSegments < current.TotalSegments;
    }

#if DEBUG
    private static void WriteDebugCandidateTrace(
        RouteSuggestionRequest request,
        RouteSuggestionState startState,
        RouteSuggestionDestinationChoice? bestDestination,
        List<RouteSuggestionDestinationChoice> candidates,
        Dictionary<RouteSuggestionState, RouteSuggestionPrevious> previous,
        RouteSuggestionDebugStats debugStats)
    {
        var rankedCandidates = candidates
            .Distinct()
            .ToList();
        rankedCandidates.Sort(static (left, right) => CompareDestinationChoices(left, right));

        Debug.WriteLine($"[MapRouteService] Route suggestion debug {request.StartNodeId} -> {request.DestinationNodeId}");
        Debug.WriteLine($"[MapRouteService] Candidates evaluated: {rankedCandidates.Count}");
        Debug.WriteLine(
            $"[MapRouteService] Search stats: dequeued={debugStats.DequeuedStates}, settled={debugStats.SettledStates}, edges={debugStats.EdgesConsidered}, missingCost={debugStats.MissingStateCostSkips}, alreadySettled={debugStats.AlreadySettledSkips}, noOutgoing={debugStats.StatesWithoutOutgoingEdges}");
        Debug.WriteLine(
            $"[MapRouteService] Pruning stats: traveled={debugStats.TraveledSegmentPrunes}, reused={debugStats.ReusedSegmentPrunes}, segmentCap={debugStats.SegmentCapPrunes}, dominated={debugStats.DominatedCostPrunes}, destSegmentCap={debugStats.DestinationRejectedBySegmentCap}, friendlyExit={debugStats.DestinationRejectedByFriendlyExit}, continuation={debugStats.DestinationRejectedByContinuation}, stoppedByBestCost={debugStats.StoppedByBestCostBound}");
        Debug.WriteLine(
            $"[MapRouteService] Budget stats: explored={debugStats.ExploredStates}, timeout={debugStats.StoppedByTimeout}, explorationBudget={debugStats.StoppedByExplorationBudget}");

        if (bestDestination.HasValue)
        {
            Debug.WriteLine($"[MapRouteService] Best candidate: {BuildDebugCandidateSummary(startState, bestDestination.Value, previous)}");
        }

        foreach (var candidate in rankedCandidates.Take(5).Select((choice, index) => (choice, index)))
        {
            Debug.WriteLine($"[MapRouteService] Top {candidate.index + 1}: {BuildDebugCandidateSummary(startState, candidate.choice, previous)}");
        }
    }

    private static int CompareDestinationChoices(RouteSuggestionDestinationChoice left, RouteSuggestionDestinationChoice right)
    {
        if (left.Equals(right))
        {
            return 0;
        }

        return IsBetterDestination(left, right) ? -1 : 1;
    }

    private static string BuildDebugCandidateSummary(
        RouteSuggestionState startState,
        RouteSuggestionDestinationChoice candidate,
        Dictionary<RouteSuggestionState, RouteSuggestionPrevious> previous)
    {
        var pathEntries = ReconstructPathEntries(startState, candidate.DestinationState, previous);
        var nodes = new List<string> { startState.NodeId };
        nodes.AddRange(pathEntries.Select(entry => entry.Edge.ToNodeId));
        var railroads = pathEntries.Select(entry => entry.Edge.RailroadIndex.ToString(CultureInfo.InvariantCulture)).ToArray();

        return string.Join(" | ",
            $"nodes={string.Join("->", nodes)}",
            $"rr={string.Join(",", railroads)}",
            $"weighted={candidate.CombinedWeightedCost:F2}",
            $"combined={candidate.CombinedCost:F2}",
            $"arrival={candidate.ArrivalCost.TotalCost}",
            $"arrivalWeighted={candidate.ArrivalCost.WeightedCost}",
            $"exit={candidate.ExitAnalysis.WorstCaseExitCost}",
            $"lookahead={candidate.LookaheadPenalty:F2}",
            $"turns={candidate.ArrivalCost.TotalTurns}",
            $"segments={candidate.ArrivalCost.TotalSegments}");
    }

    private static void ApplyBudgetExceededDebugStats(RouteSuggestionDebugStats debugStats, RouteSearchBudget searchBudget)
    {
        debugStats.ExploredStates = searchBudget.ExploredStates;
        debugStats.StoppedByTimeout = searchBudget.StopReason == RouteSearchBudgetStopReason.Timeout;
        debugStats.StoppedByExplorationBudget = searchBudget.StopReason == RouteSearchBudgetStopReason.ExplorationBudget;
    }

    private sealed class RouteSuggestionDebugStats
    {
        public int ExploredStates { get; set; }

        public int DequeuedStates { get; set; }

        public int SettledStates { get; set; }

        public int EdgesConsidered { get; set; }

        public int MissingStateCostSkips { get; set; }

        public int AlreadySettledSkips { get; set; }

        public int StatesWithoutOutgoingEdges { get; set; }

        public int TraveledSegmentPrunes { get; set; }

        public int ReusedSegmentPrunes { get; set; }

        public int SegmentCapPrunes { get; set; }

        public int DominatedCostPrunes { get; set; }

        public int DestinationRejectedBySegmentCap { get; set; }

        public int DestinationRejectedByFriendlyExit { get; set; }

        public int DestinationRejectedByContinuation { get; set; }

        public bool StoppedByBestCostBound { get; set; }

        public bool StoppedByTimeout { get; set; }

        public bool StoppedByExplorationBudget { get; set; }
    }
#endif

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

    private static string BuildSegmentKey(string fromNodeId, string toNodeId, int railroadIndex)
    {
        return string.Compare(fromNodeId, toNodeId, StringComparison.OrdinalIgnoreCase) <= 0
            ? string.Concat(fromNodeId, "-", toNodeId, ":", railroadIndex.ToString(System.Globalization.CultureInfo.InvariantCulture))
            : string.Concat(toNodeId, "-", fromNodeId, ":", railroadIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
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

    private static string BuildNodeRailroadKey(string nodeId, int railroadIndex)
    {
        return string.Concat(nodeId, "|", railroadIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}

public readonly record struct RouteSuggestionState(string NodeId, int LastRailroadIndex, int PointsUsedInCurrentTurn);

public readonly record struct RouteSuggestionCostKey(int TotalCost, int WeightedCost, int TotalTurns, int TotalSegments);

public readonly record struct RouteSuggestionDestinationChoice(
    RouteSuggestionState DestinationState,
    RouteSuggestionCostKey ArrivalCost,
    RouteSuggestionExitAnalysis ExitAnalysis,
    RouteSuggestionTieBreakAnalysis TieBreakAnalysis,
    double LookaheadPenalty)
{
    public double CombinedCost => ArrivalCost.TotalCost + ExitAnalysis.ExpectedExitCost;
    public double CombinedWeightedCost => ArrivalCost.WeightedCost + ExitAnalysis.ExpectedExitCost + LookaheadPenalty;
}

public readonly record struct RouteSuggestionExitAnalysis(
    double ExpectedExitCost,
    int WorstCaseExitCost,
    double BonusOutProbability);

public readonly record struct RouteSuggestionTieBreakAnalysis(
    IReadOnlyList<int> PaidOwnerCashValues,
    IReadOnlyList<double> PaidOwnerAccessibleDestinationPercentages,
    IReadOnlyList<double> PaidOwnerMonopolyDestinationPercentages,
    int DistinctPaidOwnerCount,
    int MaxPaymentsToSingleOwner)
{
    public static RouteSuggestionTieBreakAnalysis Empty { get; } = new([], [], [], 0, 0);
}

public readonly record struct RouteSuggestionPrevious(
    RouteSuggestionState PreviousState,
    RouteGraphEdge Edge,
    RailroadOwnershipCategory OwnershipCategory,
    int TurnsAdded,
    int CostPerTurn,
    int TotalCostAdded);

public readonly record struct RouteSuggestionCurrentTurnProgress(
    string EndpointNodeId,
    IReadOnlyList<string> ConsumedSegmentKeys,
    bool ReachedDestinationThisTurn);

public readonly record struct RouteSearchLookaheadState(string NodeId, int LastRailroadIndex, int SegmentsUsed);

public enum RouteSearchBudgetStopReason
{
    ExplorationBudget,
    Timeout
}

public sealed class RouteSearchBudget
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly int _maximumExploredStates;
    private readonly TimeSpan _maximumSearchDuration;

    public RouteSearchBudget(int maximumExploredStates, TimeSpan maximumSearchDuration)
    {
        _maximumExploredStates = maximumExploredStates;
        _maximumSearchDuration = maximumSearchDuration;
    }

    public int ExploredStates { get; private set; }

    public RouteSearchBudgetStopReason? StopReason { get; private set; }

    public bool IsExceeded => StopReason.HasValue;

    public bool TryStartState()
    {
        if (!TryContinue())
        {
            return false;
        }

        ExploredStates++;
        if (ExploredStates > _maximumExploredStates)
        {
            StopReason = RouteSearchBudgetStopReason.ExplorationBudget;
            return false;
        }

        return true;
    }

    public bool TryContinue()
    {
        if (StopReason.HasValue)
        {
            return false;
        }

        if (_stopwatch.Elapsed >= _maximumSearchDuration)
        {
            StopReason = RouteSearchBudgetStopReason.Timeout;
            return false;
        }

        return true;
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
    public required string SegmentKey { get; init; }
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
