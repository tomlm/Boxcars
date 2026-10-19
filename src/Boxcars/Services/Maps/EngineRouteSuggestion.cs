using Boxcars.Data.Maps;
using Boxcars.Engine.Domain;
using RailBaronGameEngine = global::Boxcars.Engine.Domain.GameEngine;

namespace Boxcars.Services.Maps;

/// <summary>
/// The engine's suggested route for a player, in the shape the map draws: who owns each segment, where each turn
/// starts and what it costs.
/// </summary>
public static class EngineRouteSuggestion
{
    public static RouteSuggestionResult Suggest(RailBaronGameEngine engine, int playerIndex, RouteSuggestionOptions options)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(options);

        var player = engine.Players[playerIndex];
        Boxcars.Engine.Domain.Route route;
        try
        {
            route = engine.SuggestRouteForPlayer(playerIndex, options);
        }
        catch (InvalidOperationException exception)
        {
            return new RouteSuggestionResult
            {
                Status = RouteSuggestionStatus.Error,
                Message = exception.Message,
                DestinationNodeId = options.DestinationNodeId ?? string.Empty
            };
        }

        var startNodeId = route.NodeIds.Count > 0 ? route.NodeIds[0] : string.Empty;
        var destinationNodeId = route.NodeIds.Count > 0 ? route.NodeIds[^1] : startNodeId;
        if (route.Segments.Count == 0)
        {
            return new RouteSuggestionResult
            {
                Status = RouteSuggestionStatus.NoRoute,
                Message = "No route found.",
                StartNodeId = startNodeId,
                DestinationNodeId = options.DestinationNodeId ?? destinationNodeId,
                NodeIds = [startNodeId]
            };
        }

        var costs = engine.GetRouteSegmentCosts(playerIndex, route);
        var segments = route.Segments
            .Select((segment, index) => new RouteSuggestionSegment
            {
                FromNodeId = segment.FromNodeId,
                ToNodeId = segment.ToNodeId,
                RailroadIndex = segment.RailroadIndex,
                OwnershipCategory = ResolveOwnership(engine, player, segment.RailroadIndex),
                Turns = costs[index].StartsTurn ? 1 : 0,
                CostPerTurn = costs[index].FeePerTurn,
                TotalCost = costs[index].FeeAdded
            })
            .ToList();

        return new RouteSuggestionResult
        {
            Status = RouteSuggestionStatus.Success,
            StartNodeId = startNodeId,
            DestinationNodeId = destinationNodeId,
            NodeIds = [.. route.NodeIds],
            Segments = segments,
            TotalTurns = segments.Sum(segment => segment.Turns),
            TotalCost = route.TotalCost,
            Approach = player == engine.CurrentTurn.ActivePlayer ? engine.LastApproachDecision : null,
            ArrivalBonus = engine.GetArrivalBonus(playerIndex, route)
        };
    }

    private static RailroadOwnershipCategory ResolveOwnership(RailBaronGameEngine engine, Player player, int railroadIndex)
    {
        var owner = engine.Railroads.FirstOrDefault(railroad => railroad.Index == railroadIndex)?.Owner;
        return owner is null ? RailroadOwnershipCategory.Public
            : owner == player ? RailroadOwnershipCategory.Friendly
            : RailroadOwnershipCategory.Unfriendly;
    }
}
