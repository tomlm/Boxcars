namespace Boxcars.Data;

public sealed class RegionChoicePhaseModel
{
    public int PlayerIndex { get; init; } = -1;

    public string PlayerName { get; init; } = string.Empty;

    public string CurrentCityName { get; init; } = string.Empty;

    public string CurrentRegionCode { get; init; } = string.Empty;

    public string CurrentRegionName { get; init; } = string.Empty;

    public IReadOnlyList<DestinationRegionOption> Options { get; init; } = [];

    public string? SelectedRegionCode { get; init; }

    public bool CanConfirm { get; init; }
}

public sealed class DestinationRegionOption
{
    public string RegionCode { get; init; } = string.Empty;

    public string RegionName { get; init; } = string.Empty;

    public decimal RegionProbabilityPercent { get; init; }

    public decimal AccessibleDestinationPercent { get; init; }

    public decimal MonopolyDestinationPercent { get; init; }

    public int EligibleCityCount { get; init; }
}