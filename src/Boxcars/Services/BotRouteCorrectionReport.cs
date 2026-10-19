using System.Globalization;
using System.Text;
using Boxcars.Engine.Data.Maps;

namespace Boxcars.Services;

/// <summary>
/// The bug report filed when, in debug mode, the admin moves a bot along a different route than the one it chose:
/// why, the bot's route, and the admin's route instead.
/// </summary>
public static class BotRouteCorrectionReport
{
    /// <summary>The "what happened" and "expected" text for <see cref="BugReportService.SubmitAsync"/>.</summary>
    public static (string Issue, string Expected) Build(
        BotRouteReview review,
        MapDefinition? mapDefinition,
        IReadOnlyList<string> chosenSegmentKeys,
        int chosenFeeEstimate,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(review);
        ArgumentNullException.ThrowIfNull(chosenSegmentKeys);

        var start = review.RouteNodeIds.Count > 0 ? DescribeNode(mapDefinition, review.RouteNodeIds[0]) : "?";
        var destination = review.RouteNodeIds.Count > 0 ? DescribeNode(mapDefinition, review.RouteNodeIds[^1]) : "?";
        var fees = string.Join(", ", review.Fees.Select(fee => $"{FormatCurrency(fee.Amount)} to {fee.OwnerName} for {string.Join(", ", fee.RailroadNames)}"));

        var issue = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"Bot route corrected: {review.PlayerName} from {start} to {destination}")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"**Why the admin changed it:** {reason.Trim()}")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"**Bot's move on turn {review.TurnNumber}** ({review.MoveSteps} steps; pays {fees}):")
            .AppendLine(DescribeRoute(mapDefinition, review.MoveSegmentKeys))
            .AppendLine()
            .AppendLine("**Bot's full planned route:**")
            .AppendLine(DescribeRoute(mapDefinition, review.RouteSegmentKeys))
            .AppendLine()
            .AppendLine("<details><summary>Bot's route segments</summary>")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"`{string.Join(" ", review.RouteSegmentKeys)}`")
            .AppendLine()
            .AppendLine("</details>")
            .ToString();

        var expected = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"**Admin's move instead** ({chosenSegmentKeys.Count} steps; estimated fees {FormatCurrency(chosenFeeEstimate)}):")
            .AppendLine(DescribeRoute(mapDefinition, chosenSegmentKeys))
            .AppendLine()
            .AppendLine("<details><summary>Admin's route segments</summary>")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"`{string.Join(" ", chosenSegmentKeys)}`")
            .AppendLine()
            .AppendLine("</details>")
            .ToString();

        return (issue, expected);
    }

    /// <summary>
    /// A route as its railroad legs, such as "Denver —D&amp;RGW (2)→ Pueblo —AT&amp;SF (5)→ Tucumcari", from segment keys
    /// in the form <c>from|to|railroadIndex</c>.
    /// </summary>
    public static string DescribeRoute(MapDefinition? mapDefinition, IReadOnlyList<string> segmentKeys)
    {
        var segments = segmentKeys
            .Select(key => key.Split('|'))
            .Where(parts => parts.Length == 3 && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            .Select(parts => (From: parts[0], To: parts[1], RailroadIndex: int.Parse(parts[2], CultureInfo.InvariantCulture)))
            .ToList();
        if (segments.Count == 0)
        {
            return "_No route._";
        }

        var text = new StringBuilder(DescribeNode(mapDefinition, segments[0].From));
        var legStart = 0;
        for (var index = 1; index <= segments.Count; index++)
        {
            if (index < segments.Count && segments[index].RailroadIndex == segments[legStart].RailroadIndex)
            {
                continue;
            }

            text.Append(CultureInfo.InvariantCulture, $" —{RailroadName(mapDefinition, segments[legStart].RailroadIndex)} ({index - legStart})→ {DescribeNode(mapDefinition, segments[index - 1].To)}");
            legStart = index;
        }

        return text.ToString();
    }

    private static string DescribeNode(MapDefinition? mapDefinition, string nodeId)
    {
        // Node ids are "regionIndex:dotIndex", with regions numbered from 1.
        var parts = nodeId.Split(':');
        if (mapDefinition is not null
            && parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var regionIndex)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dotIndex)
            && regionIndex >= 1
            && regionIndex <= mapDefinition.Regions.Count)
        {
            var regionCode = mapDefinition.Regions[regionIndex - 1].Code;
            var city = mapDefinition.Cities.FirstOrDefault(candidate =>
                candidate.MapDotIndex == dotIndex
                && string.Equals(candidate.RegionCode, regionCode, StringComparison.OrdinalIgnoreCase));
            if (city is not null)
            {
                return city.Name;
            }
        }

        return $"dot {nodeId}";
    }

    private static string RailroadName(MapDefinition? mapDefinition, int railroadIndex)
    {
        var railroad = mapDefinition?.Railroads.FirstOrDefault(candidate => candidate.Index == railroadIndex);
        return railroad?.ShortName ?? railroad?.Name ?? railroadIndex.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatCurrency(int value)
    {
        return $"${Math.Max(0, value):N0}";
    }
}
