using Boxcars.Data.Maps;
using Boxcars.Engine.Data.Maps;

namespace Boxcars.Services.Maps;

public sealed class BoardViewportService
{
    public const double MinZoom = 100;
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

    public BoardViewport FitToPoints(
        MapDefinition mapDefinition,
        IEnumerable<(double X, double Y)> points,
        double targetAspectRatio,
        double maxZoomPercent,
        double paddingFactor = 0.16,
        double minimumVisibleMapFraction = 0.12)
    {
        var pointList = points.ToList();
        if (pointList.Count == 0)
        {
            return InitializeFitToBoard(mapDefinition);
        }

        var minX = pointList.Min(point => point.X);
        var maxX = pointList.Max(point => point.X);
        var minY = pointList.Min(point => point.Y);
        var maxY = pointList.Max(point => point.Y);

        var contentWidth = Math.Max(maxX - minX, mapDefinition.ScaleWidth * minimumVisibleMapFraction);
        var contentHeight = Math.Max(maxY - minY, mapDefinition.ScaleHeight * minimumVisibleMapFraction);

        var paddedWidth = contentWidth + Math.Max(contentWidth * paddingFactor, mapDefinition.ScaleWidth * 0.03);
        var paddedHeight = contentHeight + Math.Max(contentHeight * paddingFactor, mapDefinition.ScaleHeight * 0.03);

        var desiredAspectRatio = targetAspectRatio <= 0 ? 1.0 : targetAspectRatio;
        var currentAspectRatio = paddedWidth / Math.Max(0.0001, paddedHeight);

        if (currentAspectRatio < desiredAspectRatio)
        {
            paddedWidth = paddedHeight * desiredAspectRatio;
        }
        else
        {
            paddedHeight = paddedWidth / desiredAspectRatio;
        }

        var requestedZoom = Math.Min(
            mapDefinition.ScaleWidth / Math.Max(0.0001, paddedWidth),
            mapDefinition.ScaleHeight / Math.Max(0.0001, paddedHeight)) * 100.0;

        var clampedMaximumZoom = Math.Clamp(maxZoomPercent, MinZoom, MaxZoom);
        var zoomPercent = Math.Min(ClampZoom(requestedZoom), clampedMaximumZoom);
        if (zoomPercent <= MinZoom)
        {
            return InitializeFitToBoard(mapDefinition);
        }

        var actualView = GetViewBox(
            zoomPercent,
            mapDefinition.ScaleLeft + (mapDefinition.ScaleWidth / 2.0),
            mapDefinition.ScaleTop + (mapDefinition.ScaleHeight / 2.0),
            mapDefinition);

        var halfViewWidth = actualView.Width / 2.0;
        var halfViewHeight = actualView.Height / 2.0;
        var centeredX = (minX + maxX) / 2.0;
        var centeredY = (minY + maxY) / 2.0;

        var minCenterX = mapDefinition.ScaleLeft + halfViewWidth;
        var maxCenterX = mapDefinition.ScaleLeft + mapDefinition.ScaleWidth - halfViewWidth;
        var minCenterY = mapDefinition.ScaleTop + halfViewHeight;
        var maxCenterY = mapDefinition.ScaleTop + mapDefinition.ScaleHeight - halfViewHeight;

        return new BoardViewport
        {
            ZoomPercent = zoomPercent,
            CenterX = ClampCenter(centeredX, minCenterX, maxCenterX),
            CenterY = ClampCenter(centeredY, minCenterY, maxCenterY),
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

        if (clampedZoom <= MinZoom)
        {
            return InitializeFitToBoard(mapDefinition);
        }

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

    private static double ClampCenter(double value, double minimum, double maximum)
    {
        if (minimum > maximum)
        {
            return (minimum + maximum) / 2.0;
        }

        return Math.Clamp(value, minimum, maximum);
    }
}

public readonly record struct RelativePoint(double X, double Y, double Width, double Height);

public readonly record struct ViewBox(double X, double Y, double Width, double Height)
{
    public string ToSvgValue() => $"{X:F4} {Y:F4} {Width:F4} {Height:F4}";
}
