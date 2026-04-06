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

    [Fact]
    public void ExpressBonusMove_SnapshotAfterEndTurn_ReflectsPostBonusPosition()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        var player = engine.CurrentTurn.ActivePlayer;

        // Upgrade to Express
        player.LocomotiveType = LocomotiveType.Express;

        // Draw destination in a different region so we have a long route
        random.QueueWeightedDraw(1); // SE region
        random.QueueWeightedDraw(0); // first city (Miami)
        engine.DrawDestination();

        Assert.Equal(TurnPhase.Roll, engine.CurrentTurn.Phase);

        // Save route before rolling
        var route = engine.SuggestRoute();
        engine.SaveRoute(route);
        Assert.True(route.Segments.Count >= 4, $"Route needs enough segments for test, got {route.Segments.Count}");

        // Roll doubles (3+3=6) to trigger Express bonus
        random.QueueDiceRoll(3, 3);
        engine.RollDice();

        Assert.Equal(TurnPhase.Move, engine.CurrentTurn.Phase);
        Assert.True(engine.CurrentTurn.BonusRollAvailable, "Express doubles should make bonus available");

        var movementRemaining = engine.CurrentTurn.MovementRemaining;
        var stepsToMove = Math.Min(movementRemaining, route.Segments.Count);
        var preMovNodeId = player.CurrentNodeId;

        // Move the initial roll amount
        engine.MoveAlongRoute(stepsToMove);

        // Snapshot after initial move (this is what would be persisted as the Move event)
        var snapshotAfterInitialMove = engine.ToSnapshot();
        var initialMoveNodeId = snapshotAfterInitialMove.Players[player.Index].CurrentNodeId;

        Assert.NotEqual(preMovNodeId, initialMoveNodeId);

        // If we exhausted movement, bonus phase should now be active
        if (engine.CurrentTurn.Phase == TurnPhase.Move && engine.CurrentTurn.MovementRemaining > 0)
        {
            // Bonus move is active — the engine auto-prepared it via StartBonusMove
            var bonusMovement = engine.CurrentTurn.MovementRemaining;

            // Need a route for bonus movement
            var bonusRoute = engine.SuggestRoute();
            engine.SaveRoute(bonusRoute);

            var bonusSteps = Math.Min(bonusMovement, bonusRoute.Segments.Count);
            if (bonusSteps > 0)
            {
                engine.MoveAlongRoute(bonusSteps);
            }

            // Snapshot after bonus move
            var snapshotAfterBonusMove = engine.ToSnapshot();
            var bonusMoveNodeId = snapshotAfterBonusMove.Players[player.Index].CurrentNodeId;

            // Bonus should have moved the player further
            Assert.NotEqual(initialMoveNodeId, bonusMoveNodeId);

            // Now resolve fees and end turn
            if (engine.CurrentTurn.Phase == TurnPhase.EndTurn)
            {
                engine.EndTurn();
            }
            else if (engine.CurrentTurn.Phase == TurnPhase.UseFees)
            {
                // Fees already resolved automatically, just need to wait for EndTurn
                Assert.Equal(TurnPhase.EndTurn, engine.CurrentTurn.Phase);
                engine.EndTurn();
            }

            // Final snapshot (this is what the EndTurn event would persist)
            var snapshotAfterEndTurn = engine.ToSnapshot();
            var endTurnNodeId = snapshotAfterEndTurn.Players[player.Index].CurrentNodeId;

            // THE KEY ASSERTION: EndTurn snapshot must have post-bonus position, not pre-bonus
            Assert.Equal(bonusMoveNodeId, endTurnNodeId);
            Assert.NotEqual(initialMoveNodeId, endTurnNodeId);
        }
    }
}
