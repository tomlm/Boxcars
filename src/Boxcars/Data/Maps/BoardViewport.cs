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

public sealed class MapLoadResult
{
    public required bool Succeeded { get; init; }
    public MapDefinition? Definition { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}
