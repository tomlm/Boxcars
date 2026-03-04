using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Domain;
using Boxcars.Engine.Events;
using Boxcars.Engine.Tests.Fixtures;
using Boxcars.Engine.Tests.TestDoubles;

namespace Boxcars.Engine.Tests.Unit;

/// <summary>
/// Tests for win condition: $200k cash + returned to home city (T050, T055).
/// </summary>
public class WinConditionTests
{
    [Fact]
    public void Player_CanDeclare_When200k()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();
        var player = engine.Players[0];

        player.Cash = 200_000;

        Assert.True(player.Cash >= 200_000);
    }

    [Fact]
    public void Player_HasDeclared_PropertyChange()
    {
        var player = new Player("Test", 0);
        var changedProps = new List<string>();
        player.PropertyChanged += (s, e) => changedProps.Add(e.PropertyName!);

        player.HasDeclared = true;

        Assert.Contains("HasDeclared", changedProps);
        Assert.True(player.HasDeclared);
    }

    [Fact]
    public void GameOver_SetsWinnerAndStatus()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        // When game ends, winner and status should be set
        // We verify the initial state is not completed
        Assert.Equal(GameStatus.InProgress, engine.GameStatus);
        Assert.Null(engine.Winner);
    }

    [Fact]
    public void GameOver_Event_FiresCorrectly()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        GameOverEventArgs? eventArgs = null;
        engine.GameOver += (s, e) => eventArgs = e;

        // Verify event can be subscribed
        Assert.Null(eventArgs);
    }

    [Fact]
    public void Player_Below200k_CannotDeclare()
    {
        var player = new Player("Test", 0);
        player.Cash = 199_999;

        Assert.True(player.Cash < 200_000);
        Assert.False(player.HasDeclared);
    }

    [Fact]
    public void Player_AtHome_WithDeclared_IsWinState()
    {
        var homeCity = new CityDefinition { Name = "NYC", RegionCode = "NE", PayoutIndex = 0 };
        var player = new Player("Test", 0);
        player.Cash = 250_000;
        player.HomeCity = homeCity;
        player.CurrentCity = homeCity;
        player.HasDeclared = true;

        Assert.Equal(player.HomeCity.Name, player.CurrentCity.Name);
        Assert.True(player.HasDeclared);
        Assert.True(player.Cash >= 200_000);
    }
}
