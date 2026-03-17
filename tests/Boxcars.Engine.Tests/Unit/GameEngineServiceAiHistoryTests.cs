using System.Reflection;
using System.Text.Json;
using Azure;
using Boxcars.Data;
using Boxcars.Engine.Domain;
using Boxcars.Engine.Tests.Fixtures;
using Boxcars.GameEngine;
using Boxcars.Services;

namespace Boxcars.Engine.Tests.Unit;

public class GameEngineServiceAiHistoryTests
{
    [Fact]
    public void ResolveIfMatchETag_EmptyGameEntityEtag_UsesWildcard()
    {
        var gameEntity = new GameEntity
        {
            PartitionKey = "game-1",
            RowKey = "GAME",
            GameId = "game-1"
        };

        var resolved = InvokeResolveIfMatchEtag(gameEntity);

        Assert.Equal(ETag.All.ToString(), resolved.ToString());
    }

    [Fact]
    public void BuildTimelineItems_PersistedAiMoveEvent_PreservesStructuredAiAttribution()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Move);

        var action = new MoveAction
        {
            PlayerId = engine.CurrentTurn.ActivePlayer.Name,
            PlayerIndex = engine.CurrentTurn.ActivePlayer.Index,
            ActorUserId = BotOptions.DefaultServerActorUserId,
            PointsTaken = ["albany", "boston"],
            SelectedSegmentKeys = ["albany|boston|0"],
            BotMetadata = new BotRecordedActionMetadata
            {
                BotDefinitionId = "bot-1",
                BotName = "El Cheapo",
                DecisionSource = "SuggestedRoute",
                FallbackReason = "OpenAI request timed out."
            }
        };

        engine.MoveAlongRoute(1);
        var snapshot = engine.ToSnapshot();
        var persistedEvent = new GameEventEntity
        {
            PartitionKey = "game-1",
            RowKey = "Event_0000000001",
            GameId = "game-1",
            EventKind = nameof(MoveAction),
            EventData = GameEventSerialization.SerializeEventData(action),
            ChangeSummary = "Alice moved to Boston.",
            SerializedGameState = JsonSerializer.Serialize(snapshot),
            OccurredUtc = DateTimeOffset.UtcNow,
            ActingPlayerIndex = 0,
            ActingUserId = BotOptions.DefaultServerActorUserId,
            CreatedBy = "Alice"
        };

        var timelineItem = Assert.Single(InvokeBuildTimelineItems(persistedEvent, null));

        Assert.Equal(EventTimelineKind.Move, timelineItem.EventKind);
        Assert.Equal("Alice moved to Boston.", timelineItem.Description);
        Assert.True(timelineItem.IsAiAction);
        Assert.Equal(BotOptions.DefaultServerActorUserId, timelineItem.ActingUserId);
        Assert.Equal("bot-1", timelineItem.BotDefinitionId);
        Assert.Equal("El Cheapo", timelineItem.BotName);
        Assert.Equal("SuggestedRoute", timelineItem.BotDecisionSource);
        Assert.Equal("OpenAI request timed out.", timelineItem.BotFallbackReason);
    }

    [Fact]
    public void BuildTimelineItems_PersistedHumanMoveEvent_DoesNotProjectAiAttribution()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Move);

        var action = new MoveAction
        {
            PlayerId = engine.CurrentTurn.ActivePlayer.Name,
            PlayerIndex = engine.CurrentTurn.ActivePlayer.Index,
            ActorUserId = "alice@example.com",
            PointsTaken = ["albany", "boston"],
            SelectedSegmentKeys = ["albany|boston|0"]
        };

        engine.MoveAlongRoute(1);
        var snapshot = engine.ToSnapshot();
        var persistedEvent = new GameEventEntity
        {
            PartitionKey = "game-1",
            RowKey = "Event_0000000002",
            GameId = "game-1",
            EventKind = nameof(MoveAction),
            EventData = GameEventSerialization.SerializeEventData(action),
            ChangeSummary = "Alice moved to Boston.",
            SerializedGameState = JsonSerializer.Serialize(snapshot),
            OccurredUtc = DateTimeOffset.UtcNow,
            ActingPlayerIndex = 0,
            ActingUserId = "alice@example.com",
            CreatedBy = "Alice"
        };

        var timelineItem = Assert.Single(InvokeBuildTimelineItems(persistedEvent, null));

        Assert.False(timelineItem.IsAiAction);
        Assert.Equal("alice@example.com", timelineItem.ActingUserId);
        Assert.Equal(string.Empty, timelineItem.BotDefinitionId);
        Assert.Equal(string.Empty, timelineItem.BotName);
        Assert.Equal(string.Empty, timelineItem.BotDecisionSource);
        Assert.Equal(string.Empty, timelineItem.BotFallbackReason);
    }

    private static IReadOnlyList<EventTimelineItem> InvokeBuildTimelineItems(GameEventEntity gameEvent, GameEventEntity? previousGameEvent)
    {
        var method = typeof(GameService).GetMethod("BuildTimelineItems", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("BuildTimelineItems was not found.");

        return (IReadOnlyList<EventTimelineItem>)(method.Invoke(null, [gameEvent, previousGameEvent])
            ?? throw new InvalidOperationException("BuildTimelineItems returned null."));
    }

    private static ETag InvokeResolveIfMatchEtag(GameEntity gameEntity)
    {
        var method = typeof(GameEngineService).GetMethod("ResolveIfMatchETag", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ResolveIfMatchETag was not found.");

        return (ETag)(method.Invoke(null, [gameEntity])
            ?? throw new InvalidOperationException("ResolveIfMatchETag returned null."));
    }
}