using Boxcars.Data;
using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Domain;
using Boxcars.Engine.Persistence;
using RailBaronGameEngine = Boxcars.Engine.Domain.GameEngine;

namespace Boxcars.Services;

/// <summary>
/// How much cash a player can safely spend: fees they owe this turn, fees opponents are expected to pay them soon, and the
/// operating reserve they should keep given where they are.
/// Shared by purchase recommendations and bots.
/// </summary>
public static class OperatingCapitalService
{
    // Typical dots moved per turn, used to see which railroads an opponent rides on their next turn.
    private const int FreightDotsPerTurn = 7;
    private const int ExpressDotsPerTurn = 8;
    private const int SuperchiefDotsPerTurn = 10;

    /// <summary>How much a fee on an opponent's planned route next turn counts toward income; they can still change route.</summary>
    public const decimal NextTurnRouteWeight = 0.75m;

    /// <summary>How much a fee on an opponent's planned route the turn after next counts; it arrives after the player's next move.</summary>
    public const decimal SecondTurnRouteWeight = 0.5m;

    /// <summary>Fees the player owes for the railroads ridden this turn (paid when the turn ends).</summary>
    public static int CalculateFeesOwed(GameState state, int playerIndex, GameSettings settings)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(settings);

        if (playerIndex < 0 || playerIndex >= state.Players.Count)
        {
            return 0;
        }

        var fullRateRailroads = state.Turn.RailroadsRequiringFullOwnerRateThisTurn.ToHashSet();
        var ownerBuckets = new Dictionary<int, bool>();
        var usesPublicFee = false;
        var usesPrivateFee = false;

        foreach (var railroadIndex in state.Turn.RailroadsRiddenThisTurn)
        {
            if (!state.RailroadOwnership.TryGetValue(railroadIndex, out var ownerIndex) || ownerIndex is null)
            {
                usesPublicFee = true;
                continue;
            }

            if (ownerIndex.Value == playerIndex)
            {
                usesPrivateFee = true;
                continue;
            }

            var requiresFullOwnerRate = fullRateRailroads.Contains(railroadIndex);
            ownerBuckets[ownerIndex.Value] = ownerBuckets.GetValueOrDefault(ownerIndex.Value) || requiresFullOwnerRate;
        }

