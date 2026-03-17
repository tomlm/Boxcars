using System.Reflection;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Boxcars.Data;
using Boxcars.Engine.Domain;
using Boxcars.Engine.Tests.Fixtures;
using Boxcars.GameEngine;
using Boxcars.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Boxcars.Engine.Tests.Unit;

public class GameEngineServiceAiHistoryTests
{
    [Fact]
    public async Task EnqueueActionAsync_GameAlreadyBusy_RejectsSecondPlayerAction()
    {
        var service = CreateGameEngineServiceForTests();

        await service.EnqueueActionAsync(
            "game-1",
            new EndTurnAction
            {
                PlayerId = "Player 1",
                PlayerIndex = 0,
                ActorUserId = "player-1"
            });

        Assert.True(service.IsGameBusy("game-1"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.EnqueueActionAsync(
                "game-1",
                new EndTurnAction
                {
                    PlayerId = "Player 1",
                    PlayerIndex = 0,
                    ActorUserId = "player-1"
                }));

        Assert.Equal("Another action is still being processed for this game. Wait for the board to finish updating and try again.", exception.Message);
    }

    [Fact]
    public async Task CreateAutomaticTurnActionAsync_AllPlayersDisconnected_ReturnsNull()
    {
        var presenceService = new GamePresenceService();
        var service = CreateGameEngineServiceForTests(presenceService);
        var (engine, _) = GameEngineFixture.CreateTestEngine();
        engine.CurrentTurn.Phase = TurnPhase.Roll;

        var gameEntity = new GameEntity
        {
            PartitionKey = "game-1",
            RowKey = "GAME",
            GameId = "game-1",
            PlayersJson = GamePlayerSelectionSerialization.Serialize(
                BotTurnServiceTestHarness.CreateSelections(
                    BotTurnServiceTestHarness.ActivePlayerUserId,
                    BotTurnServiceTestHarness.OtherPlayerUserId))
        };

        var action = await InvokeCreateAutomaticTurnActionAsync(service, gameEntity, engine);

        Assert.Null(action);
    }

    [Fact]
    public async Task CreateAutomaticTurnActionAsync_ConnectedPlayerPresent_ReturnsBuiltInAutomaticAction()
    {
        var presenceService = new GamePresenceService();
        presenceService.SetMockConnectionState("game-1", BotTurnServiceTestHarness.ActivePlayerUserId, isConnected: true);

        var service = CreateGameEngineServiceForTests(presenceService);
        var (engine, _) = GameEngineFixture.CreateTestEngine();
        engine.CurrentTurn.Phase = TurnPhase.Roll;

        var gameEntity = new GameEntity
        {
            PartitionKey = "game-1",
            RowKey = "GAME",
            GameId = "game-1",
            PlayersJson = GamePlayerSelectionSerialization.Serialize(
                BotTurnServiceTestHarness.CreateSelections(
                    BotTurnServiceTestHarness.ActivePlayerUserId,
                    BotTurnServiceTestHarness.OtherPlayerUserId))
        };

        var action = await InvokeCreateAutomaticTurnActionAsync(service, gameEntity, engine);

        Assert.IsType<RollDiceAction>(action);
    }

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

    private static async Task<PlayerAction?> InvokeCreateAutomaticTurnActionAsync(
        GameEngineService service,
        GameEntity gameEntity,
        Boxcars.Engine.Domain.GameEngine engine)
    {
        var method = typeof(GameEngineService).GetMethod("CreateAutomaticTurnActionAsync", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("CreateAutomaticTurnActionAsync was not found.");

        var task = (Task<PlayerAction?>)(method.Invoke(service, [gameEntity, engine, CancellationToken.None])
            ?? throw new InvalidOperationException("CreateAutomaticTurnActionAsync returned null."));

        return await task;
    }

    private static GameEngineService CreateGameEngineServiceForTests()
    {
        return CreateGameEngineServiceForTests(new GamePresenceService());
    }

    private static GameEngineService CreateGameEngineServiceForTests(GamePresenceService presenceService)
    {
        return new GameEngineService(
            new TestWebHostEnvironment(),
            new TableServiceClient(new Uri("https://example.com"), new TableSharedKeyCredential("devstoreaccount1", Convert.ToBase64String(new byte[32]))),
            presenceService,
            Options.Create(new BotOptions()),
            Options.Create(new PurchaseRulesOptions()),
            NullLogger<GameEngineService>.Instance);
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Boxcars.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}