namespace Boxcars.Engine.Domain;

public sealed class PendingRegionChoice
{
    public int PlayerIndex { get; init; } = -1;

    public string CurrentCityName { get; init; } = string.Empty;

    public string CurrentRegionCode { get; init; } = string.Empty;

    public string TriggeredByInitialRegionCode { get; init; } = string.Empty;

    public IReadOnlyList<string> EligibleRegionCodes { get; init; } = [];

    public IReadOnlyDictionary<string, int> EligibleCityCountsByRegion { get; init; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
}