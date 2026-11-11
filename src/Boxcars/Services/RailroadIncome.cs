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
    /// The fees the engine has credited to each railroad in the game's statistics (see
    /// GameStatistics.RailroadFeesCollected). Every railroad on the map gets a row, under its current owner (or none).
    /// </summary>
    public static RailroadIncome Build(GameState state, MapDefinition mapDefinition)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(mapDefinition);

        var income = state.Statistics?.RailroadFeesCollected ?? [];
        var ownership = state.RailroadOwnership;
        var rows = mapDefinition.Railroads
            .Select(railroad => new RailroadIncomeRow(
                railroad.Index,
                railroad.Name,
                railroad.ShortName ?? railroad.Name,
                railroad.PurchasePrice ?? RailBaronGameEngine.GetRailroadPurchasePrice(railroad.Index),
                ownership.GetValueOrDefault(railroad.Index),
                income.GetValueOrDefault(railroad.Index)))
            .OrderBy(row => row.OwnerIndex ?? int.MaxValue)
            .ThenByDescending(row => row.FeesCollected)
            .ThenBy(row => row.RailroadName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new RailroadIncome(rows);
    }
}

/// <param name="PurchasePrice">The railroad's list price in this game.</param>
/// <param name="OwnerIndex">The railroad's owner in the latest state, or null when the bank holds it.</param>
/// <param name="FeesCollected">Fees opponents have paid for riding it this game, whoever owned it at the time.</param>
public sealed record RailroadIncomeRow(int RailroadIndex, string RailroadName, string ShortName, int PurchasePrice, int? OwnerIndex, int FeesCollected)
{
    /// <summary>Fees collected as a multiple of the purchase price; 1 means it has paid for itself.</summary>
    public decimal Multiplier => PurchasePrice > 0 ? (decimal)FeesCollected / PurchasePrice : 0m;
}
