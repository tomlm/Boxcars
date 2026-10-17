using Boxcars.Engine.Persistence;
using Boxcars.Services;

namespace Boxcars.Engine.Tests.Unit;

public class AllRailroadsSoldNoticeTests
{
    [Fact]
    public void BuildAllRailroadsSoldNotice_LastRailroadJustSold_GivesTheNewUnfriendlyFee()
    {
        var notice = GameBoardStateMapper.BuildAllRailroadsSoldNotice(
            new GameState { AllRailroadsSold = false },
            new GameState { AllRailroadsSold = true },
            GameSettingsTestData.Create(unfriendlyFee2: 12_000));

        Assert.Equal("All railroads are sold, unfriendly railroad travel is now $12,000", notice);
    }

    [Theory]
    [InlineData(false, false)] // still railroads for sale
    [InlineData(true, true)]   // already sold before this update
    public void BuildAllRailroadsSoldNotice_NotJustSold_GivesNothing(bool soldBefore, bool soldAfter)
    {
        var notice = GameBoardStateMapper.BuildAllRailroadsSoldNotice(
            new GameState { AllRailroadsSold = soldBefore },
            new GameState { AllRailroadsSold = soldAfter },
            GameSettingsTestData.Create());

        Assert.Null(notice);
    }

    [Fact]
    public void BuildAllRailroadsSoldNotice_FirstStateLoaded_GivesNothing()
    {
        // Opening a game where everything is already sold isn't news.
        var notice = GameBoardStateMapper.BuildAllRailroadsSoldNotice(null, new GameState { AllRailroadsSold = true }, GameSettingsTestData.Create());

        Assert.Null(notice);
    }
}
