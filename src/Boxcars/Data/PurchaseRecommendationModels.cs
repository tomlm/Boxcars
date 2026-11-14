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
    TollBooth,

    /// <summary>The last railroad for sale into a region the buyer can't reach.</summary>
    Region
}

/// <summary>The engine upgrade recommendation shown on the purchase bar.</summary>
public enum EngineUpgradeRecommendation
{
    None,
    Express,
    Superchief,

    /// <summary>A Superchief is the pick, but the buyer can't afford one yet.</summary>
    SaveForSuperchief
}

/// <param name="Reason">Short explanation of why the railroad wins the category, e.g. "+12.3% access".</param>
/// <param name="SpareCash">Cash left over the operating reserve after buying it, paying fees and collecting incoming fees; negative for a railroad to save for (how far short).</param>
public sealed record PurchaseRecommendation(
    PurchaseRecommendationCategory Category,
    int RailroadIndex,
    string RailroadName,
    int PurchasePrice,
    string Reason,
    int SpareCash)
{
    /// <summary>False for a railroad to save for: the buyer can't safely afford it yet.</summary>
    public bool CanBuyNow => SpareCash >= 0;
}

/// <param name="IncomingFees">Fees opponents are expected to pay the buyer soon with their current railroads.</param>
/// <param name="OperatingReserve">Reserve the buyer should keep with their current network (each pick uses its own).</param>
/// <param name="EngineUpgrade">See <c>PurchaseRecommendationService.RecommendEngineUpgrade</c>.</param>
public sealed record PurchaseRecommendationSet(
    IReadOnlyList<PurchaseRecommendation> Recommendations,
    int Cash,
    int FeesOwed,
    int IncomingFees,
    int OperatingReserve,
    EngineUpgradeRecommendation EngineUpgrade = EngineUpgradeRecommendation.None);

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
/// <param name="SpareCash">Cash left over the reserve after buying it; negative for one to save for (how far short).</param>
/// <param name="CanBuyNow">False for a railroad to save for, which can't be selected yet.</param>
public sealed record PurchaseRecommendationModel(
    string CategoryLabel,
    string OptionKey,
    string RailroadName,
    int PurchasePrice,
    string Reason,
    int SpareCash,
    bool CanBuyNow = true);

/// <summary>The buyer's budget and recommendations for the purchase bar.</summary>
/// <param name="EngineUpgradeOptionKey">The option key that selects the recommended engine; null when there's none to buy now.</param>
public sealed record PurchaseRecommendationsModel(
    IReadOnlyList<PurchaseRecommendationModel> Recommendations,
    int Cash,
    int FeesOwed,
    int IncomingFees,
    int OperatingReserve,
    EngineUpgradeRecommendation EngineUpgrade = EngineUpgradeRecommendation.None,
    string? EngineUpgradeOptionKey = null)
{
    public static PurchaseRecommendationsModel Empty { get; } = new([], 0, 0, 0, 0);
}
