namespace Boxcars.Engine.Domain;

public sealed class PendingRegionChoice
{
    public int PlayerIndex { get; init; } = -1;

    public string CurrentCityName { get; init; } = string.Empty;

    public string CurrentRegionCode { get; init; } = string.Empty;

    public string TriggeredByInitialRegionCode { get; init; } = string.Empty;

    public PendingDestinationAssignmentKind AssignmentKind { get; init; } = PendingDestinationAssignmentKind.NormalDestination;

    public IReadOnlyList<string> EligibleRegionCodes { get; init; } = [];

    public IReadOnlyDictionary<string, int> EligibleCityCountsByRegion { get; init; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
}

public sealed class PendingHomeCityChoice
{
    public int PlayerIndex { get; init; } = -1;

    public string RegionCode { get; init; } = string.Empty;

    public string RegionName { get; init; } = string.Empty;

    public string CurrentHomeCityName { get; init; } = string.Empty;

    public IReadOnlyList<string> EligibleCityNames { get; init; } = [];
}

public sealed class PendingHomeSwap
{
    public int PlayerIndex { get; init; } = -1;

    public string CurrentHomeCityName { get; init; } = string.Empty;

    public string FirstDestinationCityName { get; init; } = string.Empty;
}