using System.Globalization;
using System.Text;
using Boxcars.Data.Maps;

namespace Boxcars.Services.Maps;

public sealed class RbpMapParserService : IMapParserService
{
    public async Task<MapLoadResult> ParseAsync(string fileName, Stream contentStream, CancellationToken cancellationToken)
    {
        if (!IsSupportedFile(fileName))
        {
            return new MapLoadResult
            {
                Succeeded = false,
                Errors = new[] { "Unsupported map file type. Please select a .rbp or .rb3 file." }
            };
        }

        if (contentStream is null)
        {
            return new MapLoadResult
            {
                Succeeded = false,
                Errors = new[] { "Map file stream is unavailable." }
            };
        }

        cancellationToken.ThrowIfCancellationRequested();

        string rawContent;
        await using (var memoryStream = new MemoryStream())
        {
            await contentStream.CopyToAsync(memoryStream, cancellationToken);
            rawContent = Encoding.UTF8.GetString(memoryStream.ToArray());
        }

        if (string.IsNullOrWhiteSpace(rawContent))
        {
            return new MapLoadResult
            {
                Succeeded = false,
                Errors = new[] { "Map file is empty." }
            };
        }

        return ParseText(rawContent);
    }

    private static bool IsSupportedFile(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return extension.Equals(".rbp", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".rb3", StringComparison.OrdinalIgnoreCase);
    }

    private static MapLoadResult ParseText(string rawContent)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var map = new MapDefinition();
        var headerValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var currentRouteRailroadIndex = 0;

        var regionIndexByCode = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var currentSection = string.Empty;

        var lines = rawContent.Replace("\r\n", "\n").Split('\n');

        foreach (var originalLine in lines)
        {
            var line = originalLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (TryGetSection(line, out var sectionName))
            {
                currentSection = sectionName;
                continue;
            }

            if (currentSection.Equals("rout", StringComparison.OrdinalIgnoreCase) && line.StartsWith('\''))
            {
                ParseRouteRailroadHeaderLine(line, map, ref currentRouteRailroadIndex);
                continue;
            }

            if (currentSection.StartsWith("re", StringComparison.OrdinalIgnoreCase) && line.StartsWith('\''))
            {
                var potentialDotLine = line.TrimStart('\'').TrimStart();
                if (potentialDotLine.StartsWith("s,", StringComparison.OrdinalIgnoreCase))
                {
                    ParseRegionDotLine(currentSection, potentialDotLine, map.TrainDots);
                }

                continue;
            }

            if (line.StartsWith('"') || line.StartsWith('\''))
            {
                continue;
            }

            if (line.Contains('=') && currentSection.Equals("header", StringComparison.OrdinalIgnoreCase))
            {
                var split = line.Split('=', 2);
                headerValues[split[0].Trim()] = split[1].Trim();
                continue;
            }

            switch (currentSection)
            {
                case "regn":
                    ParseRegionLine(line, map, regionIndexByCode);
                    break;
                case "city":
                    ParseCityLine(line, map);
                    break;
                case "rr":
                    ParseRailroadLine(line, map);
                    break;
                case "map":
                    ParseLineSegmentLine(line, map.MapLines);
                    break;
                case "sep":
                    ParseLineSegmentLine(line, map.Separators);
                    break;
                case "rout":
                    ParseRouteLine(line, map, currentRouteRailroadIndex);
                    break;
                case "label":
                    ParseLabelLine(line, map);
                    break;
                default:
                    if (currentSection.StartsWith("re", StringComparison.OrdinalIgnoreCase))
                    {
                        ParseRegionDotLine(currentSection, line, map.TrainDots);
                    }
                    break;
            }
        }

        ApplyHeader(map, headerValues, errors);
        ValidateParsedMap(map, errors, warnings);

        if (errors.Count > 0)
        {
            return new MapLoadResult
            {
                Succeeded = false,
                Errors = errors,
                Warnings = warnings
            };
        }

