using Boxcars.Data;
using Boxcars.GameEngine;
using Boxcars.Services;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using System.Net;

namespace Boxcars.Engine.Tests.Unit;

public class GamePresenceServiceTests
{
    [Fact]
    public void PruneStaleConnections_ExpiredHeartbeat_MarksUserDisconnected()
    {
        var clock = new AdjustableTimeProvider(DateTimeOffset.UtcNow);
        var service = new GamePresenceService(clock, TimeSpan.FromSeconds(15), Timeout.InfiniteTimeSpan);

        service.AddConnection("game-1", "target", "connection-1");

        clock.Advance(TimeSpan.FromSeconds(16));
        var changedGames = service.PruneStaleConnections();

        Assert.Contains("game-1", changedGames);
        Assert.False(service.IsUserConnected("game-1", "target"));
    }

    [Fact]
    public void RefreshConnection_BeforeTimeout_KeepsUserConnected()
    {
        var clock = new AdjustableTimeProvider(DateTimeOffset.UtcNow);
        var service = new GamePresenceService(clock, TimeSpan.FromSeconds(15), Timeout.InfiniteTimeSpan);

        service.AddConnection("game-1", "target", "connection-1");
        clock.Advance(TimeSpan.FromSeconds(10));

        service.RefreshConnection("game-1", "target", "connection-1");

        clock.Advance(TimeSpan.FromSeconds(10));
        service.PruneStaleConnections();

        Assert.True(service.IsUserConnected("game-1", "target"));
    }

    [Fact]
    public void RemoveConnection_ControllerDisconnect_PreservesDelegatedControl()
    {
        var service = new GamePresenceService();

        service.AddConnection("game-1", "controller", "connection-1");
        service.SetMockConnectionState("game-1", "target", isConnected: false);

        var taken = service.TryTakeDelegatedControl("game-1", "target", "controller");
        var removed = service.RemoveConnection("game-1", "controller", "connection-1");

        Assert.True(taken);
        Assert.True(removed);
        Assert.Equal("controller", service.GetDelegatedControllerUserId("game-1", "target"));
    }

    [Fact]
    public void SetMockConnectionState_ControllerDisconnect_PreservesDelegatedControl()
    {
        var service = new GamePresenceService();

        service.SetMockConnectionState("game-1", "controller", isConnected: true);
        service.SetMockConnectionState("game-1", "target", isConnected: false);

        var taken = service.TryTakeDelegatedControl("game-1", "target", "controller");
        service.SetMockConnectionState("game-1", "controller", isConnected: false);

        Assert.True(taken);
        Assert.Equal("controller", service.GetDelegatedControllerUserId("game-1", "target"));
    }

    [Fact]
    public void SetMockConnectionState_TargetReconnect_ClearsDelegatedControl()
    {
        var service = new GamePresenceService();

        service.SetMockConnectionState("game-1", "controller", isConnected: true);
        service.SetMockConnectionState("game-1", "target", isConnected: false);
        service.TryTakeDelegatedControl("game-1", "target", "controller");

        service.SetMockConnectionState("game-1", "target", isConnected: true);

        Assert.Null(service.GetDelegatedControllerUserId("game-1", "target"));
    }

    [Fact]
    public void ResolveSeatControllerState_OfflineDelegatedSeat_ReturnsDelegated()
    {
        var service = new GamePresenceService();

        service.SetMockConnectionState("game-1", "controller", isConnected: true);
        service.SetMockConnectionState("game-1", "target", isConnected: false);
        service.TryTakeDelegatedControl("game-1", "target", "controller");

        var controllerState = service.ResolveSeatControllerState("game-1", "target", activeSeatState: null);

        Assert.Equal(SeatControllerModes.Delegated, controllerState.ControllerMode);
        Assert.Equal("controller", controllerState.DelegatedControllerUserId);
        Assert.False(controllerState.IsConnected);
    }