        var opponentRate = RailBaronGameEngine.GetUnfriendlyFee(settings, state.AllRailroadsSold);
        return (usesPublicFee ? RailBaronGameEngine.GetPublicFee(settings) : 0)
            + (usesPrivateFee ? RailBaronGameEngine.GetPrivateFee(settings) : 0)
            + ownerBuckets.Values.Sum(requiresFullOwnerRate => requiresFullOwnerRate ? opponentRate : RailBaronGameEngine.GetPrivateFee(settings));
    }

    /// <summary>
    /// Fees opponents are expected to pay the player over the next two turns for riding
    /// <paramref name="ownedRailroadIndices"/>, weighted by how sure each payment is. Each opponent pays at most one fee per turn:
    /// <list type="bullet">
    /// <item>Certain (full value): the opponent stands where every railroad belongs to the player, so their next move must ride one.</item>
    /// <item>Planned next turn (<see cref="NextTurnRouteWeight"/>): their route rides one within their next turn of dots.</item>
    /// <item>Planned the turn after (<see cref="SecondTurnRouteWeight"/>): it rides one within the turn after.</item>
    /// </list>
    /// A railroad the opponent is grandfathered on is counted at their grandfathered rate.
    /// </summary>
    public static int EstimateIncomingFees(MapDefinition? mapDefinition, GameState state, int playerIndex, IReadOnlyCollection<int> ownedRailroadIndices, GameSettings settings)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ownedRailroadIndices);
        ArgumentNullException.ThrowIfNull(settings);

        if (ownedRailroadIndices.Count == 0)
        {
            return 0;
        }

        var unfriendlyFee = RailBaronGameEngine.GetUnfriendlyFee(settings, state.AllRailroadsSold);
        var railroadsByNode = mapDefinition is null ? null : BuildRailroadsByNode(mapDefinition);
        var total = 0m;

        for (var index = 0; index < state.Players.Count; index++)
        {
            var opponent = state.Players[index];
            if (index == playerIndex || opponent.IsBankrupt)
            {
                continue;
            }

            int FeeFor(int railroadIndex) => opponent.GrandfatheredRailroadFees
                .FirstOrDefault(entry => entry.RailroadIndex == railroadIndex)?.FeeAmount ?? unfriendlyFee;

            var dotsPerTurn = GetDotsPerTurn(opponent.LocomotiveType);
            var upcomingSegments = opponent.ActiveRoute?.Segments.Skip(Math.Max(0, opponent.RouteProgressIndex)).ToList() ?? [];
            int? FirstOwnedRailroad(IEnumerable<RouteSegmentState> segments) => segments
                .Select(segment => (int?)segment.RailroadIndex)
                .FirstOrDefault(railroadIndex => ownedRailroadIndices.Contains(railroadIndex!.Value));

            // Next turn: certain when every railroad where they stand is the player's, else their planned route.
            if (railroadsByNode is not null
                && opponent.CurrentNodeId is { } nodeId
                && railroadsByNode.TryGetValue(nodeId, out var railroadsHere)
                && railroadsHere.All(ownedRailroadIndices.Contains))
            {
                total += railroadsHere.Min(FeeFor);
            }
            else if (FirstOwnedRailroad(upcomingSegments.Take(dotsPerTurn)) is int nextTurnRailroad)
            {
                total += NextTurnRouteWeight * FeeFor(nextTurnRailroad);
            }

            if (FirstOwnedRailroad(upcomingSegments.Skip(dotsPerTurn).Take(dotsPerTurn)) is int secondTurnRailroad)
            {
                total += SecondTurnRouteWeight * FeeFor(secondTurnRailroad);
            }
        }

        return (int)Math.Round(total, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The operating reserve still needed once expected incoming fees are counted: income that arrives before the player
    /// moves again stands in for cash kept on hand, down to nothing. Fees owed this turn are paid before any of it arrives,
    /// so they are not covered.
    /// </summary>
    public static int ReserveAfterIncomingFees(int operatingReserve, int incomingFees)
    {
        return Math.Max(0, operatingReserve - Math.Max(0, incomingFees));
    }

    /// <summary>
    /// Cash to keep on hand after buying: one opponent's fee if the player can leave their current city without
    /// paying an opponent (on their own or an unowned railroad), two if every railroad there belongs to an opponent.
    /// With default settings that's $5,000 / $10,000 while railroads are left and $10,000 / $20,000 once all are sold.
    /// Fees already owed this turn are not included.
    /// </summary>
    public static OperatingReserve CalculateOperatingReserve(MapDefinition mapDefinition, GameState state, int playerIndex, GameSettings settings)
    {
        ArgumentNullException.ThrowIfNull(mapDefinition);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(settings);

        var opponentFee = RailBaronGameEngine.GetUnfriendlyFee(settings, state.AllRailroadsSold);
        var atFriendlyCity = playerIndex < 0
            || playerIndex >= state.Players.Count
            || IsFriendlyCity(mapDefinition, state, playerIndex, state.Players[playerIndex].CurrentCityName);

        return new OperatingReserve(atFriendlyCity ? opponentFee : 2 * opponentFee, atFriendlyCity, opponentFee);
    }

    /// <summary>
    /// Whether a railroad serving the city lets the player leave without paying an opponent. Unknown cities count
    /// as friendly so the reserve never jumps on missing map data.
    /// </summary>
    private static bool IsFriendlyCity(MapDefinition mapDefinition, GameState state, int playerIndex, string cityName)
    {
        var city = mapDefinition.Cities.FirstOrDefault(candidate => string.Equals(candidate.Name, cityName, StringComparison.OrdinalIgnoreCase));
        var region = city is null
            ? null
            : mapDefinition.Regions.FirstOrDefault(candidate => string.Equals(candidate.Code, city.RegionCode, StringComparison.OrdinalIgnoreCase));
        if (city?.MapDotIndex is not int dotIndex || region is null)
        {
            return true;
        }

        var servingRailroads = mapDefinition.RailroadRouteSegments
            .Where(segment => (segment.StartRegionIndex == region.Index && segment.StartDotIndex == dotIndex)
                || (segment.EndRegionIndex == region.Index && segment.EndDotIndex == dotIndex))
            .Select(segment => segment.RailroadIndex)
            .Distinct()
            .ToList();

        return servingRailroads.Count == 0
            || servingRailroads.Any(railroadIndex =>
                !state.RailroadOwnership.TryGetValue(railroadIndex, out var ownerIndex)
                || ownerIndex is null
                || ownerIndex.Value == playerIndex);
    }

    public static Dictionary<string, decimal> BuildRegionProbabilityLookup(MapDefinition mapDefinition)
    {
        ArgumentNullException.ThrowIfNull(mapDefinition);

        var regionIndexByCode = mapDefinition.Regions
            .ToDictionary(region => region.Code, region => region.Index, StringComparer.OrdinalIgnoreCase);
        var weightedRegions = mapDefinition.Regions
            .Where(region => region.Probability.HasValue && region.Probability.Value > 0)
            .ToList();

        if (weightedRegions.Count > 0)
        {
            return weightedRegions.ToDictionary(
                region => region.Code,
                region => Math.Round((decimal)region.Probability!.Value, 3, MidpointRounding.AwayFromZero),
                StringComparer.OrdinalIgnoreCase);
        }

        var cityCountByRegion = mapDefinition.Cities
            .Where(city => city.MapDotIndex.HasValue && regionIndexByCode.ContainsKey(city.RegionCode))
            .GroupBy(city => city.RegionCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var totalCityCount = cityCountByRegion.Values.Sum();

        return cityCountByRegion.ToDictionary(
            entry => entry.Key,
            entry => totalCityCount == 0
                ? 0m
                : Math.Round(entry.Value * 100m / totalCityCount, 3, MidpointRounding.AwayFromZero),
            StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, HashSet<int>> BuildRailroadsByNode(MapDefinition mapDefinition)
    {
        var railroadsByNode = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var segment in mapDefinition.RailroadRouteSegments)
        {
            foreach (var nodeId in new[] { $"{segment.StartRegionIndex}:{segment.StartDotIndex}", $"{segment.EndRegionIndex}:{segment.EndDotIndex}" })
            {
                if (!railroadsByNode.TryGetValue(nodeId, out var railroads))
                {
                    railroadsByNode[nodeId] = railroads = [];
                }

                railroads.Add(segment.RailroadIndex);
            }
        }

        return railroadsByNode;
    }

    private static int GetDotsPerTurn(string locomotiveType)
    {
        return Enum.TryParse<LocomotiveType>(locomotiveType, ignoreCase: true, out var parsed)
            ? parsed switch
            {
                LocomotiveType.Superchief => SuperchiefDotsPerTurn,
                LocomotiveType.Express => ExpressDotsPerTurn,
                _ => FreightDotsPerTurn
            }
            : FreightDotsPerTurn;
    }
}

/// <param name="Amount">Cash to keep after buying.</param>
/// <param name="AtFriendlyCity">The player can leave their current city without paying an opponent.</param>
/// <param name="OpponentFee">The fee for riding an opponent's railroad right now.</param>
public sealed record OperatingReserve(int Amount, bool AtFriendlyCity, int OpponentFee);
