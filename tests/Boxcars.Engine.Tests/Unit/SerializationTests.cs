using System.Text.Json;
using Boxcars.Engine.Domain;
using Boxcars.Engine.Persistence;
using Boxcars.Engine.Tests.Fixtures;
using Boxcars.Engine.Tests.TestDoubles;
using GE = Boxcars.Engine.Domain.GameEngine;

namespace Boxcars.Engine.Tests.Unit;

/// <summary>
/// Tests for serialization round-trip and snapshot/restore (T045-T046, T059).
/// </summary>
public class SerializationTests
{
    [Fact]
    public void ToSnapshot_ProducesValidGameState()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        var snapshot = engine.ToSnapshot();

        Assert.NotNull(snapshot);
        Assert.Equal(2, snapshot.Players.Count);
        Assert.True(snapshot.RailroadOwnership.Count > 0);
        Assert.NotNull(snapshot.Turn);
    }

    [Fact]
    public void ToSnapshot_CapturesPlayerState()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();
        var player = engine.Players[0];

        var snapshot = engine.ToSnapshot();
        var playerState = snapshot.Players[0];

        Assert.Equal(player.Name, playerState.Name);
        Assert.Equal(player.Cash, playerState.Cash);
        Assert.Equal(player.HomeCity.Name, playerState.HomeCityName);
        Assert.Equal(player.LocomotiveType.ToString(), playerState.LocomotiveType);
    }

    [Fact]
    public void Snapshot_RoundTrip_Json_PreservesData()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        var snapshot = engine.ToSnapshot();
        var json = JsonSerializer.Serialize(snapshot);
        var deserialized = JsonSerializer.Deserialize<GameState>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(snapshot.Players.Count, deserialized!.Players.Count);
        Assert.Equal(snapshot.GameStatus, deserialized.GameStatus);
        Assert.Equal(snapshot.RailroadOwnership.Count, deserialized.RailroadOwnership.Count);
    }

    [Fact]
    public void Snapshot_RoundTrip_Json_IsReasonableSize()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        var snapshot = engine.ToSnapshot();
        var json = JsonSerializer.Serialize(snapshot);

        // Azure Table Storage entity limit is 1MB; game state should be well under
        Assert.True(json.Length < 500_000, $"JSON size {json.Length} exceeds reasonable limit");
    }

    [Fact]
    public void FromSnapshot_RestoresEngineState()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();

        // Modify some state
        random.QueueWeightedDraw(1);
        random.QueueWeightedDraw(0);
        engine.DrawDestination();

        var snapshot = engine.ToSnapshot();
        var map = engine.MapDefinition;

        var restored = GE.FromSnapshot(snapshot, map, new FixedRandomProvider());

        Assert.Equal(engine.GameStatus, restored.GameStatus);
        Assert.Equal(engine.Players.Count, restored.Players.Count);
        Assert.Equal(engine.Players[0].Name, restored.Players[0].Name);
        Assert.Equal(engine.Players[0].Cash, restored.Players[0].Cash);
        Assert.Equal(engine.Players[0].Destination, restored.Players[0].Destination);
    }

    [Fact]
    public void FromSnapshot_RestoresTurnState()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();

        random.QueueWeightedDraw(1);
        random.QueueWeightedDraw(0);
        engine.DrawDestination();

        var snapshot = engine.ToSnapshot();
        var restored = GE.FromSnapshot(snapshot, engine.MapDefinition, new FixedRandomProvider());

        Assert.Equal(engine.CurrentTurn.TurnNumber, restored.CurrentTurn.TurnNumber);
        Assert.Equal(engine.CurrentTurn.Phase, restored.CurrentTurn.Phase);
    }

    [Fact]
    public void FromSnapshot_NullSnapshot_ThrowsArgumentNull()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        Assert.Throws<ArgumentNullException>(() =>
            GE.FromSnapshot(null!, engine.MapDefinition, new FixedRandomProvider()));
    }

    [Fact]
    public void FromSnapshot_RestoredEngine_CanContinuePlay()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();

        // Draw destination
        random.QueueWeightedDraw(1);
        random.QueueWeightedDraw(0);
        engine.DrawDestination();

        var snapshot = engine.ToSnapshot();
        var restoredRandom = new FixedRandomProvider();
        var restored = GE.FromSnapshot(snapshot, engine.MapDefinition, restoredRandom);

        // Should be able to continue playing: roll dice
        restoredRandom.QueueDiceRoll(3, 2);
        restored.RollDice();

        Assert.NotNull(restored.CurrentTurn.DiceResult);
        Assert.Equal(5, restored.CurrentTurn.DiceResult!.Total);
    }

    [Fact]
    public void ToSnapshot_CapturesRailroadOwnership()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        // Assign ownership
        engine.Railroads[0].Owner = engine.Players[0];
        engine.Players[0].OwnedRailroads.Add(engine.Railroads[0]);

        var snapshot = engine.ToSnapshot();

        Assert.Equal(0, snapshot.RailroadOwnership[engine.Railroads[0].Index]);
    }

    [Fact]
    public void ToSnapshot_UnownedRailroad_HasNullOwnerIndex()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        var snapshot = engine.ToSnapshot();

        // Unowned railroads should have null owner index
        Assert.True(snapshot.RailroadOwnership.Values.Any(v => v == null),
            "Expected at least one railroad with null owner index");
    }

    [Fact]
    public void Snapshot_RoundTrip_PreservesLocomotiveType()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();
        engine.Players[0].LocomotiveType = LocomotiveType.Express;

        var snapshot = engine.ToSnapshot();
        var restored = GE.FromSnapshot(snapshot, engine.MapDefinition, new FixedRandomProvider());

        Assert.Equal(LocomotiveType.Express, restored.Players[0].LocomotiveType);
    }
}
