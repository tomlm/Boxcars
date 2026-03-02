using Boxcars.Data.Maps;

namespace Boxcars.Services.Maps;

public sealed class BoardProjectionService
{
    private const double ParallelRailSpacing = 0.9;

    public BoardProjectionResult Project(MapDefinition mapDefinition)
    {
        var warnings = new List<string>();
        var cities = new List<CityRenderItem>();
        var railroadSegments = BuildRailroadSegments(mapDefinition, warnings);
        var regionLabels = mapDefinition.RegionLabels
            .Select(label => new RegionLabelRenderItem
            {
                Text = label.Text,
                X = label.X,
                Y = label.Y
            })
            .ToList();

        var regionCodeToIndex = mapDefinition.Regions
            .Select((region, index) => new { region.Code, RegionIndex = index + 1 })
            .ToDictionary(entry => entry.Code, entry => entry.RegionIndex, StringComparer.OrdinalIgnoreCase);

        foreach (var city in mapDefinition.Cities)
        {
            if (!regionCodeToIndex.TryGetValue(city.RegionCode, out var regionIndex))
            {
                warnings.Add($"City '{city.Name}' has unknown region code '{city.RegionCode}'.");
                continue;
            }

            var cityDot = mapDefinition.TrainDots.FirstOrDefault(dot =>
                dot.RegionIndex == regionIndex &&
                city.MapDotIndex.HasValue &&
                dot.DotIndex == city.MapDotIndex.Value);

            if (cityDot is null)
            {
                warnings.Add($"City '{city.Name}' references missing map dot '{city.MapDotIndex}' in region '{city.RegionCode}'.");
                continue;
            }

            cities.Add(new CityRenderItem
            {
                Name = city.Name,
                RegionCode = city.RegionCode,
                X = cityDot.X,
                Y = cityDot.Y
            });
        }

        var model = new BoardRenderModel
        {
            Cities = cities,
            TrainDots = mapDefinition.TrainDots,
            RailroadSegments = railroadSegments,
            RegionLabels = regionLabels,
            MapLines = mapDefinition.MapLines,
            Separators = mapDefinition.Separators
        };

        return new BoardProjectionResult
        {
            Model = model,
            Warnings = warnings
        };
    }

    private static List<RailroadRenderSegment> BuildRailroadSegments(MapDefinition mapDefinition, List<string> warnings)
    {
        var dotLookup = mapDefinition.TrainDots
            .ToDictionary(dot => (dot.RegionIndex, dot.DotIndex));

        var rawSegments = new List<RawRailroadSegment>();

        foreach (var route in mapDefinition.RailroadRouteSegments)
        {
            if (!dotLookup.TryGetValue((route.StartRegionIndex, route.StartDotIndex), out var startDot)
                || !dotLookup.TryGetValue((route.EndRegionIndex, route.EndDotIndex), out var endDot))
            {
                warnings.Add($"Railroad route point missing for railroad index '{route.RailroadIndex}' ({route.StartRegionIndex}:{route.StartDotIndex} -> {route.EndRegionIndex}:{route.EndDotIndex}).");
                continue;
            }

            var railroad = mapDefinition.Railroads.FirstOrDefault(item => item.Index == route.RailroadIndex);
            var strokeColor = ToRailroadColor(railroad);

            rawSegments.Add(new RawRailroadSegment
            {
                RailroadIndex = route.RailroadIndex,
                X1 = startDot.X,
                Y1 = startDot.Y,
                X2 = endDot.X,
                Y2 = endDot.Y,
                StrokeColor = strokeColor
            });
        }

        var projected = new List<RailroadRenderSegment>(rawSegments.Count);

        foreach (var grouping in rawSegments.GroupBy(GetEdgeKey))
        {
            var groupedSegments = grouping
                .OrderBy(segment => segment.RailroadIndex)
                .ToList();

            for (var index = 0; index < groupedSegments.Count; index++)
            {
                var segment = groupedSegments[index];
                var offset = (index - ((groupedSegments.Count - 1) / 2.0)) * ParallelRailSpacing;
                projected.Add(OffsetSegment(segment, offset));
            }
        }

        return projected;
    }

    private static RailroadRenderSegment OffsetSegment(RawRailroadSegment segment, double offset)
    {
        var dx = segment.X2 - segment.X1;
        var dy = segment.Y2 - segment.Y1;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length < 0.001)
        {
            return new RailroadRenderSegment
            {
                X1 = segment.X1,
                Y1 = segment.Y1,
                X2 = segment.X2,
                Y2 = segment.Y2,
                StrokeColor = segment.StrokeColor,
                IsOwned = false
            };
        }

        var offsetX = (-dy / length) * offset;
        var offsetY = (dx / length) * offset;

        return new RailroadRenderSegment
        {
            X1 = segment.X1 + offsetX,
            Y1 = segment.Y1 + offsetY,
            X2 = segment.X2 + offsetX,
            Y2 = segment.Y2 + offsetY,
            StrokeColor = segment.StrokeColor,
            IsOwned = false
        };
    }

    private static string GetEdgeKey(RawRailroadSegment segment)
    {
        var first = segment.X1 < segment.X2 || (Math.Abs(segment.X1 - segment.X2) < 0.0001 && segment.Y1 <= segment.Y2)
            ? (segment.X1, segment.Y1)
            : (segment.X2, segment.Y2);
        var second = first == (segment.X1, segment.Y1)
            ? (segment.X2, segment.Y2)
            : (segment.X1, segment.Y1);

        return $"{first.Item1:F3},{first.Item2:F3}|{second.Item1:F3},{second.Item2:F3}";
    }

    private static string ToRailroadColor(RailroadDefinition? railroad)
    {
        if (railroad?.Red is >= 0 and <= 255
            && railroad.Green is >= 0 and <= 255
            && railroad.Blue is >= 0 and <= 255)
        {
            return $"rgb({railroad.Red.Value},{railroad.Green.Value},{railroad.Blue.Value})";
        }

        return "rgb(96,96,96)";
    }

    private sealed class RawRailroadSegment
    {
        public required int RailroadIndex { get; init; }
        public required double X1 { get; init; }
        public required double Y1 { get; init; }
        public required double X2 { get; init; }
        public required double Y2 { get; init; }
        public required string StrokeColor { get; init; }
    }
}
