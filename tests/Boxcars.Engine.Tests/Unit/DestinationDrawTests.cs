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

    [Fact]
    public void DrawDestination_SameRegion_TransitionsToRegionChoiceWithoutAssigningDestination()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();

        random.QueueWeightedDraw(0);
        random.QueueWeightedDraw(1);

        var drawnCity = engine.DrawDestination();

        Assert.Equal("Boston", drawnCity.Name);
        Assert.Equal(TurnPhase.RegionChoice, engine.CurrentTurn.Phase);
        Assert.Null(engine.CurrentTurn.ActivePlayer.Destination);
        Assert.NotNull(engine.CurrentTurn.PendingRegionChoice);
        Assert.Equal("NE", engine.CurrentTurn.PendingRegionChoice!.CurrentRegionCode);
        Assert.Contains("NE", engine.CurrentTurn.PendingRegionChoice.EligibleRegionCodes, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("SE", engine.CurrentTurn.PendingRegionChoice.EligibleRegionCodes, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ChooseDestinationRegion_EligibleRegion_AssignsWeightedDestinationFromSelectedRegion()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();

        random.QueueWeightedDraw(0);
        random.QueueWeightedDraw(1);
        engine.DrawDestination();

        random.QueueWeightedDraw(1);
        var city = engine.ChooseDestinationRegion("SE");

        Assert.Equal(TurnPhase.Roll, engine.CurrentTurn.Phase);
        Assert.Null(engine.CurrentTurn.PendingRegionChoice);
        Assert.Equal("Atlanta", city.Name);
        Assert.Equal("SE", city.RegionCode);
        Assert.NotNull(engine.CurrentTurn.ActivePlayer.Destination);
        Assert.Equal("Atlanta", engine.CurrentTurn.ActivePlayer.Destination!.Name);
    }

    [Fact]
    public void ChooseDestinationRegion_IneligibleRegion_ThrowsAndKeepsPendingChoice()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();

        random.QueueWeightedDraw(0);
        random.QueueWeightedDraw(1);
        engine.DrawDestination();

        var exception = Assert.Throws<InvalidOperationException>(() => engine.ChooseDestinationRegion("MW"));

        Assert.Contains("not an eligible replacement region", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TurnPhase.RegionChoice, engine.CurrentTurn.Phase);
        Assert.NotNull(engine.CurrentTurn.PendingRegionChoice);
    }

    [Fact]
    public void ChooseDestinationRegion_CurrentRegionSameCity_EndsTurnWithoutAssigningDestination()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();

        random.QueueWeightedDraw(0);
        random.QueueWeightedDraw(1);
        engine.DrawDestination();

        random.QueueWeightedDraw(0);
        var city = engine.ChooseDestinationRegion("NE");

        Assert.Equal("New York", city.Name);
        Assert.Equal(TurnPhase.EndTurn, engine.CurrentTurn.Phase);
        Assert.Null(engine.CurrentTurn.PendingRegionChoice);
        Assert.Null(engine.CurrentTurn.ActivePlayer.Destination);
        Assert.Null(engine.CurrentTurn.ActivePlayer.TripOriginCity);
    }
}