        return new MapLoadResult
        {
            Succeeded = true,
            Definition = map,
            Warnings = warnings
        };
    }

    private static bool TryGetSection(string line, out string sectionName)
    {
        sectionName = string.Empty;

        var trimmed = line.TrimStart('\'');
        if (!trimmed.StartsWith('[') || !trimmed.EndsWith(']'))
        {
            return false;
        }

        sectionName = trimmed[1..^1].Trim().ToLowerInvariant();
        return true;
    }

    private static void ParseRegionLine(string line, MapDefinition map, Dictionary<string, int> regionIndexByCode)
    {
        if (!line.Contains(',') || line.Contains('='))
        {
            return;
        }

        var tokens = ParseCsvLine(line);
        if (tokens.Count < 2)
        {
            return;
        }

        var name = tokens[0].Trim();
        var code = tokens[1].Trim();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        double? probability = null;
        if (tokens.Count > 2 && double.TryParse(tokens[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedProbability))
        {
            probability = parsedProbability;
        }

        map.Regions.Add(new RegionDefinition
        {
            Name = name,
            Code = code,
            Probability = probability
        });

        regionIndexByCode[code] = map.Regions.Count;
    }

    private static void ParseCityLine(string line, MapDefinition map)
    {
        if (!line.Contains(',') || int.TryParse(line, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            return;
        }

        var tokens = ParseCsvLine(line);
        if (tokens.Count < 5)
        {
            return;
        }

        var cityName = tokens[0].Trim();
        var regionCode = tokens[1].Trim();

        if (string.IsNullOrWhiteSpace(cityName) || string.IsNullOrWhiteSpace(regionCode))
        {
            return;
        }

        var probability = TryParseDouble(tokens[2]);
        var payoutIndex = TryParseInt(tokens[3]);
        var dotIndex = TryParseInt(tokens[4]);

        map.Cities.Add(new CityDefinition
        {
            Name = cityName,
            RegionCode = regionCode,
            Probability = probability,
            PayoutIndex = payoutIndex,
            MapDotIndex = dotIndex
        });
    }

    private static void ParseRailroadLine(string line, MapDefinition map)
    {
        if (!line.Contains(',') || int.TryParse(line, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            return;
        }

        var tokens = ParseCsvLine(line);
        if (tokens.Count == 0)
        {
            return;
        }

        var name = tokens[0].Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var shortName = tokens.Count > 2 ? tokens[2].Trim() : null;
        var colorIndex = tokens.Count > 3 ? TryParseInt(tokens[3]) : null;
        var blue = tokens.Count > 6 ? TryParseInt(tokens[6]) : null;
        var green = tokens.Count > 7 ? TryParseInt(tokens[7]) : null;
        var red = tokens.Count > 8 ? TryParseInt(tokens[8]) : null;

        map.Railroads.Add(new RailroadDefinition
        {
            Index = map.Railroads.Count + 1,
            Name = name,
            ShortName = string.IsNullOrWhiteSpace(shortName) ? null : shortName,
            ColorIndex = colorIndex,
            Red = red,
            Green = green,
            Blue = blue
        });
    }

    private static void ParseLabelLine(string line, MapDefinition map)
    {
        if (!line.Contains(',') || int.TryParse(line, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            return;
        }

        var tokens = ParseCsvLine(line);
        if (tokens.Count < 3)
        {
            return;
        }

        var text = tokens[0].Trim();
        var x = TryParseDouble(tokens[1]);
        var y = TryParseDouble(tokens[2]);

        if (string.IsNullOrWhiteSpace(text) || x is null || y is null)
        {
            return;
        }

        map.RegionLabels.Add(new RegionLabelDefinition
        {
            Text = text,
            X = x.Value,
            Y = y.Value
        });
    }

    private static void ParseRouteRailroadHeaderLine(string line, MapDefinition map, ref int currentRouteRailroadIndex)
    {
        var sectionRailroadText = line.TrimStart('\'').Trim();
        if (string.IsNullOrWhiteSpace(sectionRailroadText))
        {
            return;
        }

        var railroad = map.Railroads.FirstOrDefault(rr =>
            sectionRailroadText.Equals(rr.ShortName, StringComparison.OrdinalIgnoreCase)
            || sectionRailroadText.Equals(rr.Name, StringComparison.OrdinalIgnoreCase));

        if (railroad is not null)
        {
            currentRouteRailroadIndex = railroad.Index;
        }
    }

    private static void ParseRouteLine(string line, MapDefinition map, int railroadIndex)
    {
        if (railroadIndex <= 0)
        {
            return;
        }

        var tokens = ParseCsvLine(line)
            .Select(TryParseInt)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();

        if (tokens.Count < 2)
        {
            return;
        }

        var points = new List<RouteDotRef>();
        var currentRegion = 0;

        for (var tokenIndex = 0; tokenIndex < tokens.Count;)
        {
            if (tokenIndex + 1 < tokens.Count
                && ((tokens[tokenIndex] == 0 && tokens[tokenIndex + 1] == 0)
                    || (tokens[tokenIndex] == -1 && tokens[tokenIndex + 1] == -1)))
            {
                break;
            }

            var token = tokens[tokenIndex];
            if (token <= 0)
            {
                tokenIndex++;
                continue;
            }

            if (token >= 1 && token <= map.Regions.Count && tokenIndex + 1 < tokens.Count)
            {
                var dotToken = tokens[tokenIndex + 1];
                if (dotToken > 0)
                {
                    currentRegion = token;
                    var decodedDotToken = dotToken >= 1000 ? dotToken % 1000 : dotToken;
                    points.Add(new RouteDotRef(currentRegion, decodedDotToken));

                    tokenIndex += 2;
                    continue;
                }
            }

            if (currentRegion > 0)
            {
                var decodedToken = token >= 1000 ? token % 1000 : token;
                points.Add(new RouteDotRef(currentRegion, decodedToken));
            }

            tokenIndex++;
        }

        if (points.Count < 2)
        {
            return;
        }

        for (var i = 0; i < points.Count - 1; i++)
        {
            var start = points[i];
            var end = points[i + 1];

            if (start.RegionIndex <= 0 || start.DotIndex <= 0 || end.RegionIndex <= 0 || end.DotIndex <= 0)
            {
                continue;
            }

            map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition
            {
                RailroadIndex = railroadIndex,
                StartRegionIndex = start.RegionIndex,
                StartDotIndex = start.DotIndex,
                EndRegionIndex = end.RegionIndex,
                EndDotIndex = end.DotIndex
            });
        }
    }

    private readonly record struct RouteDotRef(int RegionIndex, int DotIndex);

    private static void ParseRegionDotLine(string section, string line, List<TrainDot> trainDots)
    {
        if (!line.StartsWith("s,", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var regionIndexText = section[2..];
        if (!int.TryParse(regionIndexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var regionIndex))
        {
            return;
        }

        var tokens = ParseCsvLine(line);
        if (tokens.Count < 4)
        {
            return;
        }

        var x = TryParseDouble(tokens[1]);
        var y = TryParseDouble(tokens[2]);
        var color = TryParseInt(tokens[3]);

        if (x is null || y is null)
        {
            return;
        }

        var nextDotIndex = trainDots.Count(dot => dot.RegionIndex == regionIndex) + 1;
        trainDots.Add(new TrainDot
        {
            Id = $"{regionIndex}-{nextDotIndex}",
            RegionIndex = regionIndex,
            DotIndex = nextDotIndex,
            X = x.Value,
            Y = y.Value,
            ColorIndex = color
        });
    }

    private static void ParseLineSegmentLine(string line, List<LineSegment> destination)
    {
        if (!line.StartsWith("l,", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var tokens = ParseCsvLine(line);
        if (tokens.Count < 6)
        {
            return;
        }

        var x1 = TryParseDouble(tokens[1]);
        var y1 = TryParseDouble(tokens[2]);
        var x2 = TryParseDouble(tokens[3]);
        var y2 = TryParseDouble(tokens[4]);
        var style = TryParseInt(tokens[5]);

        if (x1 is null || y1 is null || x2 is null || y2 is null)
        {
            return;
        }

        destination.Add(new LineSegment
        {
            X1 = x1.Value,
            Y1 = y1.Value,
            X2 = x2.Value,
            Y2 = y2.Value,
            StyleIndex = style
        });
    }

    private static void ApplyHeader(MapDefinition map, Dictionary<string, string> headerValues, List<string> errors)
    {
        map.Name = headerValues.TryGetValue("name", out var name) ? name : null;
        map.Version = headerValues.TryGetValue("version", out var version) ? version : null;
        map.BackgroundKey = headerValues.TryGetValue("sharebgnd", out var backgroundKey) ? backgroundKey : null;

        map.ScaleLeft = ParseRequiredHeaderDouble(headerValues, "scalel", errors);
        map.ScaleTop = ParseRequiredHeaderDouble(headerValues, "scalet", errors);
        map.ScaleWidth = ParseRequiredHeaderDouble(headerValues, "scalew", errors);
        map.ScaleHeight = ParseRequiredHeaderDouble(headerValues, "scaleh", errors);
    }

    private static double ParseRequiredHeaderDouble(Dictionary<string, string> headerValues, string key, List<string> errors)
    {
        if (!headerValues.TryGetValue(key, out var value) || !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            errors.Add($"Missing or invalid required header value '{key}'.");
            return 0;
        }

        return parsed;
    }

    private static void ValidateParsedMap(MapDefinition map, List<string> errors, List<string> warnings)
    {
        if (map.ScaleWidth <= 0 || map.ScaleHeight <= 0)
        {
            errors.Add("Map scale bounds are invalid. Expected positive scale width and height.");
        }

        if (map.Cities.Count == 0)
        {
            errors.Add("Map is missing required city data.");
        }

        if (map.TrainDots.Count == 0)
        {
            errors.Add("Map is missing required train-position dot data.");
        }

        if (string.IsNullOrWhiteSpace(map.BackgroundKey))
        {
            warnings.Add("Map does not define a background key; background resolution may require manual upload.");
        }
    }

    private static List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var character in line)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (character == ',' && !inQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        values.Add(current.ToString());
        return values;
    }

    private static int? TryParseInt(string token)
    {
        var cleaned = token.Split('\'', 2)[0].Trim();
        return int.TryParse(cleaned, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static double? TryParseDouble(string token)
    {
        var cleaned = token.Split('\'', 2)[0].Trim();
        return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }
}
