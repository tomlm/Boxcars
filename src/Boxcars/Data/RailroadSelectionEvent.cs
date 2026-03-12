namespace Boxcars.Data;

public sealed class RailroadSelectionEvent
{
    public int RailroadIndex { get; init; }

    public double ClientX { get; init; }

    public double ClientY { get; init; }
}