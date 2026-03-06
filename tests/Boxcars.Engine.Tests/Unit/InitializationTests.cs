using Boxcars.Engine.Domain;
using Boxcars.Engine.Tests.Fixtures;
using Boxcars.Engine.Tests.TestDoubles;
using GE = Boxcars.Engine.Domain.GameEngine;

namespace Boxcars.Engine.Tests.Unit;

/// <summary>
/// Tests for US1: Initialize and Inspect a Game.
/// Covers constructor validation, player defaults, railroad population, and turn setup.
/// </summary>
public class InitializationTests
{
    #region Constructor Validation (T014)

    [Fact]
    public void Constructor_WithTwoPlayers_CreatesValidGame()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        Assert.Equal(GameStatus.InProgress, engine.GameStatus);
        Assert.Equal(2, engine.Players.Count);
    }

    [Fact]
    public void Constructor_WithSixPlayers_CreatesValidGame()
    {
        var map = GameEngineFixture.CreateTestMap();
        var random = GameEngineFixture.CreateDeterministicRandom(6);
        var names = new[] { "P1", "P2", "P3", "P4", "P5", "P6" };

        var engine = new GE(map, names, random);

        Assert.Equal(6, engine.Players.Count);
        Assert.Equal(GameStatus.InProgress, engine.GameStatus);
    }

    [Fact]
    public void Constructor_WithOneName_ThrowsArgumentException()
    {
        var map = GameEngineFixture.CreateTestMap();
        var random = new FixedRandomProvider();

        var ex = Assert.Throws<ArgumentException>(() =>
            new GE(map, new[] { "Solo" }, random));
        Assert.Contains("At least 2 players", ex.Message);
    }

    [Fact]
    public void Constructor_WithSevenNames_ThrowsArgumentException()
    {
        var map = GameEngineFixture.CreateTestMap();
        var random = new FixedRandomProvider();
        var names = new[] { "P1", "P2", "P3", "P4", "P5", "P6", "P7" };

        var ex = Assert.Throws<ArgumentException>(() =>
            new GE(map, names, random));
        Assert.Contains("At most 6 players", ex.Message);
    }

    [Fact]
    public void Constructor_WithEmptyName_ThrowsArgumentException()
    {
        var map = GameEngineFixture.CreateTestMap();
        var random = new FixedRandomProvider();

        var ex = Assert.Throws<ArgumentException>(() =>
            new GE(map, new[] { "Alice", "" }, random));
        Assert.Contains("null or empty", ex.Message);
    }

    [Fact]
    public void Constructor_WithNullName_ThrowsArgumentException()
    {
        var map = GameEngineFixture.CreateTestMap();
        var random = new FixedRandomProvider();

        var ex = Assert.Throws<ArgumentException>(() =>
            new GE(map, new[] { "Alice", null! }, random));
        Assert.Contains("null or empty", ex.Message);
    }

    [Fact]
    public void Constructor_WithWhitespaceName_ThrowsArgumentException()
    {
        var map = GameEngineFixture.CreateTestMap();
        var random = new FixedRandomProvider();

        var ex = Assert.Throws<ArgumentException>(() =>
            new GE(map, new[] { "Alice", "   " }, random));
        Assert.Contains("null or empty", ex.Message);
    }

    [Fact]
    public void Constructor_WithDuplicateNames_ThrowsArgumentException()
    {
        var map = GameEngineFixture.CreateTestMap();
        var random = new FixedRandomProvider();

        var ex = Assert.Throws<ArgumentException>(() =>
            new GE(map, new[] { "Alice", "alice" }, random));
        Assert.Contains("Duplicate player name", ex.Message);
    }

    [Fact]
    public void Constructor_WithNullMap_ThrowsArgumentNullException()
    {
        var random = new FixedRandomProvider();

        Assert.Throws<ArgumentNullException>(() =>
            new GE(null!, new[] { "Alice", "Bob" }, random));
    }

    [Fact]
    public void Constructor_WithNullRandom_ThrowsArgumentNullException()
    {
        var map = GameEngineFixture.CreateTestMap();

        Assert.Throws<ArgumentNullException>(() =>
            new GE(map, new[] { "Alice", "Bob" }, null!));
    }

    [Fact]
    public void Constructor_WithNullPlayerNames_ThrowsArgumentNullException()
    {
        var map = GameEngineFixture.CreateTestMap();
        var random = new FixedRandomProvider();

        Assert.Throws<ArgumentNullException>(() =>
            new GE(map, null!, random));
    }

    #endregion

    #region Initialization Defaults (T015)

    [Fact]
    public void Players_HaveCorrectStartingCash()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        foreach (var player in engine.Players)
        {
            Assert.Equal(20_000, player.Cash);
        }
    }

    [Fact]
    public void Players_HaveNoOwnedRailroads()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        foreach (var player in engine.Players)
        {
            Assert.Empty(player.OwnedRailroads);
        }
    }

    [Fact]
    public void Players_HaveNoDestination()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        foreach (var player in engine.Players)
        {
            Assert.Null(player.Destination);
        }
    }

    [Fact]
    public void Players_HaveHomeCityAssigned()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        foreach (var player in engine.Players)
        {
            Assert.NotNull(player.HomeCity);
            Assert.False(string.IsNullOrEmpty(player.HomeCity.Name));
        }
    }

    [Fact]
    public void Players_CurrentCityMatchesHomeCity()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        foreach (var player in engine.Players)
        {
            Assert.Equal(player.HomeCity.Name, player.CurrentCity.Name);
        }
    }

    [Fact]
    public void Players_StartWithFreightLocomotive()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        foreach (var player in engine.Players)
        {
            Assert.Equal(LocomotiveType.Freight, player.LocomotiveType);
        }
    }

    [Fact]
    public void Players_AreActiveAndNotBankrupt()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        foreach (var player in engine.Players)
        {
            Assert.True(player.IsActive);
            Assert.False(player.IsBankrupt);
        }
    }

    [Fact]
    public void Players_HaveCorrectNames()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        Assert.Equal("Alice", engine.Players[0].Name);
        Assert.Equal("Bob", engine.Players[1].Name);
    }

    [Fact]
    public void Players_HaveCorrectIndices()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        for (int i = 0; i < engine.Players.Count; i++)
        {
            Assert.Equal(i, engine.Players[i].Index);
        }
    }

    [Fact]
    public void Railroads_PopulatedFromMap()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();
        var map = GameEngineFixture.CreateTestMap();

        Assert.Equal(map.Railroads.Count, engine.Railroads.Count);
    }

    [Fact]
    public void Railroads_HaveCorrectNames()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        Assert.Equal("Pennsylvania Railroad", engine.Railroads[0].Name);
        Assert.Equal("Baltimore & Ohio", engine.Railroads[1].Name);
    }

    [Fact]
    public void Railroads_AllUnowned()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        foreach (var rr in engine.Railroads)
        {
            Assert.Null(rr.Owner);
        }
    }

    [Fact]
    public void CurrentTurn_IsFirstPlayerFirstTurn()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        Assert.Equal(engine.Players[0], engine.CurrentTurn.ActivePlayer);
        Assert.Equal(1, engine.CurrentTurn.TurnNumber);
        Assert.Equal(TurnPhase.DrawDestination, engine.CurrentTurn.Phase);
    }

    [Fact]
    public void CurrentTurn_NoInitialDiceResult()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        Assert.Null(engine.CurrentTurn.DiceResult);
        Assert.Equal(0, engine.CurrentTurn.MovementRemaining);
    }

    [Fact]
    public void GameStatus_IsInProgress()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        Assert.Equal(GameStatus.InProgress, engine.GameStatus);
    }

    [Fact]
    public void AllRailroadsSold_IsFalseInitially()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        Assert.False(engine.AllRailroadsSold);
    }

    [Fact]
    public void Winner_IsNullInitially()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        Assert.Null(engine.Winner);
    }

    [Fact]
    public void MapDefinition_IsAccessible()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        Assert.NotNull(engine.MapDefinition);
        Assert.Equal("TestMap", engine.MapDefinition.Name);
    }

    #endregion
}
