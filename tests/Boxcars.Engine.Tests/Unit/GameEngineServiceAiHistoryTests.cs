using System.Reflection;
using System.Text.Json;
using System.Net;
using Azure;
using Azure.Core;
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
            GameId = "game-1"
        };
        var playerStates = BotTurnServiceTestHarness.CreatePlayerStates(
            BotTurnServiceTestHarness.CreateSelections(
                BotTurnServiceTestHarness.ActivePlayerUserId,
                BotTurnServiceTestHarness.OtherPlayerUserId));

        var action = await InvokeCreateAutomaticTurnActionAsync(service, gameEntity, playerStates, engine);

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
            GameId = "game-1"
        };
        var playerStates = BotTurnServiceTestHarness.CreatePlayerStates(
            BotTurnServiceTestHarness.CreateSelections(
                BotTurnServiceTestHarness.ActivePlayerUserId,
                BotTurnServiceTestHarness.OtherPlayerUserId));

        var action = await InvokeCreateAutomaticTurnActionAsync(service, gameEntity, playerStates, engine);

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

    [Fact]
    public async Task PersistPlayerStateControlChangesAsync_StaleSeatEtag_ReloadsAndRetries()
    {
        var persistedPlayerState = new GamePlayerStateEntity
        {
            PartitionKey = "game-1",
            RowKey = GamePlayerStateEntity.BuildRowKey(0),
            GameId = "game-1",
            SeatIndex = 0,
            PlayerUserId = "alice@example.com",
            ControllerMode = SeatControllerModes.Self,
            ETag = new ETag("\"fresh\"")
        };
        var staleOriginal = GamePlayerStateProjection.Clone(persistedPlayerState);
        staleOriginal.ETag = new ETag("\"stale\"");

        var updatedPlayerState = GamePlayerStateProjection.Clone(staleOriginal);
        updatedPlayerState.ControllerMode = SeatControllerModes.AI;
        updatedPlayerState.BotDefinitionId = "bot-1";
        updatedPlayerState.BotControlStatus = BotControlStatuses.Active;
        updatedPlayerState.BotControlActivatedUtc = DateTimeOffset.UtcNow;

        var gamesTable = new RetryingFakeGamesTableClient(persistedPlayerState);
        var service = new GameEngineService(
            new TestWebHostEnvironment(),
            gamesTable,
            new GamePresenceService(),
            null!,
            Options.Create(new BotOptions()),
            NullLogger<GameEngineService>.Instance);

        await InvokePersistPlayerStateControlChangesAsync(service, [staleOriginal], [updatedPlayerState]);

        var persisted = Assert.Single(gamesTable.PlayerStates);
        Assert.Equal(SeatControllerModes.AI, persisted.ControllerMode);
        Assert.Equal("bot-1", persisted.BotDefinitionId);
        Assert.Equal(BotControlStatuses.Active, persisted.BotControlStatus);
        Assert.True(gamesTable.UpdateAttempts >= 2);
    }

    [Fact]
    public async Task UpdatePlayerStateStatisticsAsync_RollAction_LoadsAffectedSeatDirectly()
    {
        var playerState = new GamePlayerStateEntity
        {
            PartitionKey = "game-1",
            RowKey = GamePlayerStateEntity.BuildRowKey(0),
            GameId = "game-1",
            SeatIndex = 0,
            PlayerUserId = "alice@example.com",
            ETag = new ETag("\"seat-0\"")
        };
        var gamesTable = new StatisticsDirectLoadFakeGamesTableClient(playerState);
        var service = new GameEngineService(
            new TestWebHostEnvironment(),
            gamesTable,
            new GamePresenceService(),
            null!,
            Options.Create(new BotOptions()),
            NullLogger<GameEngineService>.Instance);

        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Roll);
        var snapshotBeforeAction = engine.ToSnapshot();
        engine.RollDice();
        var snapshotAfterAction = engine.ToSnapshot();

        await InvokeUpdatePlayerStateStatisticsAsync(service, "game-1", snapshotBeforeAction, snapshotAfterAction);

        Assert.Equal(0, gamesTable.QueryCount);
        Assert.Equal(1, gamesTable.GetEntityCount);
        var persisted = Assert.Single(gamesTable.PlayerStates);
        Assert.Equal(1, persisted.TurnsTaken);
    }

    [Fact]
    public async Task UpdatePlayerStateStatisticsAsync_DestinationAssignment_TracksFriendlyAndUnfriendlyLogEntries()
    {
        var playerState = new GamePlayerStateEntity
        {
            PartitionKey = "game-1",
            RowKey = GamePlayerStateEntity.BuildRowKey(0),
            GameId = "game-1",
            SeatIndex = 0,
            PlayerUserId = "alice@example.com",
            ETag = new ETag("\"seat-0\"")
        };
        var gamesTable = new StatisticsDirectLoadFakeGamesTableClient(playerState);
        var service = new GameEngineService(
            new TestWebHostEnvironment(),
            gamesTable,
            new GamePresenceService(),
            null!,
            Options.Create(new BotOptions()),
            NullLogger<GameEngineService>.Instance);
        SetMapDefinition(service, GameEngineFixture.CreateTestMap());

        var snapshotBeforeFriendly = new global::Boxcars.Engine.Persistence.GameState
        {
            ActivePlayerIndex = 0,
            Players =
            [
                new global::Boxcars.Engine.Persistence.PlayerState
                {
                    Name = "Alice",
                    CurrentCityName = "New York",
                    OwnedRailroadIndices = [0]
                },
                new global::Boxcars.Engine.Persistence.PlayerState
                {
                    Name = "Bob",
                    CurrentCityName = "Miami",
                    OwnedRailroadIndices = [1]
                }
            ],
            RailroadOwnership = new Dictionary<int, int?>
            {
                [0] = 0,
                [1] = 1
            }
        };
        var snapshotAfterFriendly = new global::Boxcars.Engine.Persistence.GameState
        {
            ActivePlayerIndex = 0,
            Players =
            [
                new global::Boxcars.Engine.Persistence.PlayerState
                {
                    Name = "Alice",
                    CurrentCityName = "New York",
                    DestinationCityName = "Boston",
                    OwnedRailroadIndices = [0]
                },
                new global::Boxcars.Engine.Persistence.PlayerState
                {
                    Name = "Bob",
                    CurrentCityName = "Miami",
                    OwnedRailroadIndices = [1]
                }
            ],
            RailroadOwnership = new Dictionary<int, int?>
            {
                [0] = 0,
                [1] = 1
            }
        };

        await InvokeUpdatePlayerStateStatisticsAsync(service, "game-1", snapshotBeforeFriendly, snapshotAfterFriendly);

        var snapshotBeforeUnfriendly = snapshotAfterFriendly;
        var snapshotAfterUnfriendly = new global::Boxcars.Engine.Persistence.GameState
        {
            ActivePlayerIndex = 0,
            Players =
            [
                new global::Boxcars.Engine.Persistence.PlayerState
                {
                    Name = "Alice",
                    CurrentCityName = "Boston",
                    DestinationCityName = "Atlanta",
                    OwnedRailroadIndices = [0]
                },
                new global::Boxcars.Engine.Persistence.PlayerState
                {
                    Name = "Bob",
                    CurrentCityName = "Miami",
                    OwnedRailroadIndices = [1]
                }
            ],
            RailroadOwnership = new Dictionary<int, int?>
            {
                [0] = 0,
                [1] = 1
            }
        };

        await InvokeUpdatePlayerStateStatisticsAsync(service, "game-1", snapshotBeforeUnfriendly, snapshotAfterUnfriendly);

        var persisted = Assert.Single(gamesTable.PlayerStates);
        Assert.Equal(2, persisted.DestinationCount);
        Assert.Equal(1, persisted.UnfriendlyDestinationCount);
        Assert.Equal("Boston|Atlanta*", persisted.DestinationLog);
    }

    [Fact]
    public async Task UpdatePlayerStateStatisticsAsync_FeeResolution_TracksPaidAndCollectedTotals()
    {
        var payerState = new GamePlayerStateEntity
        {
            PartitionKey = "game-1",
            RowKey = GamePlayerStateEntity.BuildRowKey(0),
            GameId = "game-1",
            SeatIndex = 0,
            PlayerUserId = "alice@example.com",
            ETag = new ETag("\"seat-0\"")
        };
        var collectorState = new GamePlayerStateEntity
        {
            PartitionKey = "game-1",
            RowKey = GamePlayerStateEntity.BuildRowKey(1),
            GameId = "game-1",
            SeatIndex = 1,
            PlayerUserId = "bob@example.com",
            ETag = new ETag("\"seat-1\"")
        };
        var gamesTable = new StatisticsDirectLoadFakeGamesTableClient(payerState, collectorState);
        var service = new GameEngineService(
            new TestWebHostEnvironment(),
            gamesTable,
            new GamePresenceService(),
            null!,
            Options.Create(new BotOptions()),
            NullLogger<GameEngineService>.Instance);

        var snapshotBeforeAction = new global::Boxcars.Engine.Persistence.GameState
        {
            ActivePlayerIndex = 0,
            Players =
            [
                new global::Boxcars.Engine.Persistence.PlayerState
                {
                    Name = "Alice",
                    CurrentCityName = "Boston",
                    GrandfatheredRailroadIndices = []
                },
                new global::Boxcars.Engine.Persistence.PlayerState
                {
                    Name = "Bob",
                    CurrentCityName = "Atlanta",
                    OwnedRailroadIndices = [1]
                }
            ],
            RailroadOwnership = new Dictionary<int, int?>
            {
                [1] = 1,
                [2] = null
            },
            Turn = new global::Boxcars.Engine.Persistence.TurnState
            {
                PendingFeeAmount = 6000,
                RailroadsRiddenThisTurn = [1, 2],
                RailroadsRequiringFullOwnerRateThisTurn = [1]
            }
        };
        var snapshotAfterAction = new global::Boxcars.Engine.Persistence.GameState
        {
            ActivePlayerIndex = 0,
            Players = snapshotBeforeAction.Players,
            RailroadOwnership = snapshotBeforeAction.RailroadOwnership,
            Turn = new global::Boxcars.Engine.Persistence.TurnState
            {
                PendingFeeAmount = 0,
                RailroadsRiddenThisTurn = [1, 2],
                RailroadsRequiringFullOwnerRateThisTurn = [1]
            }
        };

        await InvokeUpdatePlayerStateStatisticsAsync(service, "game-1", snapshotBeforeAction, snapshotAfterAction);

        Assert.Equal(2, gamesTable.GetEntityCount);
        var persistedPayer = gamesTable.PlayerStates.Single(playerState => playerState.SeatIndex == 0);
        var persistedCollector = gamesTable.PlayerStates.Single(playerState => playerState.SeatIndex == 1);
        Assert.Equal(6000, persistedPayer.TotalFeesPaid);
        Assert.Equal(0, persistedPayer.TotalFeesCollected);
        Assert.Equal(5000, persistedCollector.TotalFeesCollected);
    }

    private static IReadOnlyList<EventTimelineItem> InvokeBuildTimelineItems(GameEventEntity gameEvent, GameEventEntity? previousGameEvent)
    {
        var method = typeof(GameService).GetMethod(
            "BuildTimelineItems",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            [typeof(GameEventEntity), typeof(GameEventEntity)],
            modifiers: null)
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
        List<GamePlayerStateEntity> playerStates,
        Boxcars.Engine.Domain.GameEngine engine)
    {
        var method = typeof(GameEngineService).GetMethod("CreateAutomaticTurnActionAsync", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("CreateAutomaticTurnActionAsync was not found.");

        var task = (Task<PlayerAction?>)(method.Invoke(service, [gameEntity, playerStates, engine, CancellationToken.None])
            ?? throw new InvalidOperationException("CreateAutomaticTurnActionAsync returned null."));

        return await task;
    }

    private static async Task InvokePersistPlayerStateControlChangesAsync(
        GameEngineService service,
        List<GamePlayerStateEntity> originalPlayerStates,
        List<GamePlayerStateEntity> updatedPlayerStates)
    {
        var method = typeof(GameEngineService).GetMethod("PersistPlayerStateControlChangesAsync", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("PersistPlayerStateControlChangesAsync was not found.");

        var task = (Task)(method.Invoke(service, [originalPlayerStates, updatedPlayerStates, CancellationToken.None])
            ?? throw new InvalidOperationException("PersistPlayerStateControlChangesAsync returned null."));

        await task;
    }

    private static async Task InvokeUpdatePlayerStateStatisticsAsync(
        GameEngineService service,
        string gameId,
        global::Boxcars.Engine.Persistence.GameState snapshotBeforeAction,
        global::Boxcars.Engine.Persistence.GameState snapshotAfterAction)
    {
        var method = typeof(GameEngineService).GetMethod("UpdatePlayerStateStatisticsAsync", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("UpdatePlayerStateStatisticsAsync was not found.");

        var task = (Task)(method.Invoke(service, [gameId, snapshotBeforeAction, snapshotAfterAction, CancellationToken.None])
            ?? throw new InvalidOperationException("UpdatePlayerStateStatisticsAsync returned null."));

        await task;
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
            NullLogger<GameEngineService>.Instance);
    }

    private static void SetMapDefinition(GameEngineService service, global::Boxcars.Engine.Data.Maps.MapDefinition mapDefinition)
    {
        var field = typeof(GameEngineService).GetField("_mapDefinition", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("_mapDefinition field was not found.");

        field.SetValue(service, mapDefinition);
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

    private sealed class RetryingFakeGamesTableClient(params GamePlayerStateEntity[] playerStates) : TableClient
    {
        public List<GamePlayerStateEntity> PlayerStates { get; } = playerStates.Select(GamePlayerStateProjection.Clone).ToList();
        public int UpdateAttempts { get; private set; }

        public override Task<Response<T>> GetEntityAsync<T>(
            string partitionKey,
            string rowKey,
            IEnumerable<string>? select = null,
            CancellationToken cancellationToken = default)
        {
            if (typeof(T) != typeof(GamePlayerStateEntity))
            {
                throw new NotSupportedException($"Unsupported entity type: {typeof(T).Name}");
            }

            var playerState = PlayerStates.SingleOrDefault(existing =>
                string.Equals(existing.PartitionKey, partitionKey, StringComparison.Ordinal)
                && string.Equals(existing.RowKey, rowKey, StringComparison.Ordinal));

            if (playerState is null)
            {
                throw new RequestFailedException((int)HttpStatusCode.NotFound, "Entity was not found.");
            }

            return Task.FromResult(Response.FromValue((T)(ITableEntity)GamePlayerStateProjection.Clone(playerState), new FakeResponse((int)HttpStatusCode.OK)));
        }

        public override Task<Response> UpdateEntityAsync<T>(
            T entity,
            ETag ifMatch,
            TableUpdateMode mode = TableUpdateMode.Merge,
            CancellationToken cancellationToken = default)
        {
            UpdateAttempts++;

            var tableEntity = entity as TableEntity ?? throw new InvalidOperationException("Expected table entity.");
            var playerState = PlayerStates.Single(existing =>
                string.Equals(existing.PartitionKey, tableEntity.PartitionKey, StringComparison.Ordinal)
                && string.Equals(existing.RowKey, tableEntity.RowKey, StringComparison.Ordinal));

            if (ifMatch != playerState.ETag)
            {
                throw new RequestFailedException((int)HttpStatusCode.PreconditionFailed, "The update condition specified in the request was not satisfied.");
            }

            playerState.ControllerMode = tableEntity.GetString(nameof(GamePlayerStateEntity.ControllerMode)) ?? playerState.ControllerMode;
            playerState.ControllerUserId = tableEntity.GetString(nameof(GamePlayerStateEntity.ControllerUserId)) ?? playerState.ControllerUserId;
            playerState.BotDefinitionId = tableEntity.GetString(nameof(GamePlayerStateEntity.BotDefinitionId)) ?? playerState.BotDefinitionId;
            playerState.AuctionPlanTurnNumber = tableEntity.GetInt32(nameof(GamePlayerStateEntity.AuctionPlanTurnNumber)) ?? playerState.AuctionPlanTurnNumber;
            playerState.AuctionPlanRailroadIndex = tableEntity.GetInt32(nameof(GamePlayerStateEntity.AuctionPlanRailroadIndex)) ?? playerState.AuctionPlanRailroadIndex;
            playerState.AuctionPlanStartingPrice = tableEntity.GetInt32(nameof(GamePlayerStateEntity.AuctionPlanStartingPrice)) ?? playerState.AuctionPlanStartingPrice;
            playerState.AuctionPlanMaximumBid = tableEntity.GetInt32(nameof(GamePlayerStateEntity.AuctionPlanMaximumBid)) ?? playerState.AuctionPlanMaximumBid;
            playerState.BotControlActivatedUtc = tableEntity.GetDateTimeOffset(nameof(GamePlayerStateEntity.BotControlActivatedUtc)) ?? playerState.BotControlActivatedUtc;
            playerState.BotControlClearedUtc = tableEntity.GetDateTimeOffset(nameof(GamePlayerStateEntity.BotControlClearedUtc)) ?? playerState.BotControlClearedUtc;
            playerState.BotControlStatus = tableEntity.GetString(nameof(GamePlayerStateEntity.BotControlStatus)) ?? playerState.BotControlStatus;
            playerState.BotControlClearReason = tableEntity.GetString(nameof(GamePlayerStateEntity.BotControlClearReason)) ?? playerState.BotControlClearReason;
            playerState.ETag = new ETag("\"updated\"");

            return Task.FromResult<Response>(new FakeResponse((int)HttpStatusCode.NoContent));
        }
    }

    private sealed class StatisticsDirectLoadFakeGamesTableClient(params GamePlayerStateEntity[] playerStates) : TableClient
    {
        public List<GamePlayerStateEntity> PlayerStates { get; } = playerStates.Select(GamePlayerStateProjection.Clone).ToList();
        public int QueryCount { get; private set; }
        public int GetEntityCount { get; private set; }

        public override Task<Response<T>> GetEntityAsync<T>(
            string partitionKey,
            string rowKey,
            IEnumerable<string>? select = null,
            CancellationToken cancellationToken = default)
        {
            GetEntityCount++;

            if (typeof(T) != typeof(GamePlayerStateEntity))
            {
                throw new NotSupportedException($"Unsupported entity type: {typeof(T).Name}");
            }

            var playerState = PlayerStates.SingleOrDefault(existing =>
                string.Equals(existing.PartitionKey, partitionKey, StringComparison.Ordinal)
                && string.Equals(existing.RowKey, rowKey, StringComparison.Ordinal));

            if (playerState is null)
            {
                throw new RequestFailedException((int)HttpStatusCode.NotFound, "Entity was not found.");
            }

            return Task.FromResult(Response.FromValue((T)(ITableEntity)GamePlayerStateProjection.Clone(playerState), new FakeResponse((int)HttpStatusCode.OK)));
        }

        public override AsyncPageable<T> QueryAsync<T>(
            string? filter = null,
            int? maxPerPage = null,
            IEnumerable<string>? select = null,
            CancellationToken cancellationToken = default)
        {
            QueryCount++;
            throw new InvalidOperationException("Statistics updates should load affected player rows directly, not scan the player-state partition.");
        }

        public override Task<Response> UpdateEntityAsync<T>(
            T entity,
            ETag ifMatch,
            TableUpdateMode mode = TableUpdateMode.Merge,
            CancellationToken cancellationToken = default)
        {
            var tableEntity = entity as TableEntity ?? throw new InvalidOperationException("Expected table entity.");
            var playerState = PlayerStates.Single(existing =>
                string.Equals(existing.PartitionKey, tableEntity.PartitionKey, StringComparison.Ordinal)
                && string.Equals(existing.RowKey, tableEntity.RowKey, StringComparison.Ordinal));

            playerState.TurnsTaken = tableEntity.GetInt32(nameof(GamePlayerStateEntity.TurnsTaken)) ?? playerState.TurnsTaken;
            playerState.FreightTurnCount = tableEntity.GetInt32(nameof(GamePlayerStateEntity.FreightTurnCount)) ?? playerState.FreightTurnCount;
            playerState.FreightRollTotal = tableEntity.GetInt32(nameof(GamePlayerStateEntity.FreightRollTotal)) ?? playerState.FreightRollTotal;
            playerState.ExpressTurnCount = tableEntity.GetInt32(nameof(GamePlayerStateEntity.ExpressTurnCount)) ?? playerState.ExpressTurnCount;
            playerState.ExpressRollTotal = tableEntity.GetInt32(nameof(GamePlayerStateEntity.ExpressRollTotal)) ?? playerState.ExpressRollTotal;
            playerState.SuperchiefTurnCount = tableEntity.GetInt32(nameof(GamePlayerStateEntity.SuperchiefTurnCount)) ?? playerState.SuperchiefTurnCount;
            playerState.SuperchiefRollTotal = tableEntity.GetInt32(nameof(GamePlayerStateEntity.SuperchiefRollTotal)) ?? playerState.SuperchiefRollTotal;
            playerState.BonusRollCount = tableEntity.GetInt32(nameof(GamePlayerStateEntity.BonusRollCount)) ?? playerState.BonusRollCount;
            playerState.BonusRollTotal = tableEntity.GetInt32(nameof(GamePlayerStateEntity.BonusRollTotal)) ?? playerState.BonusRollTotal;
            playerState.TotalPayoffsCollected = tableEntity.GetInt32(nameof(GamePlayerStateEntity.TotalPayoffsCollected)) ?? playerState.TotalPayoffsCollected;
            playerState.TotalFeesPaid = tableEntity.GetInt32(nameof(GamePlayerStateEntity.TotalFeesPaid)) ?? playerState.TotalFeesPaid;
            playerState.TotalFeesCollected = tableEntity.GetInt32(nameof(GamePlayerStateEntity.TotalFeesCollected)) ?? playerState.TotalFeesCollected;
            playerState.TotalRailroadFaceValuePurchased = tableEntity.GetInt32(nameof(GamePlayerStateEntity.TotalRailroadFaceValuePurchased)) ?? playerState.TotalRailroadFaceValuePurchased;
            playerState.TotalRailroadAmountPaid = tableEntity.GetInt32(nameof(GamePlayerStateEntity.TotalRailroadAmountPaid)) ?? playerState.TotalRailroadAmountPaid;
            playerState.AuctionWins = tableEntity.GetInt32(nameof(GamePlayerStateEntity.AuctionWins)) ?? playerState.AuctionWins;
            playerState.AuctionBidsPlaced = tableEntity.GetInt32(nameof(GamePlayerStateEntity.AuctionBidsPlaced)) ?? playerState.AuctionBidsPlaced;
            playerState.RailroadsPurchasedCount = tableEntity.GetInt32(nameof(GamePlayerStateEntity.RailroadsPurchasedCount)) ?? playerState.RailroadsPurchasedCount;
            playerState.RailroadsAuctionedCount = tableEntity.GetInt32(nameof(GamePlayerStateEntity.RailroadsAuctionedCount)) ?? playerState.RailroadsAuctionedCount;
            playerState.RailroadsSoldToBankCount = tableEntity.GetInt32(nameof(GamePlayerStateEntity.RailroadsSoldToBankCount)) ?? playerState.RailroadsSoldToBankCount;
            playerState.DestinationCount = tableEntity.GetInt32(nameof(GamePlayerStateEntity.DestinationCount)) ?? playerState.DestinationCount;
            playerState.UnfriendlyDestinationCount = tableEntity.GetInt32(nameof(GamePlayerStateEntity.UnfriendlyDestinationCount)) ?? playerState.UnfriendlyDestinationCount;
            playerState.DestinationLog = tableEntity.GetString(nameof(GamePlayerStateEntity.DestinationLog)) ?? playerState.DestinationLog;
            playerState.ETag = new ETag("\"updated\"");

            return Task.FromResult<Response>(new FakeResponse((int)HttpStatusCode.NoContent));
        }
    }

    private sealed class FakeResponse(int status) : Response
    {
        public override int Status => status;
        public override string ReasonPhrase => string.Empty;
        public override Stream? ContentStream { get; set; }
        public override string ClientRequestId { get; set; } = Guid.NewGuid().ToString("N");

        public override void Dispose()
        {
        }

        protected override bool ContainsHeader(string name)
        {
            return false;
        }

        protected override IEnumerable<HttpHeader> EnumerateHeaders()
        {
            yield break;
        }

        protected override bool TryGetHeader(string name, out string value)
        {
            value = string.Empty;
            return false;
        }

        protected override bool TryGetHeaderValues(string name, out IEnumerable<string> values)
        {
            values = [];
            return false;
        }
    }
}
