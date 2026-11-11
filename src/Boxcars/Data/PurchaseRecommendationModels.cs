using Boxcars.Engine.Domain;

namespace Boxcars.Data;

/// <summary>Purchase suggestion categories, in the order they're shown.</summary>
public enum PurchaseRecommendationCategory
{
    Blended,
    Access,
    Connectivity,
    Monopoly,
    Strategy,
    LowPrice,

    /// <summary>Buying it leaves an opponent's destination reachable only on the buyer's railroads.</summary>
    TollBooth
}

/// <param name="Reason">Short explanation of why the railroad wins the category, e.g. "+12.3% access".</param>
/// <param name="SpareCash">Cash left over the operating reserve after buying it, paying fees and collecting incoming fees.</param>
public sealed record PurchaseRecommendation(
    PurchaseRecommendationCategory Category,
    int RailroadIndex,
    string RailroadName,
    int PurchasePrice,
    string Reason,
    int SpareCash);

/// <param name="IncomingFees">Fees opponents are expected to pay the buyer soon with their current railroads.</param>
/// <param name="OperatingReserve">Reserve the buyer should keep with their current network (each pick uses its own).</param>
/// <param name="EngineUpgrade">The engine to upgrade to, or null for none (see <c>PurchaseRecommendationService.RecommendEngineUpgrade</c>).</param>
public sealed record PurchaseRecommendationSet(
    IReadOnlyList<PurchaseRecommendation> Recommendations,
    int Cash,
    int FeesOwed,
    int IncomingFees,
    int OperatingReserve,
    LocomotiveType? EngineUpgrade = null);

/// <summary>What the engine upgrade recommendation needs beyond the buyer's finances.</summary>
/// <param name="UnsoldShare">Share of the map's railroads still for sale (0-1).</param>
public sealed record EngineUpgradeInputs(
    LocomotiveType CurrentEngine,
    int ExpressPrice,
    int SuperchiefPrice,
    int WinningCash,
    decimal UnsoldShare);

/// <summary>A railroad the buyer can afford.</summary>
public sealed record PurchaseCandidate(int RailroadIndex, string RailroadName, int PurchasePrice);

/// <param name="DestinationCityName">Where the opponent is heading, or null when they have no destination.</param>
public sealed record RecommendationOpponent(string Name, int Cash, IReadOnlyCollection<int> OwnedRailroadIndices, string? DestinationCityName = null);

/// <summary>What the buyer can safely spend.</summary>
/// <param name="OperatingReserve">Cash to keep after buying (see <c>OperatingCapitalService.CalculateOperatingReserve</c>).</param>
/// <param name="EstimateIncomingFees">Fees opponents are expected to pay soon for a given set of owned railroads (see <c>OperatingCapitalService.EstimateIncomingFees</c>).</param>
public sealed record PurchaseFinances(
    int Cash,
    int FeesOwed,
    int OperatingReserve,
    Func<IReadOnlyCollection<int>, int> EstimateIncomingFees);

/// <summary>A recommendation ready for the purchase bar, with the option key that selects it.</summary>
public sealed record PurchaseRecommendationModel(
    string CategoryLabel,
    string OptionKey,
    string RailroadName,
    int PurchasePrice,
    string Reason,
    int SpareCash);

/// <summary>The buyer's budget and recommendations for the purchase bar.</summary>
/// <param name="EngineUpgrade">The engine to upgrade to, or null for none.</param>
/// <param name="EngineUpgradeOptionKey">The option key that selects <paramref name="EngineUpgrade"/>, or null for none.</param>
public sealed record PurchaseRecommendationsModel(
    IReadOnlyList<PurchaseRecommendationModel> Recommendations,
    int Cash,
    int FeesOwed,
    int IncomingFees,
    int OperatingReserve,
    LocomotiveType? EngineUpgrade = null,
    string? EngineUpgradeOptionKey = null)
{
    public static PurchaseRecommendationsModel Empty { get; } = new([], 0, 0, 0, 0);
}
