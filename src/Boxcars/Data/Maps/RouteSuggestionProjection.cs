namespace Boxcars.Data.Maps;

public sealed class RouteSuggestionSegmentProjection
{
    public required string FromNodeId { get; init; }
    public required string ToNodeId { get; init; }
    public required int RailroadIndex { get; init; }
    public required RouteSuggestionHighlightType HighlightType { get; init; }
}

public static class RouteSuggestionProjection
{
    public static IReadOnlyList<RouteSuggestionSegmentProjection> BuildSegmentProjections(
        RouteSuggestionResult suggestion,
        int solidSegmentCount)
    {
        if (suggestion.Status != RouteSuggestionStatus.Success || suggestion.Segments.Count == 0)
        {
            return [];
        }

        var boundedSolidSegmentCount = Math.Max(0, solidSegmentCount);

        return suggestion.Segments
            .Select((segment, index) => new RouteSuggestionSegmentProjection
            {
                FromNodeId = segment.FromNodeId,
                ToNodeId = segment.ToNodeId,
                RailroadIndex = segment.RailroadIndex,
                HighlightType = index >= boundedSolidSegmentCount
                    ? RouteSuggestionHighlightType.Dashed
                    : RouteSuggestionHighlightType.Solid
            })
            .ToList();
    }

    public static RouteSuggestionHighlightType ClassifyNode(int nodeIndex, int solidSegmentCount)
    {
        var bounded = Math.Max(0, solidSegmentCount);
        if (nodeIndex > bounded)
        {
            return RouteSuggestionHighlightType.Dashed;
        }

        if (nodeIndex == bounded && bounded > 0)
        {
            return RouteSuggestionHighlightType.Endpoint;
        }

        return RouteSuggestionHighlightType.Solid;
    }
}