    [Fact]
    public void ResolveSeatControllerState_DisconnectedHumanWithoutManualController_DefaultsToBot()
    {
        var service = new GamePresenceService();

        service.SetMockConnectionState("game-1", "target", isConnected: false);

        var controllerState = service.ResolveSeatControllerState("game-1", "target", activeSeatState: null);

        Assert.Equal(SeatControllerModes.AI, controllerState.ControllerMode);
        Assert.Equal("target", controllerState.BotDefinitionId);
        Assert.False(controllerState.IsConnected);
    }

    [Fact]
    public void ResolveSeatControllerState_BotControl_ReturnsBot()
    {
        var service = new GamePresenceService();

        service.SetMockConnectionState("game-1", "target", isConnected: false);

        var controllerState = service.ResolveSeatControllerState(
            "game-1",
            "target",
            new GamePlayerStateEntity
            {
                GameId = "game-1",
                PlayerUserId = "target",
                ControllerUserId = string.Empty,
                ControllerMode = SeatControllerModes.AI,
                BotDefinitionId = "bot-1",
                BotControlStatus = BotControlStatuses.Active
            });

        Assert.Equal(SeatControllerModes.AI, controllerState.ControllerMode);
        Assert.Null(controllerState.DelegatedControllerUserId);
        Assert.Equal("target", controllerState.BotDefinitionId);
    }

    [Fact]
    public void ResolveSeatControllerState_DedicatedBotControl_ReturnsBot()
    {
        var service = new GamePresenceService();

        var controllerState = service.ResolveSeatControllerState(
            "game-1",
            "beatle-bot",
            new GamePlayerStateEntity
            {
                GameId = "game-1",
                PlayerUserId = "beatle-bot",
                ControllerMode = SeatControllerModes.AI,
                BotDefinitionId = "bot-1",
                BotControlStatus = BotControlStatuses.Active
            });

        Assert.Equal(SeatControllerModes.AI, controllerState.ControllerMode);
        Assert.Equal("beatle-bot", controllerState.BotDefinitionId);
    }

    [Fact]
    public void ReleaseDelegatedControl_DisconnectedSeatFallsBackToBot()
    {
        var service = new GamePresenceService();

        service.SetMockConnectionState("game-1", "controller", isConnected: true);
        service.SetMockConnectionState("game-1", "target", isConnected: false);
        service.TryTakeDelegatedControl("game-1", "target", "controller");
        service.ReleaseDelegatedControl("game-1", "target", "controller");

        var controllerState = service.ResolveSeatControllerState(
            "game-1",
            "target",
            new GamePlayerStateEntity
            {
                GameId = "game-1",
                PlayerUserId = "target",
                ControllerUserId = "controller",
                ControllerMode = SeatControllerModes.AI,
                BotDefinitionId = "bot-1",
                BotControlStatus = BotControlStatuses.Active
            });

        Assert.Equal(SeatControllerModes.AI, controllerState.ControllerMode);
        Assert.Null(controllerState.DelegatedControllerUserId);
    }

    [Fact]
    public void Reconnect_BotControlFallsBackToSelf()
    {
        var service = new GamePresenceService();

        service.SetMockConnectionState("game-1", "controller", isConnected: true);
        service.SetMockConnectionState("game-1", "target", isConnected: false);
        service.TryTakeDelegatedControl("game-1", "target", "controller");
        service.SetMockConnectionState("game-1", "target", isConnected: true);

        var controllerState = service.ResolveSeatControllerState(
            "game-1",
            "target",
            new GamePlayerStateEntity
            {
                GameId = "game-1",
                PlayerUserId = "target",
                ControllerUserId = "controller",
                ControllerMode = SeatControllerModes.AI,
                BotDefinitionId = "bot-1",
                BotControlStatus = BotControlStatuses.Active
            });

        Assert.Equal(SeatControllerModes.AI, controllerState.ControllerMode);
        Assert.True(controllerState.IsConnected);
    }

