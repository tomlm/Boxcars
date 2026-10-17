using Boxcars.Engine.Persistence;
using global::Boxcars.Services;

namespace Boxcars.Engine.Tests.Unit;

public class BugReportServiceTests
{
    private static readonly DateTimeOffset ReportedUtc = new(2026, 10, 16, 23, 30, 0, TimeSpan.Zero);

    [Fact]
    public void BuildIssue_UsesTheTemplateHeadingsAndTitlesTheIssueFromTheFirstLine()
    {
        var (title, body) = BugReportService.BuildIssue(
            "Bonus out stayed on red\nIt cost me $10k next turn.",
            "Get onto B&O for $1k",
            "thermous",
            "game/game-1",
            "game-1",
            CreateState(),
            ReportedUtc);

        Assert.Equal("Bonus out stayed on red", title);
        Assert.True(body.IndexOf("# Issue", StringComparison.Ordinal) < body.IndexOf("# Expected", StringComparison.Ordinal));
        Assert.True(body.IndexOf("# Expected", StringComparison.Ordinal) < body.IndexOf("# Game State", StringComparison.Ordinal));
        Assert.Contains("It cost me $10k next turn.", body, StringComparison.Ordinal);
        Assert.Contains("Get onto B&O for $1k", body, StringComparison.Ordinal);
        Assert.Contains("Reported by thermous on `/game/game-1` at 2026-10-16 23:30:00 UTC.", body, StringComparison.Ordinal);
        Assert.Contains("```json", body, StringComparison.Ordinal);
        Assert.Contains("\"CurrentCityName\": \"Buffalo\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildIssue_LeavesEmailAddressesOutOfTheGameState()
    {
        var (_, body) = BugReportService.BuildIssue("Bug", "Fix", "thermous", "game/game-1", "game-1", CreateState(), ReportedUtc);

        Assert.DoesNotContain("thermous@example.com", body, StringComparison.Ordinal);
        Assert.Contains("\"ControllerUserId\": \"[removed]\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildIssue_OutsideAGame_SaysSo()
    {
        var (_, body) = BugReportService.BuildIssue("Dashboard bug", string.Empty, "thermous", "dashboard", null, null, ReportedUtc);

        Assert.Contains("Not in a game.", body, StringComparison.Ordinal);
        Assert.Contains("_Not given._", body, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildIssue_StateTooLargeForAnIssue_PointsToTheSavedEventsInstead()
    {
        var state = CreateState();
        state.Players[0].UsedSegments = Enumerable.Range(0, 5_000).Select(index => $"1:{index}-1:{index + 1}:5").ToList();

        var (_, body) = BugReportService.BuildIssue("Bug", "Fix", "thermous", "game/game-1", "game-1", state, ReportedUtc);

        Assert.True(body.Length <= GitHubIssueClient.MaximumBodyLength);
        Assert.Contains("too large to attach", body, StringComparison.Ordinal);
        Assert.DoesNotContain("```json", body, StringComparison.Ordinal);
    }

    private static GameState CreateState()
    {
        return new GameState
        {
            Players =
            [
                new PlayerState
                {
                    Name = "thermous",
                    CurrentCityName = "Buffalo",
                    Control = new PlayerControlState { ControllerMode = "Human", ControllerUserId = "thermous@example.com" }
                }
            ]
        };
    }
}
