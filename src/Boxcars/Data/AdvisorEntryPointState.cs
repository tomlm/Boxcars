namespace Boxcars.Data;

public sealed class AdvisorEntryPointState
{
    public string GameId { get; set; } = string.Empty;
    public bool IsOpen { get; set; }
    public bool IsLoading { get; set; }
    public bool HasAvailabilityError { get; set; }
    public string AvailabilityMessage { get; set; } = string.Empty;
}