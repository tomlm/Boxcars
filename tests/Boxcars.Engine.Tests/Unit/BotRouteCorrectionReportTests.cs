using Boxcars.Engine.Data.Maps;
using Boxcars.Services;

namespace Boxcars.Engine.Tests.Unit;

public class BotRouteCorrectionReportTests
{
    // Denver (5:74) to Pueblo (5:94) on CB&Q through a plain dot, then west on D&RGW.
    private static readonly string[] BotRoute = ["5:74|5:84|11", "5:84|5:94|11", "5:94|5:85|22"];
    private static readonly string[] AdminRoute = ["5:74|5:84|22", "5:84|5:94|22"];

    [Fact]
    public void DescribeRoute_GroupsTrackIntoRailroadLegsBetweenNamedStops()
    {
        var description = BotRouteCorrectionReport.DescribeRoute(LoadMap(), BotRoute);

        Assert.Equal("Denver —CB&Q (2)→ Pueblo —D&RGW (1)→ dot 5:85", description);
    }

    [Fact]
    public void Build_IncludesTheReasonBothRoutesAndWhatTheBotWouldPay()
    {
        var review = new BotRouteReview
        {
            GameId = "game-1",
            TurnNumber = 12,
            PlayerIndex = 1,
            PlayerName = "El Cheapo",
            RouteNodeIds = ["5:74", "5:84", "5:94", "5:85"],
            RouteSegmentKeys = BotRoute,
            MoveSteps = 2,
            Fees = [new BotRouteReviewFee("thermous", ["CB&Q"], 5_000)]
        };

        var (issue, expected) = BotRouteCorrectionReport.Build(review, LoadMap(), AdminRoute, 1_000, "  D&RGW is friendly and just as short.  ");

        Assert.StartsWith("Bot route corrected: El Cheapo from Denver to dot 5:85", issue, StringComparison.Ordinal);
        Assert.Contains("**Why the admin changed it:** D&RGW is friendly and just as short.", issue, StringComparison.Ordinal);
        Assert.Contains("pays $5,000 to thermous for CB&Q", issue, StringComparison.Ordinal);
        // The move this turn is the first two steps; the full plan goes on to the third.
        Assert.Contains("Denver —CB&Q (2)→ Pueblo" + Environment.NewLine, issue, StringComparison.Ordinal);
        Assert.Contains("Denver —CB&Q (2)→ Pueblo —D&RGW (1)→ dot 5:85", issue, StringComparison.Ordinal);
        Assert.Contains("Denver —D&RGW (2)→ Pueblo", expected, StringComparison.Ordinal);
        Assert.Contains("estimated fees $1,000", expected, StringComparison.Ordinal);
        Assert.Contains("`5:74|5:84|22 5:84|5:94|22`", expected, StringComparison.Ordinal);
    }

    private static MapDefinition LoadMap([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
    {
        // tests/Boxcars.Engine.Tests/Unit/<this file> -> src/Boxcars/U21MAP.RB3
        var mapPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFilePath)!, "..", "..", "..", "src", "Boxcars", "U21MAP.RB3"));
        return MapDefinition.Parse(File.ReadAllText(mapPath)).Definition!;
    }
}
