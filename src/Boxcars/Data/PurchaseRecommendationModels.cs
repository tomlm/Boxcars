namespace Boxcars.Data;

/// <summary>Purchase suggestion categories, in the order they're shown.</summary>
public enum PurchaseRecommendationCategory
{
    Blended,
    Access,
    Connectivity,
    Monopoly,
    Strategy,
    LowPrice
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

/// <param name="IncomingFees">Fees opponents are about to pay the buyer with their current railroads.</param>
/// <param name="OperatingReserve">Reserve the buyer should keep with their current network (each pick uses its own).</param>
public sealed record PurchaseRecommendationSet(
    IReadOnlyList<PurchaseRecommendation> Recommendations,
    int Cash,
    int FeesOwed,
    int IncomingFees,
    int OperatingReserve);

/// <summary>A railroad the buyer can afford.</summary>
public sealed record PurchaseCandidate(int RailroadIndex, string RailroadName, int PurchasePrice);

public sealed record RecommendationOpponent(string Name, int Cash, IReadOnlyCollection<int> OwnedRailroadIndices);

/// <summary>What the buyer can safely spend.</summary>
/// <param name="OperatingReserve">Cash to keep after buying (see <c>OperatingCapitalService.CalculateOperatingReserve</c>).</param>
/// <param name="EstimateIncomingFees">Fees opponents are about to pay for a given set of owned railroads.</param>
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
public sealed record PurchaseRecommendationsModel(
    IReadOnlyList<PurchaseRecommendationModel> Recommendations,
    int Cash,
    int FeesOwed,
    int IncomingFees,
    int OperatingReserve)
{
    public static PurchaseRecommendationsModel Empty { get; } = new([], 0, 0, 0, 0);
}
