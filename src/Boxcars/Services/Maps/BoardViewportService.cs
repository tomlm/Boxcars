using Boxcars.Data.Maps;
using Boxcars.Engine.Data.Maps;

namespace Boxcars.Services.Maps;

public sealed class BoardViewportService
{
    public const double MinZoom = 50;
    public const double MaxZoom = 500;

    public BoardViewport InitializeFitToBoard(MapDefinition mapDefinition)
    {
        var centerX = mapDefinition.ScaleLeft + (mapDefinition.ScaleWidth / 2.0);
        var centerY = mapDefinition.ScaleTop + (mapDefinition.ScaleHeight / 2.0);

        return new BoardViewport
        {
            ZoomPercent = 100,
            CenterX = centerX,
            CenterY = centerY,
            ZoomAnchor = ZoomAnchor.ViewportCenter
        };
    }

    public BoardViewport UpdateViewportCentered(BoardViewport current, double requestedZoom)
    {
        return new BoardViewport
        {
            ZoomPercent = ClampZoom(requestedZoom),
            CenterX = current.CenterX,
            CenterY = current.CenterY,
            ZoomAnchor = ZoomAnchor.ViewportCenter
        };
    }

    public BoardViewport UpdateCursorAnchored(
        BoardViewport current,
        double requestedZoom,
        RelativePoint relativePoint,
        MapDefinition mapDefinition)
    {
        var clampedZoom = ClampZoom(requestedZoom);
        var currentView = GetViewBox(current, mapDefinition);
        var nextView = GetViewBox(clampedZoom, current.CenterX, current.CenterY, mapDefinition);

        var width = Math.Max(1, relativePoint.Width);
        var height = Math.Max(1, relativePoint.Height);

        var relativeX = Math.Clamp(relativePoint.X, 0, width);
        var relativeY = Math.Clamp(relativePoint.Y, 0, height);

        var boardPointX = currentView.X + (relativeX / width) * currentView.Width;
        var boardPointY = currentView.Y + (relativeY / height) * currentView.Height;

        var nextX = boardPointX - (relativeX / width) * nextView.Width;
        var nextY = boardPointY - (relativeY / height) * nextView.Height;

        return new BoardViewport
        {
            ZoomPercent = clampedZoom,
            CenterX = nextX + nextView.Width / 2.0,
            CenterY = nextY + nextView.Height / 2.0,
            ZoomAnchor = ZoomAnchor.Cursor
        };
    }

    public ViewBox GetViewBox(BoardViewport viewport, MapDefinition mapDefinition)
    {
        return GetViewBox(viewport.ZoomPercent, viewport.CenterX, viewport.CenterY, mapDefinition);
    }

    private static ViewBox GetViewBox(double zoomPercent, double centerX, double centerY, MapDefinition mapDefinition)
    {
        var width = mapDefinition.ScaleWidth * 100.0 / zoomPercent;
        var height = mapDefinition.ScaleHeight * 100.0 / zoomPercent;

        return new ViewBox(
            centerX - width / 2.0,
            centerY - height / 2.0,
            width,
            height);
    }

    public static double ClampZoom(double zoomPercent)
    {
        return Math.Clamp(zoomPercent, MinZoom, MaxZoom);
    }
}

public readonly record struct RelativePoint(double X, double Y, double Width, double Height);

public readonly record struct ViewBox(double X, double Y, double Width, double Height)
{
    public string ToSvgValue() => $"{X:F4} {Y:F4} {Width:F4} {Height:F4}";
}
