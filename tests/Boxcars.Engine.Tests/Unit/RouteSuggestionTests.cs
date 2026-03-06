using Boxcars.Engine.Domain;
using Boxcars.Engine.Tests.Fixtures;
using Boxcars.Engine.Tests.TestDoubles;

namespace Boxcars.Engine.Tests.Unit;

/// <summary>
/// Tests for route suggestion and save route (T033, T034).
/// </summary>
public class RouteSuggestionTests
{
    [Fact]
    public void SuggestRoute_WithDestination_ReturnsRoute()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();

        // Draw destination
        random.QueueWeightedDraw(1); // SE region
        random.QueueWeightedDraw(0); // First city
        engine.DrawDestination();

        var route = engine.SuggestRoute();

        Assert.NotNull(route);
        Assert.True(route.NodeIds.Count > 0);
    }

    [Fact]
    public void SuggestRoute_NoDestination_ThrowsInvalidOperation()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        // No destination drawn yet, but we need to be able to call SuggestRoute
        // It requires a destination
        var ex = Assert.Throws<InvalidOperationException>(() => engine.SuggestRoute());
        Assert.Contains("No destination assigned", ex.Message);
    }

    [Fact]
    public void SuggestRoute_DoesNotMutateState()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();

        random.QueueWeightedDraw(1);
        random.QueueWeightedDraw(0);
        engine.DrawDestination();

        var phase = engine.CurrentTurn.Phase;
        var playerRoute = engine.CurrentTurn.ActivePlayer.ActiveRoute;

        engine.SuggestRoute();

        // State should be unchanged
        Assert.Equal(phase, engine.CurrentTurn.Phase);
        Assert.Equal(playerRoute, engine.CurrentTurn.ActivePlayer.ActiveRoute);
    }

    [Fact]
    public void SaveRoute_ValidRoute_SetsActiveRoute()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();

        random.QueueWeightedDraw(1);
        random.QueueWeightedDraw(0);
        engine.DrawDestination();

        var route = engine.SuggestRoute();
        engine.SaveRoute(route);

        Assert.Equal(route, engine.CurrentTurn.ActivePlayer.ActiveRoute);
    }

    [Fact]
    public void SaveRoute_NullRoute_ThrowsArgumentNull()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();

        random.QueueWeightedDraw(1);
        random.QueueWeightedDraw(0);
        engine.DrawDestination();

        Assert.Throws<ArgumentNullException>(() => engine.SaveRoute(null!));
    }

    [Fact]
    public void SaveRoute_NoDestination_ThrowsInvalidOperation()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        var dummyRoute = new Route(new[] { "0:0" }, Array.Empty<RouteSegment>(), 0);

        // No destination drawn — should fail
        var ex = Assert.Throws<InvalidOperationException>(() => engine.SaveRoute(dummyRoute));
        Assert.Contains("No destination assigned", ex.Message);
    }

    [Fact]
    public void SaveRoute_FiresPropertyChanged()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        var player = engine.Players[0];
        var changedProps = new List<string>();

        random.QueueWeightedDraw(1);
        random.QueueWeightedDraw(0);
        engine.DrawDestination();

        player.PropertyChanged += (s, e) => changedProps.Add(e.PropertyName!);

        var route = engine.SuggestRoute();
        engine.SaveRoute(route);

        Assert.Contains("ActiveRoute", changedProps);
    }

    [Fact]
    public void SaveRoute_ResetsRouteProgressIndex()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();

        random.QueueWeightedDraw(1);
        random.QueueWeightedDraw(0);
        engine.DrawDestination();

        var route = engine.SuggestRoute();
        engine.SaveRoute(route);

        Assert.Equal(0, engine.CurrentTurn.ActivePlayer.RouteProgressIndex);
    }
}
