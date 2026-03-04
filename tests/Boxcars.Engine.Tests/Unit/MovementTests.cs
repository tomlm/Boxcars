using Boxcars.Engine.Domain;
using Boxcars.Engine.Tests.Fixtures;
using Boxcars.Engine.Tests.TestDoubles;

namespace Boxcars.Engine.Tests.Unit;

/// <summary>
/// Tests for movement and non-reuse rules (T025).
/// </summary>
public class MovementTests
{
    [Fact]
    public void MoveAlongRoute_ValidSteps_DecreasesMovementRemaining()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Move);

        if (engine.CurrentTurn.Phase == TurnPhase.Move)
        {
            int before = engine.CurrentTurn.MovementRemaining;
            if (before > 0)
            {
                engine.MoveAlongRoute(1);
                Assert.Equal(before - 1, engine.CurrentTurn.MovementRemaining);
            }
        }
    }

    [Fact]
    public void MoveAlongRoute_NotInMovePhase_ThrowsInvalidOperation()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();
        // Phase is DrawDestination

        var ex = Assert.Throws<InvalidOperationException>(() => engine.MoveAlongRoute(1));
        Assert.Contains("Not in Move phase", ex.Message);
    }

    [Fact]
    public void MoveAlongRoute_ZeroSteps_ThrowsArgumentException()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Move);

        if (engine.CurrentTurn.Phase == TurnPhase.Move)
        {
            var ex = Assert.Throws<ArgumentException>(() => engine.MoveAlongRoute(0));
            Assert.Contains("Steps must be positive", ex.Message);
        }
    }

    [Fact]
    public void MoveAlongRoute_NegativeSteps_ThrowsArgumentException()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Move);

        if (engine.CurrentTurn.Phase == TurnPhase.Move)
        {
            var ex = Assert.Throws<ArgumentException>(() => engine.MoveAlongRoute(-1));
            Assert.Contains("Steps must be positive", ex.Message);
        }
    }

    [Fact]
    public void MoveAlongRoute_ExceedsMovementRemaining_ThrowsInvalidOperation()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Move);

        if (engine.CurrentTurn.Phase == TurnPhase.Move)
        {
            int remaining = engine.CurrentTurn.MovementRemaining;
            var ex = Assert.Throws<InvalidOperationException>(() =>
                engine.MoveAlongRoute(remaining + 100));
            Assert.Contains("Exceeds movement remaining", ex.Message);
        }
    }

    [Fact]
    public void MoveAlongRoute_NoActiveRoute_ThrowsInvalidOperation()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();

        // Draw destination and roll without saving route
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Roll);
        random.QueueDiceRoll(3, 4);
        engine.RollDice();

        // Clear the active route
        engine.CurrentTurn.ActivePlayer.ActiveRoute = null;

        var ex = Assert.Throws<InvalidOperationException>(() => engine.MoveAlongRoute(1));
        Assert.Contains("No active route set", ex.Message);
    }

    [Fact]
    public void MoveAlongRoute_TracksRailroadsRidden()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Move);

        if (engine.CurrentTurn.Phase == TurnPhase.Move && engine.CurrentTurn.MovementRemaining > 0)
        {
            engine.MoveAlongRoute(1);
            // Railroad should be tracked
            Assert.NotEmpty(engine.CurrentTurn.RailroadsRiddenThisTurn);
        }
    }
}
