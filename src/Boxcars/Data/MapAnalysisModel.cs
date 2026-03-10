using Boxcars.Engine.Domain;

namespace Boxcars.Data;

public sealed class MapAnalysisReport
{
    public string MapName { get; init; } = string.Empty;

    public DateTimeOffset GeneratedAtUtc { get; init; }

    public IReadOnlyList<RailroadAnalysisRow> RailroadRows { get; init; } = [];

    public IReadOnlyList<CityAccessRow> CityAccessRows { get; init; } = [];

    public IReadOnlyList<RegionProbabilityRow> RegionProbabilityRows { get; init; } = [];

    public decimal AverageTripLengthDots { get; init; }

    public decimal AveragePayoff { get; init; }

    public decimal AveragePayoffPerDot { get; init; }
}

public sealed class RailroadAnalysisRow
{
    public int RailroadIndex { get; init; } = -1;

    public string RailroadCode { get; init; } = string.Empty;

    public string FullName { get; init; } = string.Empty;

    public int PurchasePrice { get; init; }

    public int CitiesServedCount { get; init; }

    public decimal ServicePercentage { get; init; }

    public decimal MonopolyPercentage { get; init; }

    public int ConnectionCount { get; init; }

    public decimal ExpectedIncome { get; init; }
}

public sealed class CityAccessRow
{
    public string RegionCode { get; init; } = string.Empty;

    public string RegionName { get; init; } = string.Empty;

    public string CityName { get; init; } = string.Empty;

    public decimal WithinRegionPercentage { get; init; }

    public decimal GlobalAccessPercentage { get; init; }
}

public sealed class RegionProbabilityRow
{
    public string RegionCode { get; init; } = string.Empty;

    public string RegionName { get; init; } = string.Empty;

    public decimal ProbabilityPercentage { get; init; }
}

public sealed class RecommendationInputSet
{
    public MapAnalysisReport MapAnalysisReport { get; init; } = new();

    public IReadOnlyList<int> AffordableRailroadIndices { get; init; } = [];

    public IReadOnlyList<int> UnownedRailroadIndices { get; init; } = [];

    public IReadOnlyList<LocomotiveType> EligibleEngineTypes { get; init; } = [];

    public NetworkCoverageSnapshot? CurrentCoverage { get; init; }

    public IReadOnlyDictionary<int, NetworkCoverageSnapshot> ProjectedCoverageByRailroad { get; init; } =
        new Dictionary<int, NetworkCoverageSnapshot>();
}