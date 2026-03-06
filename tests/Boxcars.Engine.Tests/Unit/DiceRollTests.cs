using Boxcars.Engine.Domain;
using Boxcars.Engine.Tests.Fixtures;
using Boxcars.Engine.Tests.TestDoubles;

namespace Boxcars.Engine.Tests.Unit;

/// <summary>
/// Tests for dice rolling behavior (T024).
/// </summary>
public class DiceRollTests
{
    [Fact]
    public void RollDice_InRollPhase_ReturnsDiceResult()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Roll);

        var route = engine.SuggestRoute();
        engine.SaveRoute(route);

        random.QueueDiceRoll(3, 4);
        var result = engine.RollDice();

        Assert.NotNull(result);
        Assert.Equal(7, result.Total);
        Assert.Equal(new[] { 3, 4 }, result.WhiteDice);
        Assert.Null(result.RedDie);
    }

    [Fact]
    public void RollDice_FreightDoubles6_BonusAvailable()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Roll);

        var route = engine.SuggestRoute();
        engine.SaveRoute(route);

        random.QueueDiceRoll(6, 6);
        engine.RollDice();

        Assert.True(engine.CurrentTurn.BonusRollAvailable);
    }

    [Fact]
    public void RollDice_FreightNonDoubles_NoBonusAvailable()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Roll);

        var route = engine.SuggestRoute();
        engine.SaveRoute(route);

        random.QueueDiceRoll(3, 4);
        engine.RollDice();

        Assert.False(engine.CurrentTurn.BonusRollAvailable);
    }

    [Fact]
    public void RollDice_FreightNon6Doubles_NoBonusAvailable()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Roll);

        var route = engine.SuggestRoute();
        engine.SaveRoute(route);

        random.QueueDiceRoll(3, 3); // Doubles but not 6s
        engine.RollDice();

        Assert.False(engine.CurrentTurn.BonusRollAvailable); // Freight only gets bonus on double-6
    }

    [Fact]
    public void RollDice_NotInRollPhase_ThrowsInvalidOperation()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();
        // Phase is DrawDestination, not Roll

        var ex = Assert.Throws<InvalidOperationException>(() => engine.RollDice());
        Assert.Contains("Not in Roll phase", ex.Message);
    }

    [Fact]
    public void RollDice_SetsCurrentTurnDiceResult()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Roll);

        var route = engine.SuggestRoute();
        engine.SaveRoute(route);

        random.QueueDiceRoll(2, 5);
        engine.RollDice();

        Assert.NotNull(engine.CurrentTurn.DiceResult);
        Assert.Equal(7, engine.CurrentTurn.DiceResult.Total);
    }

    [Fact]
    public void RollDice_SetsMovementRemaining()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Roll);

        var route = engine.SuggestRoute();
        engine.SaveRoute(route);

        random.QueueDiceRoll(4, 3);
        engine.RollDice();

        Assert.Equal(7, engine.CurrentTurn.MovementRemaining);
    }

    [Fact]
    public void RollDice_AdvancesToMovePhase()
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
    public void RollDice_Doubles_DetectedCorrectly()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Roll);

        var route = engine.SuggestRoute();
        engine.SaveRoute(route);

        random.QueueDiceRoll(4, 4);
        var result = engine.RollDice();

        Assert.True(result.IsDoubles);
    }

    [Fact]
    public void RollDice_NonDoubles_DetectedCorrectly()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Roll);

        var route = engine.SuggestRoute();
        engine.SaveRoute(route);

        random.QueueDiceRoll(3, 5);
        var result = engine.RollDice();

        Assert.False(result.IsDoubles);
    }
}
