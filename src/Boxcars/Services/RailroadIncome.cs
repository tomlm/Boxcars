using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Domain;
using Boxcars.Engine.Persistence;
using RailBaronGameEngine = Boxcars.Engine.Domain.GameEngine;

namespace Boxcars.Services;

/// <summary>The fees each railroad has collected from opponents so far: the income it produced for its owners.</summary>
public sealed record RailroadIncome(IReadOnlyList<RailroadIncomeRow> Rows)
{
    public static RailroadIncome Empty { get; } = new([]);

    /// <summary>
    /// Finds fee payments in saved game states, in the order they happened: each time a player's fees collected go up,
    /// the active player paid them for the owner's railroads ridden that turn. An owner is paid once per turn at the
    /// highest rate ridden, so the payment goes to the railroads ridden at that rate, split evenly when there are
    /// several. Every railroad on the map gets a row, under its owner in the last state (or none).
    /// </summary>
    public static RailroadIncome Build(IEnumerable<GameState> statesInOrder, MapDefinition mapDefinition, GameSettings settings)
    {
        ArgumentNullException.ThrowIfNull(statesInOrder);
        ArgumentNullException.ThrowIfNull(mapDefinition);
        ArgumentNullException.ThrowIfNull(settings);

        var income = new Dictionary<int, int>();
        GameState? previous = null;

        foreach (var state in statesInOrder)
        {
            if (previous is not null && previous.Players.Count == state.Players.Count)
            {
                for (var ownerIndex = 0; ownerIndex < state.Players.Count; ownerIndex++)
                {
                    var collected = state.Players[ownerIndex].TotalFeesCollected - previous.Players[ownerIndex].TotalFeesCollected;
                    if (collected > 0)
                    {
                        AddPayment(income, state, ownerIndex, collected, settings);
                    }
                }
            }

            previous = state;
        }

        var ownership = previous?.RailroadOwnership ?? [];
        var rows = mapDefinition.Railroads
            .Select(railroad => new RailroadIncomeRow(
                railroad.Index,
                railroad.Name,
                railroad.ShortName ?? railroad.Name,
                ownership.GetValueOrDefault(railroad.Index),
                income.GetValueOrDefault(railroad.Index)))
            .OrderBy(row => row.OwnerIndex ?? int.MaxValue)
            .ThenByDescending(row => row.FeesCollected)
            .ThenBy(row => row.RailroadName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new RailroadIncome(rows);
    }

    /// <summary>Credits one owner's fee payment to the railroads the active player rode at the rate that set it.</summary>
    private static void AddPayment(Dictionary<int, int> income, GameState state, int ownerIndex, int amount, GameSettings settings)
    {
        var payer = state.ActivePlayerIndex >= 0 && state.ActivePlayerIndex < state.Players.Count
            ? state.Players[state.ActivePlayerIndex]
            : null;
        var fullRateRailroads = state.Turn.RailroadsRequiringFullOwnerRateThisTurn.ToHashSet();
        var ridden = state.Turn.RailroadsRiddenThisTurn
            .Distinct()
            .Where(railroadIndex => state.RailroadOwnership.GetValueOrDefault(railroadIndex) == ownerIndex)
            .Select(railroadIndex => (RailroadIndex: railroadIndex, Rate: payer?.GrandfatheredRailroadFees.FirstOrDefault(entry => entry.RailroadIndex == railroadIndex)?.FeeAmount
                ?? (fullRateRailroads.Contains(railroadIndex)
                    ? RailBaronGameEngine.GetUnfriendlyFee(settings, state.AllRailroadsSold)
                    : RailBaronGameEngine.GetPrivateFee(settings))))
            .ToList();
        if (ridden.Count == 0)
        {
            return;
        }

        var topRate = ridden.Max(entry => entry.Rate);
        var credited = ridden.Where(entry => entry.Rate == topRate).Select(entry => entry.RailroadIndex).ToList();
        for (var index = 0; index < credited.Count; index++)
        {
            // Whole dollars; the first railroad takes any remainder.
            var share = amount / credited.Count + (index == 0 ? amount % credited.Count : 0);
            income[credited[index]] = income.GetValueOrDefault(credited[index]) + share;
        }
    }
}

/// <param name="OwnerIndex">The railroad's owner in the latest state, or null when the bank holds it.</param>
/// <param name="FeesCollected">Fees opponents have paid for riding it this game, whoever owned it at the time.</param>
public sealed record RailroadIncomeRow(int RailroadIndex, string RailroadName, string ShortName, int? OwnerIndex, int FeesCollected);
