using Boxcars.Data;
using Boxcars.Engine.Domain;

namespace Boxcars.Services;

public sealed class PurchaseRecommendationService
{
    public RecommendationInputSet BuildInputSet(
        MapAnalysisReport mapAnalysisReport,
        IEnumerable<int> affordableRailroadIndices,
        IEnumerable<int> unownedRailroadIndices,
        IEnumerable<LocomotiveType> eligibleEngineTypes,
        NetworkCoverageSnapshot? currentCoverage,
        IReadOnlyDictionary<int, NetworkCoverageSnapshot> projectedCoverageByRailroad)
    {
        ArgumentNullException.ThrowIfNull(mapAnalysisReport);
        ArgumentNullException.ThrowIfNull(affordableRailroadIndices);
        ArgumentNullException.ThrowIfNull(unownedRailroadIndices);
        ArgumentNullException.ThrowIfNull(eligibleEngineTypes);
        ArgumentNullException.ThrowIfNull(projectedCoverageByRailroad);

        return new RecommendationInputSet
        {
            MapAnalysisReport = mapAnalysisReport,
            AffordableRailroadIndices = affordableRailroadIndices.Distinct().Order().ToList(),
            UnownedRailroadIndices = unownedRailroadIndices.Distinct().Order().ToList(),
            EligibleEngineTypes = eligibleEngineTypes.Distinct().Order().ToList(),
            CurrentCoverage = currentCoverage,
            ProjectedCoverageByRailroad = projectedCoverageByRailroad
        };
    }
}