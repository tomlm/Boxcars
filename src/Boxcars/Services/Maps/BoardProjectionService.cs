using Boxcars.Data.Maps;
using Boxcars.Engine.Data.Maps;
using System.Globalization;
using System.Text;

namespace Boxcars.Services.Maps;

public sealed class BoardProjectionService
{
    private const double ParallelRailSpacing = 0.9;

    public BoardProjectionResult Project(MapDefinition mapDefinition)
    {
        var warnings = new List<string>();
        var regionFills = BuildRegionFills(mapDefinition, warnings);
        var cities = new List<CityRenderItem>();
        var railroadSegments = BuildRailroadSegments(mapDefinition, warnings);
        var routeNodes = BuildRouteNodes(mapDefinition, warnings);
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
            RegionFills = regionFills,
            Cities = cities,
            TrainDots = mapDefinition.TrainDots,
            RailroadSegments = railroadSegments,
            RouteNodes = routeNodes,
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

    private static List<RegionFillRenderItem> BuildRegionFills(MapDefinition mapDefinition, List<string> warnings)
    {
        var fills = new List<RegionFillRenderItem>(mapDefinition.Regions.Count);
        var bounds = FloodBounds.Create(mapDefinition);
        var blocked = new bool[bounds.Width, bounds.Height];
        var owner = new int[bounds.Width, bounds.Height];

        DrawBoundarySegments(mapDefinition.MapLines, bounds, blocked);
        DrawBoundarySegments(mapDefinition.Separators, bounds, blocked);

        var queue = new Queue<(int X, int Y, int RegionIndex)>();

        foreach (var indexedRegion in mapDefinition.Regions.Select((region, index) => new { Region = region, RegionIndex = index + 1 }))
        {
            var seeds = GetRegionSeedCandidates(mapDefinition, indexedRegion.RegionIndex, indexedRegion.Region);
            if (seeds.Count == 0)
            {
                warnings.Add($"Region '{indexedRegion.Region.Name}' is missing a valid seed point.");
                continue;
            }

            var seeded = false;
            foreach (var seed in seeds)
            {
                if (!TryFindOpenCell(seed, bounds, blocked, owner, out var openCell))
                {
                    continue;
                }

                owner[openCell.X, openCell.Y] = indexedRegion.RegionIndex;
                queue.Enqueue((openCell.X, openCell.Y, indexedRegion.RegionIndex));
                seeded = true;
                break;
            }

            if (!seeded)
            {
                warnings.Add($"Region '{indexedRegion.Region.Name}' could not place a seed inside an open area.");
            }
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            TrySpread(current.X + 1, current.Y, current.RegionIndex, bounds, blocked, owner, queue);
            TrySpread(current.X - 1, current.Y, current.RegionIndex, bounds, blocked, owner, queue);
            TrySpread(current.X, current.Y + 1, current.RegionIndex, bounds, blocked, owner, queue);
            TrySpread(current.X, current.Y - 1, current.RegionIndex, bounds, blocked, owner, queue);
        }

        foreach (var indexedRegion in mapDefinition.Regions.Select((region, index) => new { Region = region, RegionIndex = index + 1 }))
        {
            var pathData = BuildFloodPathData(indexedRegion.RegionIndex, bounds, owner);
            if (string.IsNullOrWhiteSpace(pathData))
            {
                warnings.Add($"Region '{indexedRegion.Region.Name}' produced an empty fill area.");
                continue;
            }

            fills.Add(new RegionFillRenderItem
            {
                RegionIndex = indexedRegion.RegionIndex,
                RegionCode = indexedRegion.Region.Code,
                PathData = pathData,
                FillColor = GetRegionFillColor(indexedRegion.Region.Code)
            });
        }

        return fills;
    }

    private static void DrawBoundarySegments(IEnumerable<LineSegment> segments, FloodBounds bounds, bool[,] blocked)
    {
        foreach (var segment in segments)
        {
            var x0 = bounds.ToGridX(segment.X1);
            var y0 = bounds.ToGridY(segment.Y1);
            var x1 = bounds.ToGridX(segment.X2);
            var y1 = bounds.ToGridY(segment.Y2);
            DrawBoundaryLine(x0, y0, x1, y1, blocked);
        }
    }

    private static void DrawBoundaryLine(int x0, int y0, int x1, int y1, bool[,] blocked)
    {
        var dx = Math.Abs(x1 - x0);
        var sx = x0 < x1 ? 1 : -1;
        var dy = -Math.Abs(y1 - y0);
        var sy = y0 < y1 ? 1 : -1;
        var error = dx + dy;

        while (true)
        {
            MarkBlocked(x0, y0, blocked);
            if (x0 == x1 && y0 == y1)
            {
                break;
            }

            var e2 = 2 * error;
            if (e2 >= dy)
            {
                error += dy;
                x0 += sx;
            }

            if (e2 <= dx)
            {
                error += dx;
                y0 += sy;
            }
        }
    }

    private static void MarkBlocked(int gridX, int gridY, bool[,] blocked)
    {
        if (gridX < 0 || gridY < 0 || gridX >= blocked.GetLength(0) || gridY >= blocked.GetLength(1))
        {
            return;
        }

        blocked[gridX, gridY] = true;
    }

    private static bool TryFindOpenCell(Point seed, FloodBounds bounds, bool[,] blocked, int[,] owner, out (int X, int Y) cell)
    {
        var centerGridX = bounds.ToGridX(seed.X);
        var centerGridY = bounds.ToGridY(seed.Y);

        for (var radius = 0; radius <= 36; radius++)
        {
            for (var y = centerGridY - radius; y <= centerGridY + radius; y++)
            {
                var leftX = centerGridX - radius;
                var rightX = centerGridX + radius;

                if (TryCandidate(leftX, y, bounds, blocked, owner, out cell)
                    || TryCandidate(rightX, y, bounds, blocked, owner, out cell))
                {
                    return true;
                }
            }

            for (var x = centerGridX - radius + 1; x <= centerGridX + radius - 1; x++)
            {
                var topY = centerGridY - radius;
                var bottomY = centerGridY + radius;

                if (TryCandidate(x, topY, bounds, blocked, owner, out cell)
                    || TryCandidate(x, bottomY, bounds, blocked, owner, out cell))
                {
                    return true;
                }
            }
        }

        cell = default;
        return false;
    }

    private static bool TryCandidate(int gridX, int gridY, FloodBounds bounds, bool[,] blocked, int[,] owner, out (int X, int Y) cell)
    {
        if (gridX < 0 || gridY < 0 || gridX >= bounds.Width || gridY >= bounds.Height
            || blocked[gridX, gridY]
            || owner[gridX, gridY] != 0)
        {
            cell = default;
            return false;
        }

        cell = (gridX, gridY);
        return true;
    }

    private static void TrySpread(
        int gridX,
        int gridY,
        int regionIndex,
        FloodBounds bounds,
        bool[,] blocked,
        int[,] owner,
        Queue<(int X, int Y, int RegionIndex)> queue)
    {
        if (gridX < 0 || gridY < 0 || gridX >= bounds.Width || gridY >= bounds.Height)
        {
            return;
        }

        if (blocked[gridX, gridY] || owner[gridX, gridY] != 0)
        {
            return;
        }

        owner[gridX, gridY] = regionIndex;
        queue.Enqueue((gridX, gridY, regionIndex));
    }

    private static string BuildFloodPathData(int regionIndex, FloodBounds bounds, int[,] owner)
    {
        var pieces = new List<string>();
        for (var gridY = 0; gridY < bounds.Height; gridY++)
        {
            var gridX = 0;
            while (gridX < bounds.Width)
            {
                if (owner[gridX, gridY] != regionIndex)
                {
                    gridX++;
                    continue;
                }

                var runStart = gridX;
                while (gridX < bounds.Width && owner[gridX, gridY] == regionIndex)
                {
                    gridX++;
                }

                var runWidth = gridX - runStart;
                var mapX = bounds.ToMapX(runStart);
                var mapY = bounds.ToMapY(gridY);
                var mapWidth = FloodBounds.GridUnitToMapUnits(runWidth);
                var mapCellHeight = FloodBounds.CellSize;
                pieces.Add(FormattableString.Invariant($"M {mapX:F3} {mapY:F3} h {mapWidth:F3} v {mapCellHeight:F3} h {-mapWidth:F3} Z"));
            }
        }

        return string.Join(" ", pieces);
    }

    private static List<ClosedFace> BuildClosedFaces(IReadOnlyList<LineSegment> segments)
    {
        var edgeMap = new Dictionary<VertexKey, HashSet<VertexKey>>();
        foreach (var segment in segments)
        {
            var from = VertexKey.From(segment.X1, segment.Y1);
            var to = VertexKey.From(segment.X2, segment.Y2);
            if (from == to)
            {
                continue;
            }

            if (!edgeMap.TryGetValue(from, out var fromNeighbors))
            {
                fromNeighbors = new HashSet<VertexKey>();
                edgeMap[from] = fromNeighbors;
            }

            if (!edgeMap.TryGetValue(to, out var toNeighbors))
            {
                toNeighbors = new HashSet<VertexKey>();
                edgeMap[to] = toNeighbors;
            }

            fromNeighbors.Add(to);
            toNeighbors.Add(from);
        }

        var adjacency = edgeMap.ToDictionary(
            entry => entry.Key,
            entry => entry.Value
                .Select(neighbor => new DirectedNeighbor(neighbor, Math.Atan2(neighbor.Y - entry.Key.Y, neighbor.X - entry.Key.X)))
                .OrderBy(neighbor => neighbor.Angle)
                .ToList());

        var usedDirected = new HashSet<(VertexKey From, VertexKey To)>();
        var faces = new List<ClosedFace>();

        foreach (var entry in adjacency)
        {
            foreach (var neighbor in entry.Value)
            {
                var startDirected = (entry.Key, neighbor.Key);
                if (usedDirected.Contains(startDirected))
                {
                    continue;
                }

                var cycle = TraceFace(startDirected, adjacency, usedDirected);
                if (cycle.Count < 3)
                {
                    continue;
                }

                var area = Math.Abs(SignedArea(cycle));
                if (area < 1)
                {
                    continue;
                }

                faces.Add(new ClosedFace(cycle, area));
            }
        }

        return faces
            .GroupBy(face => string.Join("|", face.Vertices.Select(vertex => $"{vertex.X:F3},{vertex.Y:F3}")))
            .Select(group => group.First())
            .ToList();
    }

    private static List<Point> TraceFace(
        (VertexKey From, VertexKey To) startDirected,
        IReadOnlyDictionary<VertexKey, List<DirectedNeighbor>> adjacency,
        HashSet<(VertexKey From, VertexKey To)> usedDirected)
    {
        var cycle = new List<Point>();
        var current = startDirected;
        var guard = 0;

        while (guard++ < 10000)
        {
            usedDirected.Add(current);
            cycle.Add(new Point(current.From.X, current.From.Y));

            if (!adjacency.TryGetValue(current.To, out var outgoing) || outgoing.Count == 0)
            {
                return new List<Point>();
            }

            var reverseIndex = outgoing.FindIndex(neighbor => neighbor.Key == current.From);
            if (reverseIndex < 0)
            {
                return new List<Point>();
            }

            var nextIndex = (reverseIndex - 1 + outgoing.Count) % outgoing.Count;
            var next = outgoing[nextIndex].Key;
            current = (current.To, next);

            if (current == startDirected)
            {
                break;
            }
        }

        return guard >= 10000 ? new List<Point>() : cycle;
    }

    private static List<Point> GetRegionSeedCandidates(MapDefinition mapDefinition, int regionIndex, RegionDefinition region)
    {
        var candidates = new List<Point>();

        var labelSeed = TryGetRegionLabelSeed(mapDefinition.RegionLabels, region);
        if (labelSeed is not null)
        {
            candidates.Add(labelSeed.Value);
        }

        var regionDots = mapDefinition.TrainDots
            .Where(dot => dot.RegionIndex == regionIndex)
            .ToList();

        if (regionDots.Count > 0)
        {
            candidates.Add(new Point(regionDots.Average(dot => dot.X), regionDots.Average(dot => dot.Y)));
        }

        return candidates;
    }

    private static Point? TryGetRegionLabelSeed(IReadOnlyList<RegionLabelDefinition> labels, RegionDefinition region)
    {
        var normalizedCode = NormalizeRegionLabelToken(region.Code);
        var normalizedName = NormalizeRegionLabelToken(region.Name);

        var label = labels.FirstOrDefault(item =>
        {
            var normalizedText = NormalizeRegionLabelToken(item.Text);
            return normalizedText.Equals(normalizedCode, StringComparison.OrdinalIgnoreCase)
                   || normalizedText.Equals(normalizedName, StringComparison.OrdinalIgnoreCase);
        });

        return label is null ? null : new Point(label.X, label.Y);
    }

    private static string NormalizeRegionLabelToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value.Where(char.IsLetterOrDigit).ToArray());
    }

    private static bool PointInPolygon(Point point, IReadOnlyList<Point> polygon)
    {
        var inside = false;
        for (var i = 0; i < polygon.Count; i++)
        {
            var j = (i + polygon.Count - 1) % polygon.Count;
            var pi = polygon[i];
            var pj = polygon[j];

            var intersects = ((pi.Y > point.Y) != (pj.Y > point.Y))
                && (point.X < ((pj.X - pi.X) * (point.Y - pi.Y) / ((pj.Y - pi.Y) + 1e-9)) + pi.X);
            if (intersects)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static double SignedArea(IReadOnlyList<Point> polygon)
    {
        var area = 0.0;
        for (var index = 0; index < polygon.Count; index++)
        {
            var next = (index + 1) % polygon.Count;
            area += (polygon[index].X * polygon[next].Y) - (polygon[next].X * polygon[index].Y);
        }

        return area / 2.0;
    }

    private static string BuildPathData(IReadOnlyList<Point> polygon)
    {
        var coordinates = polygon
            .Select(point => $"{point.X.ToString("F3", CultureInfo.InvariantCulture)} {point.Y.ToString("F3", CultureInfo.InvariantCulture)}");

        return $"M {string.Join(" L ", coordinates)} Z";
    }

    private static string GetRegionFillColor(string regionCode)
    {
        return regionCode.ToUpperInvariant() switch
        {
            "NE" => "rgb(236, 208, 242)",
            "SE" => "rgb(186, 224, 229)",
            "NC" => "rgb(166, 166, 170)",
            "SC" => "rgb(192, 192, 196)",
            "PL" => "rgb(132, 208, 174)",
            "NW" => "rgb(220, 156, 224)",
            "SW" => "rgb(234, 166, 166)",
            _ => "rgb(200, 200, 200)"
        };
    }

    private readonly record struct Point(double X, double Y);

    private readonly record struct FloodBounds(int MinX, int MinY, int MaxX, int MaxY)
    {
        private const int GridScale = 2;

        public int Width => ((MaxX - MinX) * GridScale) + 1;
        public int Height => ((MaxY - MinY) * GridScale) + 1;

        public static FloodBounds Create(MapDefinition mapDefinition)
        {
            var minX = (int)Math.Floor(mapDefinition.ScaleLeft);
            var minY = (int)Math.Floor(mapDefinition.ScaleTop);
            var maxX = (int)Math.Ceiling(mapDefinition.ScaleLeft + mapDefinition.ScaleWidth);
            var maxY = (int)Math.Ceiling(mapDefinition.ScaleTop + mapDefinition.ScaleHeight);

            return new FloodBounds(minX, minY, maxX, maxY);
        }

        public bool TryMapToGrid(int mapX, int mapY, out int gridX, out int gridY)
        {
            gridX = ToGridX(mapX);
            gridY = ToGridY(mapY);
            return gridX >= 0
                   && gridY >= 0
                   && gridX < Width
                   && gridY < Height;
        }

        public int ToGridX(double mapX) => (int)Math.Round((mapX - MinX) * GridScale, MidpointRounding.AwayFromZero);

        public int ToGridY(double mapY) => (int)Math.Round((mapY - MinY) * GridScale, MidpointRounding.AwayFromZero);

        public double ToMapX(int gridX) => MinX + (gridX / (double)GridScale);

        public double ToMapY(int gridY) => MinY + (gridY / (double)GridScale);

        public static double GridUnitToMapUnits(int gridUnits) => gridUnits / (double)GridScale;

        public static double CellSize => 1d / GridScale;
    }

    private readonly record struct VertexKey(int X, int Y)
    {
        public static VertexKey From(double x, double y)
        {
            return new VertexKey(
                (int)Math.Round(x, MidpointRounding.AwayFromZero),
                (int)Math.Round(y, MidpointRounding.AwayFromZero));
        }
    }

    private readonly record struct DirectedNeighbor(VertexKey Key, double Angle);

    private sealed record ClosedFace(IReadOnlyList<Point> Vertices, double Area);

    private static List<RouteNodeRenderItem> BuildRouteNodes(MapDefinition mapDefinition, List<string> warnings)
    {
        var regionCodeToIndex = mapDefinition.Regions
            .Select((region, index) => new { region.Code, RegionIndex = index + 1 })
            .ToDictionary(entry => entry.Code, entry => entry.RegionIndex, StringComparer.OrdinalIgnoreCase);

        var dotLookup = mapDefinition.TrainDots.ToDictionary(dot => (dot.RegionIndex, dot.DotIndex));

        var routeNodes = mapDefinition.TrainDots
            .Select(dot => new RouteNodeRenderItem
            {
                NodeId = MapRouteService.NodeKey(dot.RegionIndex, dot.DotIndex),
                Name = dot.Id,
                RegionIndex = dot.RegionIndex,
                DotIndex = dot.DotIndex,
                X = dot.X,
                Y = dot.Y
            })
            .ToDictionary(node => node.NodeId, StringComparer.OrdinalIgnoreCase);

        foreach (var city in mapDefinition.Cities)
        {
            if (!city.MapDotIndex.HasValue)
            {
                continue;
            }

            if (!regionCodeToIndex.TryGetValue(city.RegionCode, out var regionIndex))
            {
                continue;
            }

            var dotIndex = city.MapDotIndex.Value;
            if (!dotLookup.TryGetValue((regionIndex, dotIndex), out var dot))
            {
                warnings.Add($"Route node city '{city.Name}' references missing map dot '{dotIndex}' in region '{city.RegionCode}'.");
                continue;
            }

            var nodeId = MapRouteService.NodeKey(regionIndex, dotIndex);
            if (!routeNodes.TryGetValue(nodeId, out var existingNode))
            {
                routeNodes[nodeId] = new RouteNodeRenderItem
                {
                    NodeId = nodeId,
                    Name = city.Name,
                    RegionIndex = regionIndex,
                    DotIndex = dotIndex,
                    X = dot.X,
                    Y = dot.Y
                };
                continue;
            }

            routeNodes[nodeId] = new RouteNodeRenderItem
            {
                NodeId = nodeId,
                Name = city.Name,
                RegionIndex = regionIndex,
                DotIndex = dotIndex,
                X = existingNode.X,
                Y = existingNode.Y
            };
        }

        return routeNodes.Values
            .OrderBy(node => node.RegionIndex)
            .ThenBy(node => node.DotIndex)
            .ToList();
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
                StartRegionIndex = route.StartRegionIndex,
                StartDotIndex = route.StartDotIndex,
                EndRegionIndex = route.EndRegionIndex,
                EndDotIndex = route.EndDotIndex,
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
                RailroadIndex = segment.RailroadIndex,
                StartRegionIndex = segment.StartRegionIndex,
                StartDotIndex = segment.StartDotIndex,
                EndRegionIndex = segment.EndRegionIndex,
                EndDotIndex = segment.EndDotIndex,
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
            RailroadIndex = segment.RailroadIndex,
            StartRegionIndex = segment.StartRegionIndex,
            StartDotIndex = segment.StartDotIndex,
            EndRegionIndex = segment.EndRegionIndex,
            EndDotIndex = segment.EndDotIndex,
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
        public required int StartRegionIndex { get; init; }
        public required int StartDotIndex { get; init; }
        public required int EndRegionIndex { get; init; }
        public required int EndDotIndex { get; init; }
        public required double X1 { get; init; }
        public required double Y1 { get; init; }
        public required double X2 { get; init; }
        public required double Y2 { get; init; }
        public required string StrokeColor { get; init; }
    }
}
