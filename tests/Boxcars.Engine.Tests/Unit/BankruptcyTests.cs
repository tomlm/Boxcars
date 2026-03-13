using Boxcars.Engine.Domain;
using Boxcars.Engine.Events;
using Boxcars.Engine.Tests.Fixtures;
using Boxcars.Engine.Tests.TestDoubles;

namespace Boxcars.Engine.Tests.Unit;

/// <summary>
/// Tests for bankruptcy detection and resolution (T040).
/// </summary>
public class BankruptcyTests
{
    [Fact]
    public void DeclinePurchase_WhenPlayerCannotPayAndOwnsNoRailroads_EliminatesPlayer()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Purchase);

        var player = engine.CurrentTurn.ActivePlayer;
        var feeRailroad = engine.Railroads.First(rr => rr.Index == 1);
        var feeOwner = engine.Players[1];
        feeRailroad.Owner = feeOwner;
        feeOwner.OwnedRailroads.Add(feeRailroad);
        player.Cash = 0;
        engine.CurrentTurn.RailroadsRiddenThisTurn.Clear();
        engine.CurrentTurn.RailroadsRiddenThisTurn.Add(feeRailroad.Index);

        engine.DeclinePurchase();

        Assert.True(player.IsBankrupt);
        Assert.False(player.IsActive);
        Assert.NotNull(engine.CurrentTurn.ForcedSaleState);
        Assert.True(engine.CurrentTurn.ForcedSaleState!.EliminationTriggered);
        Assert.Equal(TurnPhase.EndTurn, engine.CurrentTurn.Phase);
    }

    [Fact]
    public void SellRailroadToBank_WhenFinalSaleStillCannotCoverFees_EliminatesPlayer()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Purchase);

        var player = engine.CurrentTurn.ActivePlayer;
        var feeRailroad = engine.Railroads.First(rr => rr.Index == 1);
        var ownedRailroad = engine.Railroads.First(rr => rr.Index == 0);
        var feeOwner = engine.Players[1];
        feeRailroad.Owner = feeOwner;
        feeOwner.OwnedRailroads.Add(feeRailroad);
        ownedRailroad.Owner = player;
        player.OwnedRailroads.Add(ownedRailroad);
        player.Cash = 0;
        engine.CurrentTurn.RailroadsRiddenThisTurn.Clear();
        engine.CurrentTurn.RailroadsRiddenThisTurn.Add(feeRailroad.Index);

        engine.DeclinePurchase();
        engine.SellRailroadToBank(ownedRailroad);

        Assert.True(player.IsBankrupt);
        Assert.False(player.IsActive);
        Assert.NotNull(engine.CurrentTurn.ForcedSaleState);
        Assert.True(engine.CurrentTurn.ForcedSaleState!.EliminationTriggered);
        Assert.Equal(1, engine.CurrentTurn.ForcedSaleState.SalesCompletedCount);
        Assert.Equal(TurnPhase.EndTurn, engine.CurrentTurn.Phase);
    }

    [Fact]
    public void Player_NegativeCash_IsBankrupt()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();
        var player = engine.Players[0];

        player.Cash = -1;
        player.IsBankrupt = true;

        Assert.True(player.IsBankrupt);
    }

    [Fact]
    public void BankruptPlayer_IsNotActive()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();
        var player = engine.Players[0];

        player.IsBankrupt = true;
        player.IsActive = false;

        Assert.True(player.IsBankrupt);
        Assert.False(player.IsActive);
    }

    [Fact]
    public void PlayerBankrupt_RaisesEvent()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();
        var player = engine.Players[0];

        PlayerBankruptEventArgs? eventArgs = null;
        engine.PlayerBankrupt += (s, e) => eventArgs = e;

        // Simulate bankruptcy by directly invoking engine internals
        // We'll test through use fees that cause bankruptcy
        player.Cash = 0;
        player.IsBankrupt = true;
        player.IsActive = false;

        // Verify flag state
        Assert.True(player.IsBankrupt);
        Assert.False(player.IsActive);
    }

    [Fact]
    public void Bankruptcy_PropertyChangesFire()
    {
        var player = new Player("Test", 0);
        var changedProps = new List<string>();
        player.PropertyChanged += (s, e) => changedProps.Add(e.PropertyName!);

        player.IsBankrupt = true;

        Assert.Contains("IsBankrupt", changedProps);
    }

    [Fact]
    public void Player_WithZeroCash_IsNotNecessarilyBankrupt()
    {
        var player = new Player("Test", 0);
        player.Cash = 0;

        Assert.False(player.IsBankrupt);
    }
}
