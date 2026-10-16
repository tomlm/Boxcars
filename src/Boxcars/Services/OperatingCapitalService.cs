using Boxcars.Data;
using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Domain;
using Boxcars.Engine.Persistence;
using RailBaronGameEngine = Boxcars.Engine.Domain.GameEngine;

namespace Boxcars.Services;

/// <summary>
/// How much cash a player can safely spend: fees they owe this turn, fees opponents are about to pay them, and the
/// operating reserve they should keep given where they are.
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