    [Fact]
    public async Task SetMockConnectionState_HumanReconnect_ClearsPersistedAiControl()
    {
        var gamesTable = new FakeGamesTableClient(
            new GameEntity
            {
                PartitionKey = "game-1",
                RowKey = "GAME",
                GameId = "game-1",
                Seats =
                [
                    new GameSeatDefinition
                    {
                        SeatIndex = 0,
                        PlayerUserId = "target",
                        DisplayName = "Target",
                        Color = "#111111"
                    }
                ]
            },
            new GamePlayerStateEntity
            {
                PartitionKey = "game-1",
                RowKey = GamePlayerStateEntity.BuildRowKey(0),
                GameId = "game-1",
                SeatIndex = 0,
                PlayerUserId = "target",
                ControllerMode = SeatControllerModes.AI,
                BotDefinitionId = "target",
                BotControlStatus = BotControlStatuses.Active
            });
        var usersTable = new FakeUsersTableClient(new ApplicationUser
        {
            PartitionKey = "USER",
            RowKey = "target",
            UserName = "target",
            NormalizedUserName = "TARGET",
            Email = "target@example.com",
            NormalizedEmail = "TARGET@EXAMPLE.COM",
            IsBot = false
        });
        var service = new GamePresenceService(gamesTable, usersTable, cleanupInterval: Timeout.InfiniteTimeSpan);

        service.SetMockConnectionState("game-1", "target", isConnected: false);
        service.SetMockConnectionState("game-1", "target", isConnected: true);

        await WaitForAsync(() => gamesTable.Events.Count > 0);

        var gameEvent = gamesTable.Events.Last();
        var snapshot = GameEventSerialization.DeserializeSnapshot(gameEvent.SerializedGameState);
        Assert.Equal(BotControlStatuses.Cleared, snapshot.Players[0].Control.BotControlStatus);
        Assert.Equal("Reconnect", snapshot.Players[0].Control.BotControlClearReason);
        Assert.NotNull(snapshot.Players[0].Control.BotControlClearedUtc);
    }

    [Fact]
    public async Task SetMockConnectionState_BotReconnect_DoesNotClearDedicatedBotControl()
    {
        var gamesTable = new FakeGamesTableClient(
            new GameEntity
            {
                PartitionKey = "game-1",
                RowKey = "GAME",
                GameId = "game-1",
                Seats =
                [
                    new GameSeatDefinition
                    {
                        SeatIndex = 0,
                        PlayerUserId = "beatle-bot",
                        DisplayName = "Beatle Bot",
                        Color = "#111111"
                    }
                ]
            },
            new GamePlayerStateEntity
            {
                PartitionKey = "game-1",
                RowKey = GamePlayerStateEntity.BuildRowKey(0),
                GameId = "game-1",
                SeatIndex = 0,
                PlayerUserId = "beatle-bot",
                ControllerMode = SeatControllerModes.AI,
                BotDefinitionId = "beatle-bot",
                BotControlStatus = BotControlStatuses.Active
            });
        var usersTable = new FakeUsersTableClient(new ApplicationUser
        {
            PartitionKey = "USER",
            RowKey = "beatle-bot",
            UserName = "beatle-bot",
            NormalizedUserName = "BEATLE-BOT",
            Email = "beatle-bot@example.com",
            NormalizedEmail = "BEATLE-BOT@EXAMPLE.COM",
            IsBot = true
        });
        var service = new GamePresenceService(gamesTable, usersTable, cleanupInterval: Timeout.InfiniteTimeSpan);

        service.SetMockConnectionState("game-1", "beatle-bot", isConnected: false);
        service.SetMockConnectionState("game-1", "beatle-bot", isConnected: true);

        await Task.Delay(50);

        var playerState = Assert.Single(gamesTable.PlayerStates);
        Assert.Equal(BotControlStatuses.Active, playerState.BotControlStatus);
        Assert.Null(playerState.BotControlClearedUtc);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.True(condition());
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }

