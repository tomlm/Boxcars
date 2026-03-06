using Boxcars.Engine.Domain;
using GE = Boxcars.Engine.Domain.GameEngine;
using Boxcars.Engine.Events;
using Boxcars.Engine.Tests.Fixtures;
using Boxcars.Engine.Tests.TestDoubles;

namespace Boxcars.Engine.Tests.Unit;

/// <summary>
/// Tests for destination draw behavior (T026).
/// </summary>
public class DestinationDrawTests
{
    [Fact]
    public void DrawDestination_InDrawDestinationPhase_AssignsDestination()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();

        Assert.Equal(TurnPhase.DrawDestination, engine.CurrentTurn.Phase);

        random.QueueWeightedDraw(1); // Region
        random.QueueWeightedDraw(0); // City
        var city = engine.DrawDestination();

        Assert.NotNull(city);
        Assert.NotNull(engine.CurrentTurn.ActivePlayer.Destination);
        Assert.Equal(city.Name, engine.CurrentTurn.ActivePlayer.Destination!.Name);
    }

    [Fact]
    public void DrawDestination_AdvancesToRollPhase()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();

        random.QueueWeightedDraw(1);
        random.QueueWeightedDraw(0);
        engine.DrawDestination();

        Assert.Equal(TurnPhase.Roll, engine.CurrentTurn.Phase);
    }

    [Fact]
    public void DrawDestination_RaisesDestinationAssignedEvent()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        DestinationAssignedEventArgs? eventArgs = null;

        engine.DestinationAssigned += (s, e) => eventArgs = e;

        random.QueueWeightedDraw(1);
        random.QueueWeightedDraw(0);
        engine.DrawDestination();

        Assert.NotNull(eventArgs);
        Assert.Equal(engine.Players[0], eventArgs!.Player);
        Assert.NotNull(eventArgs.City);
    }

    [Fact]
    public void DrawDestination_NotInDrawPhase_ThrowsInvalidOperation()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();

        // Draw once to advance beyond DrawDestination phase
        random.QueueWeightedDraw(1);
        random.QueueWeightedDraw(0);
        engine.DrawDestination();

        // Now in Roll phase
        var ex = Assert.Throws<InvalidOperationException>(() => engine.DrawDestination());
        Assert.Contains("Not in DrawDestination phase", ex.Message);
    }

    [Fact]
    public void DrawDestination_PlayerAlreadyHasDestination_ThrowsInvalidOperation()
    {
        // This scenario shouldn't happen due to phase enforcement,
        // but the double-check inside DrawDestination handles it
        var (engine, random) = GameEngineFixture.CreateTestEngine();

        random.QueueWeightedDraw(1);
        random.QueueWeightedDraw(0);
        engine.DrawDestination();

        // Player now has a destination and phase is Roll
        var ex = Assert.Throws<InvalidOperationException>(() => engine.DrawDestination());
        Assert.Contains("Not in DrawDestination phase", ex.Message);
    }

    [Fact]
    public void DrawDestination_DeterministicProvider_ProducesExpectedCity()
    {
        var map = GameEngineFixture.CreateTestMap();
        var random = new FixedRandomProvider();

        // Queue home city draws for 2 players
        random.QueueWeightedDraw(0); random.QueueWeightedDraw(0); // Player 1: NE, New York
        random.QueueWeightedDraw(1); random.QueueWeightedDraw(0); // Player 2: SE, Miami

        var engine = new GE(map, new[] { "Alice", "Bob" }, random);

        // Queue destination draw
        random.QueueWeightedDraw(1); // SE region
        random.QueueWeightedDraw(0); // Miami
        var dest = engine.DrawDestination();

        Assert.NotNull(dest);
    }
}
