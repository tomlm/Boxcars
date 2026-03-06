namespace Boxcars.Data.Maps;

public enum ZoomAnchor
{
    Cursor,
    ViewportCenter
}

public sealed class BoardViewport
{
    public required double ZoomPercent { get; init; }
    public required double CenterX { get; init; }
    public required double CenterY { get; init; }
    public required ZoomAnchor ZoomAnchor { get; init; }
}