        public void Advance(TimeSpan amount)
        {
            _utcNow = _utcNow.Add(amount);
        }
    }

    private sealed class FakeGamesTableClient : TableClient
    {
        public GameEntity GameEntity { get; private set; }
        public List<GamePlayerStateEntity> PlayerStates { get; }
        public List<GameEventEntity> Events { get; }

        public FakeGamesTableClient(GameEntity gameEntity, params GamePlayerStateEntity[] playerStates)
        {
            GameEntity = Clone(gameEntity);
            PlayerStates = playerStates.Select(Clone).ToList();
            Events =
            [
                BuildSnapshotEvent(GameEntity, PlayerStates)
            ];
        }

        public override Task<Response<T>> GetEntityAsync<T>(
            string partitionKey,
            string rowKey,
            IEnumerable<string>? select = null,
            CancellationToken cancellationToken = default)
        {
            if (typeof(T) == typeof(GameEntity))
            {
                if (!string.Equals(partitionKey, GameEntity.PartitionKey, StringComparison.Ordinal)
                    || !string.Equals(rowKey, GameEntity.RowKey, StringComparison.Ordinal))
                {
                    throw new RequestFailedException((int)HttpStatusCode.NotFound, "Entity was not found.");
                }

                return Task.FromResult(Response.FromValue((T)(ITableEntity)Clone(GameEntity), new FakeResponse((int)HttpStatusCode.OK)));
            }

            if (typeof(T) == typeof(GamePlayerStateEntity))
            {
                var playerState = PlayerStates.SingleOrDefault(playerState =>
                    string.Equals(playerState.PartitionKey, partitionKey, StringComparison.Ordinal)
                    && string.Equals(playerState.RowKey, rowKey, StringComparison.Ordinal));

                if (playerState is null)
                {
                    throw new RequestFailedException((int)HttpStatusCode.NotFound, "Entity was not found.");
                }

                return Task.FromResult(Response.FromValue((T)(ITableEntity)Clone(playerState), new FakeResponse((int)HttpStatusCode.OK)));
            }

            throw new NotSupportedException($"Unsupported entity type: {typeof(T).Name}");
        }

        public override AsyncPageable<T> QueryAsync<T>(
            string? filter = null,
            int? maxPerPage = null,
            IEnumerable<string>? select = null,
            CancellationToken cancellationToken = default)
        {
            if (typeof(T) == typeof(GameEventEntity))
            {
                return AsyncPageable<T>.FromPages(
                [
                    Page<T>.FromValues(Events.Select(Clone).Cast<T>().ToList(), continuationToken: null, new FakeResponse((int)HttpStatusCode.OK))
                ]);
            }

            if (typeof(T) == typeof(GamePlayerStateEntity))
            {
                var values = PlayerStates
                    .Select(Clone)
                    .Cast<T>()
                    .ToList();

                return AsyncPageable<T>.FromPages(
                [
                    Page<T>.FromValues(values, continuationToken: null, new FakeResponse((int)HttpStatusCode.OK))
                ]);
            }

            return AsyncPageable<T>.FromPages(
            [
                Page<T>.FromValues([], continuationToken: null, new FakeResponse((int)HttpStatusCode.OK))
            ]);
        }

        public override Task<Response> AddEntityAsync<T>(T entity, CancellationToken cancellationToken = default)
        {
            if (entity is GameEventEntity gameEvent)
            {
                Events.Add(Clone(gameEvent));
            }

            return Task.FromResult<Response>(new FakeResponse((int)HttpStatusCode.NoContent));
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

            playerState.BotControlClearedUtc = tableEntity.GetDateTimeOffset(nameof(GamePlayerStateEntity.BotControlClearedUtc)) ?? playerState.BotControlClearedUtc;
            playerState.BotControlStatus = tableEntity.GetString(nameof(GamePlayerStateEntity.BotControlStatus)) ?? playerState.BotControlStatus;
            playerState.BotControlClearReason = tableEntity.GetString(nameof(GamePlayerStateEntity.BotControlClearReason)) ?? playerState.BotControlClearReason;
            playerState.ETag = new ETag("\"updated\"");
            return Task.FromResult<Response>(new FakeResponse((int)HttpStatusCode.NoContent));
        }
    }

    private sealed class FakeUsersTableClient(params ApplicationUser[] entities) : TableClient
    {
        private readonly Dictionary<(string PartitionKey, string RowKey), ApplicationUser> _entities = entities.ToDictionary(
            entity => (entity.PartitionKey, entity.RowKey),
            Clone);

        public override Task<Response<T>> GetEntityAsync<T>(
            string partitionKey,
            string rowKey,
            IEnumerable<string>? select = null,
            CancellationToken cancellationToken = default)
        {
            if (!_entities.TryGetValue((partitionKey, rowKey), out var entity))
            {
                throw new RequestFailedException((int)HttpStatusCode.NotFound, "Entity was not found.");
            }

            return Task.FromResult(Response.FromValue((T)(ITableEntity)Clone(entity), new FakeResponse((int)HttpStatusCode.OK)));
        }
    }

    private static GameEntity Clone(GameEntity entity)
    {
        return new GameEntity
        {
            PartitionKey = entity.PartitionKey,
            RowKey = entity.RowKey,
            Timestamp = entity.Timestamp,
            ETag = entity.ETag,
            GameId = entity.GameId,
            Seats = entity.Seats
                .Select(seat => new GameSeatDefinition
                {
                    SeatIndex = seat.SeatIndex,
                    PlayerUserId = seat.PlayerUserId,
                    DisplayName = seat.DisplayName,
                    Color = seat.Color
                })
                .ToList()
        };
    }

    private static GamePlayerStateEntity Clone(GamePlayerStateEntity entity)
    {
        return new GamePlayerStateEntity
        {
            PartitionKey = entity.PartitionKey,
            RowKey = entity.RowKey,
            Timestamp = entity.Timestamp,
            ETag = entity.ETag,
            GameId = entity.GameId,
            SeatIndex = entity.SeatIndex,
            PlayerUserId = entity.PlayerUserId,
            DisplayName = entity.DisplayName,
            Color = entity.Color,
            ControllerMode = entity.ControllerMode,
            ControllerUserId = entity.ControllerUserId,
            BotDefinitionId = entity.BotDefinitionId,
            AuctionPlanTurnNumber = entity.AuctionPlanTurnNumber,
            AuctionPlanRailroadIndex = entity.AuctionPlanRailroadIndex,
            AuctionPlanStartingPrice = entity.AuctionPlanStartingPrice,
            AuctionPlanMaximumBid = entity.AuctionPlanMaximumBid,
            BotControlActivatedUtc = entity.BotControlActivatedUtc,
            BotControlClearedUtc = entity.BotControlClearedUtc,
            BotControlStatus = entity.BotControlStatus,
            BotControlClearReason = entity.BotControlClearReason,
            TurnsTaken = entity.TurnsTaken,
            FreightTurnCount = entity.FreightTurnCount,
            FreightRollTotal = entity.FreightRollTotal,
            ExpressTurnCount = entity.ExpressTurnCount,
            ExpressRollTotal = entity.ExpressRollTotal,
            SuperchiefTurnCount = entity.SuperchiefTurnCount,
            SuperchiefRollTotal = entity.SuperchiefRollTotal,
            BonusRollCount = entity.BonusRollCount,
            BonusRollTotal = entity.BonusRollTotal,
            TotalPayoffsCollected = entity.TotalPayoffsCollected,
            TotalFeesPaid = entity.TotalFeesPaid,
            TotalFeesCollected = entity.TotalFeesCollected,
            TotalRailroadFaceValuePurchased = entity.TotalRailroadFaceValuePurchased,
            TotalRailroadAmountPaid = entity.TotalRailroadAmountPaid,
            AuctionWins = entity.AuctionWins,
            AuctionBidsPlaced = entity.AuctionBidsPlaced,
            RailroadsPurchasedCount = entity.RailroadsPurchasedCount,
            RailroadsAuctionedCount = entity.RailroadsAuctionedCount,
            RailroadsSoldToBankCount = entity.RailroadsSoldToBankCount
        };
    }

    private static ApplicationUser Clone(ApplicationUser entity)
    {
        return new ApplicationUser
        {
            PartitionKey = entity.PartitionKey,
            RowKey = entity.RowKey,
            Timestamp = entity.Timestamp,
            ETag = entity.ETag,
            Name = entity.Name,
            Email = entity.Email,
            NormalizedEmail = entity.NormalizedEmail,
            UserName = entity.UserName,
            NormalizedUserName = entity.NormalizedUserName,
            Nickname = entity.Nickname,
            NormalizedNickname = entity.NormalizedNickname,
            StrategyText = entity.StrategyText,
            IsBot = entity.IsBot,
            CreatedByUserId = entity.CreatedByUserId,
            CreatedUtc = entity.CreatedUtc,
            ModifiedByUserId = entity.ModifiedByUserId,
            ModifiedUtc = entity.ModifiedUtc
        };
    }

    private static GameEventEntity Clone(GameEventEntity entity)
    {
        return new GameEventEntity
        {
            PartitionKey = entity.PartitionKey,
            RowKey = entity.RowKey,
            Timestamp = entity.Timestamp,
            ETag = entity.ETag,
            GameId = entity.GameId,
            EventKind = entity.EventKind,
            EventData = entity.EventData,
            PreviewRouteNodeIdsJson = entity.PreviewRouteNodeIdsJson,
            PreviewRouteSegmentKeysJson = entity.PreviewRouteSegmentKeysJson,
            SerializedGameState = entity.SerializedGameState,
            OccurredUtc = entity.OccurredUtc,
            CreatedBy = entity.CreatedBy,
            ActingUserId = entity.ActingUserId,
            ActingPlayerIndex = entity.ActingPlayerIndex,
            ChangeSummary = entity.ChangeSummary
        };
    }

    private static GameEventEntity BuildSnapshotEvent(GameEntity gameEntity, IReadOnlyList<GamePlayerStateEntity> playerStates)
    {
        var snapshot = new global::Boxcars.Engine.Persistence.GameState
        {
            Players = playerStates
                .OrderBy(playerState => playerState.SeatIndex)
                .Select(playerState => new global::Boxcars.Engine.Persistence.PlayerState
                {
                    Name = string.IsNullOrWhiteSpace(playerState.DisplayName) ? playerState.PlayerUserId : playerState.DisplayName,
                    Control = GameSeatStateProjection.CloneControl(playerState.Control)
                })
                .ToList()
        };

        return new GameEventEntity
        {
            PartitionKey = gameEntity.GameId,
            RowKey = "Event_0000000001",
            GameId = gameEntity.GameId,
            EventKind = "CreateGame",
            EventData = "{}",
            PreviewRouteNodeIdsJson = "[]",
            PreviewRouteSegmentKeysJson = "[]",
            SerializedGameState = GameEventSerialization.SerializeSnapshot(snapshot),
            OccurredUtc = DateTimeOffset.UtcNow,
            CreatedBy = string.Empty,
            ActingUserId = string.Empty,
            ActingPlayerIndex = null,
            ChangeSummary = "Initial snapshot"
        };
    }

    private sealed class FakeResponse(int status) : Response
    {
        public override int Status { get; } = status;
        public override string ReasonPhrase => string.Empty;
        public override Stream? ContentStream { get; set; }
        public override string ClientRequestId { get; set; } = string.Empty;
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
