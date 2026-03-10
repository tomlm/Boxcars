using Boxcars.Engine.Domain;
using Boxcars.Engine.Events;
using Boxcars.Engine.Tests.Fixtures;
using Boxcars.Engine.Tests.TestDoubles;

namespace Boxcars.Engine.Tests.Unit;

/// <summary>
/// Tests for locomotive upgrade paths and costs (T039).
/// </summary>
public class LocomotiveUpgradeTests
{
    private const int DefaultSuperchiefPrice = 40_000;

    [Fact]
    public void UpgradeLocomotive_FreightToExpress_Costs4000()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Purchase);

        if (engine.CurrentTurn.Phase == TurnPhase.Purchase)
        {
            var player = engine.CurrentTurn.ActivePlayer;
            // Clear railroads ridden to avoid use fees affecting cash calculation
            engine.CurrentTurn.RailroadsRiddenThisTurn.Clear();
            int cashBefore = player.Cash;
            Assert.Equal(LocomotiveType.Freight, player.LocomotiveType);

            engine.UpgradeLocomotive(LocomotiveType.Express);

            Assert.Equal(LocomotiveType.Express, player.LocomotiveType);
            Assert.Equal(cashBefore - 4000, player.Cash);
        }
    }

    [Fact]
    public void UpgradeLocomotive_FreightToSuperchief_CostsConfiguredPrice()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Purchase);

        if (engine.CurrentTurn.Phase == TurnPhase.Purchase)
        {
            var player = engine.CurrentTurn.ActivePlayer;
            player.Cash = 50_000;
            engine.CurrentTurn.RailroadsRiddenThisTurn.Clear();
            int cashBefore = player.Cash;

            engine.UpgradeLocomotive(LocomotiveType.Superchief);

            Assert.Equal(LocomotiveType.Superchief, player.LocomotiveType);
            Assert.Equal(cashBefore - DefaultSuperchiefPrice, player.Cash);
        }
    }

    [Fact]
    public void UpgradeLocomotive_ExpressToSuperchief_CostsConfiguredPrice()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Purchase);

        if (engine.CurrentTurn.Phase == TurnPhase.Purchase)
        {
            var player = engine.CurrentTurn.ActivePlayer;
            player.LocomotiveType = LocomotiveType.Express;
            player.Cash = 50_000;
            engine.CurrentTurn.RailroadsRiddenThisTurn.Clear();
            int cashBefore = player.Cash;

            engine.UpgradeLocomotive(LocomotiveType.Superchief);

            Assert.Equal(LocomotiveType.Superchief, player.LocomotiveType);
            Assert.Equal(cashBefore - DefaultSuperchiefPrice, player.Cash);
        }
    }

    [Fact]
    public void UpgradeLocomotive_CannotDowngrade_ThrowsInvalidOperation()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Purchase);

        if (engine.CurrentTurn.Phase == TurnPhase.Purchase)
        {
            var player = engine.CurrentTurn.ActivePlayer;
            player.LocomotiveType = LocomotiveType.Express;

            var ex = Assert.Throws<InvalidOperationException>(() =>
                engine.UpgradeLocomotive(LocomotiveType.Freight));
            Assert.Contains("Invalid upgrade path", ex.Message);
        }
    }

    [Fact]
    public void UpgradeLocomotive_SameType_ThrowsInvalidOperation()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Purchase);

        if (engine.CurrentTurn.Phase == TurnPhase.Purchase)
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                engine.UpgradeLocomotive(LocomotiveType.Freight));
            Assert.Contains("Invalid upgrade path", ex.Message);
        }
    }

    [Fact]
    public void UpgradeLocomotive_InsufficientFunds_ThrowsInvalidOperation()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Purchase);

        if (engine.CurrentTurn.Phase == TurnPhase.Purchase)
        {
            engine.CurrentTurn.ActivePlayer.Cash = 100;

            var ex = Assert.Throws<InvalidOperationException>(() =>
                engine.UpgradeLocomotive(LocomotiveType.Express));
            Assert.Contains("Insufficient funds", ex.Message);
        }
    }

    [Fact]
    public void UpgradeLocomotive_WrongPhase_ThrowsInvalidOperation()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            engine.UpgradeLocomotive(LocomotiveType.Express));
        Assert.Contains("Not in Purchase phase", ex.Message);
    }

    [Fact]
    public void UpgradeLocomotive_RaisesLocomotiveUpgradedEvent()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Purchase);

        LocomotiveUpgradedEventArgs? eventArgs = null;
        engine.LocomotiveUpgraded += (s, e) => eventArgs = e;

        if (engine.CurrentTurn.Phase == TurnPhase.Purchase)
        {
            engine.UpgradeLocomotive(LocomotiveType.Express);

            Assert.NotNull(eventArgs);
            Assert.Equal(LocomotiveType.Freight, eventArgs!.OldType);
            Assert.Equal(LocomotiveType.Express, eventArgs.NewType);
        }
    }
}
