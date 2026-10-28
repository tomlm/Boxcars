using Boxcars.Data.Maps;
using global::Boxcars.Services;

namespace Boxcars.Engine.Tests.Unit;

public class SharedRoutePreviewServiceTests
{
    private static readonly SharedRouteSegment LasVegasOut = new("7:27", "7:26", 24);
    private static readonly SharedRouteSegment NextDot = new("7:26", "7:25", 24);

    [Fact]
    public void Publish_NewPreview_IsSharedWithTheGame()
    {
        var service = new SharedRoutePreviewService();
        var changedGames = new List<string>();
        service.PreviewChanged += changedGames.Add;

        service.Publish("game-1", new SharedRoutePreview(1, 142, [LasVegasOut], [LasVegasOut, NextDot]));

        Assert.Equal(["game-1"], changedGames);
        Assert.Equal([LasVegasOut], service.Get("game-1")!.Selected);
        Assert.Null(service.Get("game-2"));
    }

    [Fact]
    public void Publish_SameRouteAgain_DoesNotNotify()
    {
        var service = new SharedRoutePreviewService();
        service.Publish("game-1", new SharedRoutePreview(1, 142, [LasVegasOut], [LasVegasOut, NextDot]));
        var notifications = 0;
        service.PreviewChanged += _ => notifications++;

        // A fresh copy of the same route, as each selection change rebuilds it.
        service.Publish("game-1", new SharedRoutePreview(1, 142, [new("7:27", "7:26", 24)], [LasVegasOut, NextDot]));
        Assert.Equal(0, notifications);

        // A hand-picked dot is news.
        service.Publish("game-1", new SharedRoutePreview(1, 142, [LasVegasOut, NextDot], [LasVegasOut, NextDot]));
        Assert.Equal(1, notifications);
    }
}
