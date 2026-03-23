namespace Boxcars.Data;

public sealed class HomeSwapPhaseModel
{
    public int PlayerIndex { get; init; } = -1;

    public string PlayerName { get; init; } = string.Empty;

    public string CurrentHomeCityName { get; init; } = string.Empty;

    public string FirstDestinationCityName { get; init; } = string.Empty;

    public bool CanConfirm { get; init; }
}