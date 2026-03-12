namespace Boxcars.Data.Maps;

public sealed class PlayerMapState
{
    public string PlayerId { get; init; } = string.Empty;
    public string Color { get; init; } = "blue";
    public string? HomeCityName { get; init; }
    public string? TripStartCityName { get; init; }
    public string? CurrentCityName { get; init; }
    public string? StartNodeId { get; init; }
    public string? DestinationCityName { get; init; }
    public string? CurrentNodeId { get; init; }
    public IReadOnlyList<string> TraveledSegmentKeys { get; init; } = [];
    public IReadOnlyList<string> SelectedRouteSegmentKeys { get; init; } = [];
    public bool IsActiveTurn { get; init; }
    public bool IsCurrentUser { get; init; }
}
