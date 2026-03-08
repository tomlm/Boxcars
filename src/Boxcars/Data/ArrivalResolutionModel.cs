namespace Boxcars.Data;

public sealed class ArrivalResolutionModel
{
    public int PlayerIndex { get; init; } = -1;
    public string DestinationCityName { get; init; } = string.Empty;
    public int PayoutAmount { get; init; }
    public int CashAfterPayout { get; init; }
    public bool PurchaseOpportunityAvailable { get; init; }
    public string Message { get; init; } = string.Empty;
    public bool IsVisible { get; init; }
}