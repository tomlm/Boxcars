namespace Boxcars.Data;

public sealed class HomeCityChoicePhaseModel
{
    public int PlayerIndex { get; init; } = -1;

    public string PlayerName { get; init; } = string.Empty;

    public string RegionCode { get; init; } = string.Empty;

    public string RegionName { get; init; } = string.Empty;

    public string CurrentHomeCityName { get; init; } = string.Empty;

    public IReadOnlyList<HomeCityOption> Options { get; init; } = [];

    public bool CanConfirm { get; init; }
}

public sealed class HomeCityOption
{
    public string CityName { get; init; } = string.Empty;

    public bool IsCurrentSelection { get; init; }
}