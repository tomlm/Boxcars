using Boxcars.Data.Maps;

namespace Boxcars.Engine.Tests.Unit;

public class RouteSuggestionProjectionTests
{
    [Fact]
    public void BuildSegmentProjections_MarksSegmentsBeyondSolidCountAsDashed()
    {
        var suggestion = CreateSuggestion();

        var projections = RouteSuggestionProjection.BuildSegmentProjections(suggestion, solidSegmentCount: 2);

        Assert.Collection(
            projections,
            projection => Assert.Equal(RouteSuggestionHighlightType.Solid, projection.HighlightType),
            projection => Assert.Equal(RouteSuggestionHighlightType.Solid, projection.HighlightType),
            projection => Assert.Equal(RouteSuggestionHighlightType.Dashed, projection.HighlightType),
            projection => Assert.Equal(RouteSuggestionHighlightType.Dashed, projection.HighlightType));
    }

    [Theory]
    [InlineData(1, 2, RouteSuggestionHighlightType.Solid)]
    [InlineData(2, 2, RouteSuggestionHighlightType.Endpoint)]
    [InlineData(3, 2, RouteSuggestionHighlightType.Dashed)]
    [InlineData(1, -1, RouteSuggestionHighlightType.Dashed)]
    public void ClassifyNode_UsesSegmentThreshold(int nodeIndex, int solidSegmentCount, RouteSuggestionHighlightType expected)
    {
        var result = RouteSuggestionProjection.ClassifyNode(nodeIndex, solidSegmentCount);

        Assert.Equal(expected, result);
    }

    private static RouteSuggestionResult CreateSuggestion()
    {
        return new RouteSuggestionResult
        {
            Status = RouteSuggestionStatus.Success,
            StartNodeId = "A",
            DestinationNodeId = "E",
            NodeIds = ["A", "B", "C", "D", "E"],
            Segments =
            [
                new RouteSuggestionSegment
                {
                    FromNodeId = "A",
                    ToNodeId = "B",
                    RailroadIndex = 1,
                    OwnershipCategory = RailroadOwnershipCategory.Public,
                    Turns = 1,
                    CostPerTurn = 1000,
                    TotalCost = 1000
                },
                new RouteSuggestionSegment
                {
                    FromNodeId = "B",
                    ToNodeId = "C",
                    RailroadIndex = 1,
                    OwnershipCategory = RailroadOwnershipCategory.Public,
                    Turns = 0,
                    CostPerTurn = 1000,
                    TotalCost = 0
                },
                new RouteSuggestionSegment
                {
                    FromNodeId = "C",
                    ToNodeId = "D",
                    RailroadIndex = 2,
                    OwnershipCategory = RailroadOwnershipCategory.Unfriendly,
                    Turns = 2,
                    CostPerTurn = 5000,
                    TotalCost = 10000
                },
                new RouteSuggestionSegment
                {
                    FromNodeId = "D",
                    ToNodeId = "E",
                    RailroadIndex = 2,
                    OwnershipCategory = RailroadOwnershipCategory.Unfriendly,
                    Turns = 0,
                    CostPerTurn = 5000,
                    TotalCost = 0
                }
            ],
            TotalTurns = 2,
            TotalCost = 6000
        };
    }
}