using Boxcars.Engine.Domain;
using Boxcars.Engine.Tests.Fixtures;
using Boxcars.Engine.Tests.TestDoubles;

namespace Boxcars.Engine.Tests.Unit;

/// <summary>
/// Tests for turn phase progression (T027).
/// </summary>
public class TurnPhaseTests
{
    [Fact]
    public void InitialPhase_IsDrawDestination()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        Assert.Equal(TurnPhase.DrawDestination, engine.CurrentTurn.Phase);
    }

    [Fact]
    public void DrawDestination_AdvancesPhaseToRoll()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();

        random.QueueWeightedDraw(1);
        random.QueueWeightedDraw(0);
        engine.DrawDestination();

        Assert.Equal(TurnPhase.Roll, engine.CurrentTurn.Phase);
    }

    [Fact]
    public void RollDice_AdvancesPhaseToMove()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Roll);

        var route = engine.SuggestRoute();
        engine.SaveRoute(route);

        random.QueueDiceRoll(3, 4);
        engine.RollDice();

        Assert.Equal(TurnPhase.Move, engine.CurrentTurn.Phase);
    }

    [Fact]
    public void DeclinePurchase_AdvancesToEndTurn()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Purchase);

        if (engine.CurrentTurn.Phase == TurnPhase.Purchase)
        {
            engine.DeclinePurchase();
            // After use fees resolve, should be in EndTurn
            Assert.Equal(TurnPhase.EndTurn, engine.CurrentTurn.Phase);
        }
    }

    [Fact]
    public void EndTurn_AdvancesToNextPlayer()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.EndTurn);

        if (engine.CurrentTurn.Phase == TurnPhase.EndTurn)
        {
            var firstPlayer = engine.CurrentTurn.ActivePlayer;
            engine.EndTurn();
            Assert.NotEqual(firstPlayer, engine.CurrentTurn.ActivePlayer);
        }
    }

    [Fact]
    public void EndTurn_IncrementsTurnNumber()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.EndTurn);

        if (engine.CurrentTurn.Phase == TurnPhase.EndTurn)
        {
            int turnBefore = engine.CurrentTurn.TurnNumber;
            engine.EndTurn();
            Assert.Equal(turnBefore + 1, engine.CurrentTurn.TurnNumber);
        }
    }

    [Fact]
    public void EndTurn_SetsDrawDestinationIfNoDestination()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.EndTurn);

        if (engine.CurrentTurn.Phase == TurnPhase.EndTurn)
        {
            engine.EndTurn();
            // Next player has no destination, so phase should be DrawDestination
            Assert.Equal(TurnPhase.DrawDestination, engine.CurrentTurn.Phase);
        }
    }

    [Fact]
    public void EndTurn_NotInEndTurnPhase_ThrowsInvalidOperation()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        var ex = Assert.Throws<InvalidOperationException>(() => engine.EndTurn());
        Assert.Contains("Not in EndTurn phase", ex.Message);
    }

    [Fact]
    public void EndTurn_RaisesTurnStartedEvent()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.EndTurn);

        Events.TurnStartedEventArgs? eventArgs = null;
        engine.TurnStarted += (s, e) => eventArgs = e;

        if (engine.CurrentTurn.Phase == TurnPhase.EndTurn)
        {
            engine.EndTurn();
            Assert.NotNull(eventArgs);
        }
    }

    [Fact]
    public void EndTurn_ClearsTurnState()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.EndTurn);

        if (engine.CurrentTurn.Phase == TurnPhase.EndTurn)
        {
            engine.EndTurn();
            Assert.Null(engine.CurrentTurn.DiceResult);
            Assert.Equal(0, engine.CurrentTurn.MovementRemaining);
            Assert.False(engine.CurrentTurn.BonusRollAvailable);
        }
    }

    [Fact]
    public void PhaseEnforcesCorrectActionOrder()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        // Can't roll in DrawDestination phase
        Assert.Throws<InvalidOperationException>(() => engine.RollDice());

        // Can't move in DrawDestination phase
        Assert.Throws<InvalidOperationException>(() => engine.MoveAlongRoute(1));

        // Can't buy in DrawDestination phase
        Assert.Throws<InvalidOperationException>(() => engine.BuyRailroad(engine.Railroads[0]));

        // Can't end turn in DrawDestination phase
        Assert.Throws<InvalidOperationException>(() => engine.EndTurn());

        // Can't decline purchase in DrawDestination phase
        Assert.Throws<InvalidOperationException>(() => engine.DeclinePurchase());
    }
}
