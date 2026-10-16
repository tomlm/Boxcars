using Boxcars.Data;
using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Domain;
using Boxcars.Engine.Persistence;
using RailBaronGameEngine = Boxcars.Engine.Domain.GameEngine;

namespace Boxcars.Services;

/// <summary>
/// How much cash a player can safely spend: fees they owe this turn, fees opponents are about to pay them, and the
/// operating reserve they should keep to reach their next destination given where they are and what their network reaches.
/// Shared by purchase recommendations and bots.
/// </summary>
public static class OperatingCapitalService
{
    // Typical dots moved per turn, used to see which railroads an opponent rides on their next turn.
    private const int FreightDotsPerTurn = 7;
    private const int ExpressDotsPerTurn = 8;
    private const int SuperchiefDotsPerTurn = 10;

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
    /// Fees opponents are about to pay the player: each opponent whose next turn of travel on their current route
    /// rides one of <paramref name="ownedRailroadIndices"/> pays one fee.
    /// </summary>
    public static int EstimateIncomingFees(GameState state, int playerIndex, IReadOnlyCollection<int> ownedRailroadIndices, GameSettings settings)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ownedRailroadIndices);
        ArgumentNullException.ThrowIfNull(settings);

        if (ownedRailroadIndices.Count == 0)
        {
            return 0;
        }

        var fee = RailBaronGameEngine.GetUnfriendlyFee(settings, state.AllRailroadsSold);
        var payingOpponents = state.Players
            .Select((opponent, index) => (Opponent: opponent, Index: index))
            .Where(entry => entry.Index != playerIndex && !entry.Opponent.IsBankrupt && entry.Opponent.ActiveRoute is not null)
            .Count(entry =>
            {
                var route = entry.Opponent.ActiveRoute!;
                return route.Segments
                    .Skip(Math.Max(0, entry.Opponent.RouteProgressIndex))
                    .Take(GetDotsPerTurn(entry.Opponent.LocomotiveType))
                    .Any(segment => ownedRailroadIndices.Contains(segment.RailroadIndex));
            });

        return payingOpponents * fee;
    }

    /// <summary>
    /// Cash to keep on hand: an engine-based floor, the fees owed, and a buffer that grows with how likely the next
    /// destination is in a far-away region the player's network doesn't reach.
    /// </summary>
    public static OperatingReserveProfile BuildOperatingReserve(
        MapDefinition mapDefinition,
        string currentRegionCode,
        LocomotiveType locomotiveType,
        NetworkCoverageSnapshot coverage,
        int feesOwed)
    {
        ArgumentNullException.ThrowIfNull(mapDefinition);
        ArgumentNullException.ThrowIfNull(coverage);

        var regionProbabilityLookup = BuildRegionProbabilityLookup(mapDefinition);
        var ownedRegionAccessByCode = coverage.RegionAccess
            .ToDictionary(region => region.RegionCode, region => region.AccessibleDestinationPercent, StringComparer.OrdinalIgnoreCase);
        var regionHopCounts = BuildRegionHopCounts(mapDefinition, currentRegionCode);
        var engineRiskMultiplier = GetEngineRiskMultiplier(locomotiveType);

        var regionPressures = mapDefinition.Regions
            .Where(region => !string.Equals(region.Code, currentRegionCode, StringComparison.OrdinalIgnoreCase))
            .Select(region =>
            {
                var probabilityPercent = regionProbabilityLookup.TryGetValue(region.Code, out var probabilityValue)
                    ? probabilityValue
                    : 0m;
                var ownedAccessPercent = ownedRegionAccessByCode.TryGetValue(region.Code, out var accessValue)
                    ? accessValue
                    : 0m;
                var accessGapPercent = Math.Max(0m, 100m - ownedAccessPercent);
                var regionHopCount = regionHopCounts.TryGetValue(region.Code, out var hopCount)
                    ? hopCount
                    : Math.Max(1, mapDefinition.Regions.Count);
                var hopMultiplier = 1m + Math.Max(0, regionHopCount - 1) * 0.35m;
                var weightedReservePressure = Math.Round(
                    probabilityPercent * accessGapPercent / 100m * hopMultiplier,
                    2,
                    MidpointRounding.AwayFromZero);

                return new OperatingReservePressure(
                    region.Code,
                    region.Name,
                    probabilityPercent,
                    ownedAccessPercent,
                    accessGapPercent,
                    regionHopCount,
                    hopMultiplier,
                    weightedReservePressure);
            })
            .OrderByDescending(pressure => pressure.WeightedReservePressure)
            .ThenByDescending(pressure => pressure.ProbabilityPercent)
            .ThenBy(pressure => pressure.RegionCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var weightedReservePressure = regionPressures.Sum(pressure => pressure.WeightedReservePressure);
        var engineReserveFloor = GetReserveFloor(locomotiveType);
        var variableReserve = weightedReservePressure * 55m * engineRiskMultiplier;
        var recommendedOperatingReserveCash = RoundUpToNearest(
            decimal.ToInt32(Math.Ceiling(engineReserveFloor + feesOwed + variableReserve)),
            500);

        return new OperatingReserveProfile(
            currentRegionCode,
            recommendedOperatingReserveCash,
            engineRiskMultiplier,
            weightedReservePressure,
            regionPressures);
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

    private static decimal GetEngineRiskMultiplier(LocomotiveType locomotiveType)
    {
        return locomotiveType switch
        {
            LocomotiveType.Freight => 1.3m,
            LocomotiveType.Express => 1.1m,
            LocomotiveType.Superchief => 0.9m,
            _ => 1m
        };
    }

    private static int GetReserveFloor(LocomotiveType locomotiveType)
    {
        return locomotiveType switch
        {
            LocomotiveType.Freight => 10_000,
            LocomotiveType.Express => 8_000,
            LocomotiveType.Superchief => 6_000,
            _ => 8_000
        };
    }

    private static Dictionary<string, int> BuildRegionHopCounts(MapDefinition mapDefinition, string originRegionCode)
    {
        var regionCodeByIndex = mapDefinition.Regions
            .ToDictionary(region => region.Index, region => region.Code);
        var adjacencyByRegion = mapDefinition.Regions
            .ToDictionary(
                region => region.Code,
                _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

        foreach (var segment in mapDefinition.RailroadRouteSegments)
        {
            if (!regionCodeByIndex.TryGetValue(segment.StartRegionIndex, out var startRegionCode)
                || !regionCodeByIndex.TryGetValue(segment.EndRegionIndex, out var endRegionCode)
                || string.Equals(startRegionCode, endRegionCode, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            adjacencyByRegion[startRegionCode].Add(endRegionCode);
            adjacencyByRegion[endRegionCode].Add(startRegionCode);
        }

        var hopCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [originRegionCode] = 0
        };
        var queue = new Queue<string>();
        queue.Enqueue(originRegionCode);

        while (queue.Count > 0)
        {
            var regionCode = queue.Dequeue();
            var currentHopCount = hopCounts[regionCode];

            if (!adjacencyByRegion.TryGetValue(regionCode, out var neighbors))
            {
                continue;
            }

            foreach (var neighbor in neighbors)
            {
                if (hopCounts.ContainsKey(neighbor))
                {
                    continue;
                }

                hopCounts[neighbor] = currentHopCount + 1;
                queue.Enqueue(neighbor);
            }
        }

        return hopCounts;
    }

    private static int RoundUpToNearest(int amount, int increment)
    {
        if (amount <= 0)
        {
            return 0;
        }

        return ((amount + increment - 1) / increment) * increment;
    }
}

public sealed record OperatingReserveProfile(
    string CurrentRegionCode,
    int RecommendedOperatingReserveCash,
    decimal EngineRiskMultiplier,
    decimal WeightedReservePressure,
    IReadOnlyList<OperatingReservePressure> RegionPressures);

public sealed record OperatingReservePressure(
    string RegionCode,
    string RegionName,
    decimal ProbabilityPercent,
    decimal OwnedAccessPercent,
    decimal AccessGapPercent,
    int RegionHopCount,
    decimal HopMultiplier,
    decimal WeightedReservePressure);
