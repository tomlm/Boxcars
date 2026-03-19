using Boxcars.Data;
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

        var controllerState = service.ResolveSeatControllerState("game-1", "target", activeBotAssignment: null);

        Assert.Equal(SeatControllerModes.Delegated, controllerState.ControllerMode);
        Assert.Equal("controller", controllerState.DelegatedControllerUserId);
        Assert.False(controllerState.IsConnected);
    }

    [Fact]
    public void ResolveSeatControllerState_DisconnectedHumanWithoutManualController_DefaultsToBot()
    {
        var service = new GamePresenceService();

        service.SetMockConnectionState("game-1", "target", isConnected: false);

        var controllerState = service.ResolveSeatControllerState("game-1", "target", activeBotAssignment: null);

        Assert.Equal(SeatControllerModes.AI, controllerState.ControllerMode);
        Assert.Equal("target", controllerState.BotDefinitionId);
        Assert.False(controllerState.IsConnected);
    }

    [Fact]
    public void ResolveSeatControllerState_BotAssignment_ReturnsBot()
    {
        var service = new GamePresenceService();

        service.SetMockConnectionState("game-1", "target", isConnected: false);

        var controllerState = service.ResolveSeatControllerState(
            "game-1",
            "target",
            new BotAssignment
            {
                GameId = "game-1",
                PlayerUserId = "target",
                ControllerUserId = string.Empty,
                ControllerMode = SeatControllerModes.AI,
                BotDefinitionId = "bot-1",
                Status = BotAssignmentStatuses.Active
            });

        Assert.Equal(SeatControllerModes.AI, controllerState.ControllerMode);
        Assert.Null(controllerState.DelegatedControllerUserId);
        Assert.Equal("bot-1", controllerState.BotDefinitionId);
    }

    [Fact]
    public void ResolveSeatControllerState_DedicatedBotAssignment_ReturnsBot()
    {
        var service = new GamePresenceService();

        var controllerState = service.ResolveSeatControllerState(
            "game-1",
            "beatle-bot",
            new BotAssignment
            {
                GameId = "game-1",
                PlayerUserId = "beatle-bot",
                ControllerMode = SeatControllerModes.AI,
                BotDefinitionId = "bot-1",
                Status = BotAssignmentStatuses.Active
            });

        Assert.Equal(SeatControllerModes.AI, controllerState.ControllerMode);
        Assert.Equal("bot-1", controllerState.BotDefinitionId);
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
            new BotAssignment
            {
                GameId = "game-1",
                PlayerUserId = "target",
                ControllerUserId = "controller",
                ControllerMode = SeatControllerModes.AI,
                BotDefinitionId = "bot-1",
                Status = BotAssignmentStatuses.Active
            });

        Assert.Equal(SeatControllerModes.AI, controllerState.ControllerMode);
        Assert.Null(controllerState.DelegatedControllerUserId);
    }

    [Fact]
    public void Reconnect_BotAssignmentFallsBackToSelf()
    {
        var service = new GamePresenceService();

        service.SetMockConnectionState("game-1", "controller", isConnected: true);
        service.SetMockConnectionState("game-1", "target", isConnected: false);
        service.TryTakeDelegatedControl("game-1", "target", "controller");
        service.SetMockConnectionState("game-1", "target", isConnected: true);

        var controllerState = service.ResolveSeatControllerState(
            "game-1",
            "target",
            new BotAssignment
            {
                GameId = "game-1",
                PlayerUserId = "target",
                ControllerUserId = "controller",
                ControllerMode = SeatControllerModes.AI,
                BotDefinitionId = "bot-1",
                Status = BotAssignmentStatuses.Active
            });

        Assert.Equal(SeatControllerModes.Self, controllerState.ControllerMode);
        Assert.True(controllerState.IsConnected);
    }

    [Fact]
    public async Task SetMockConnectionState_HumanReconnect_ClearsPersistedAiAssignment()
    {
        var gamesTable = new FakeGamesTableClient(new GameEntity
        {
            PartitionKey = "game-1",
            RowKey = "GAME",
            GameId = "game-1",
            BotAssignmentsJson = BotAssignmentSerialization.Serialize(
            [
                new BotAssignment
                {
                    GameId = "game-1",
                    PlayerUserId = "target",
                    ControllerMode = SeatControllerModes.AI,
                    BotDefinitionId = "target",
                    Status = BotAssignmentStatuses.Active
                }
            ])
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

        await WaitForAsync(() =>
            BotAssignmentSerialization.Deserialize(gamesTable.GameEntity.BotAssignmentsJson)
                .Any(assignment => string.Equals(assignment.PlayerUserId, "target", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(assignment.Status, BotAssignmentStatuses.Cleared, StringComparison.OrdinalIgnoreCase)));

        var assignment = Assert.Single(BotAssignmentSerialization.Deserialize(gamesTable.GameEntity.BotAssignmentsJson));
        Assert.Equal(BotAssignmentStatuses.Cleared, assignment.Status);
        Assert.Equal("Reconnect", assignment.ClearReason);
        Assert.NotNull(assignment.ClearedUtc);
    }

    [Fact]
    public async Task SetMockConnectionState_BotReconnect_DoesNotClearDedicatedBotAssignment()
    {
        var gamesTable = new FakeGamesTableClient(new GameEntity
        {
            PartitionKey = "game-1",
            RowKey = "GAME",
            GameId = "game-1",
            BotAssignmentsJson = BotAssignmentSerialization.Serialize(
            [
                new BotAssignment
                {
                    GameId = "game-1",
                    PlayerUserId = "beatle-bot",
                    ControllerMode = SeatControllerModes.AI,
                    BotDefinitionId = "beatle-bot",
                    Status = BotAssignmentStatuses.Active
                }
            ])
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

        var assignment = Assert.Single(BotAssignmentSerialization.Deserialize(gamesTable.GameEntity.BotAssignmentsJson));
        Assert.Equal(BotAssignmentStatuses.Active, assignment.Status);
        Assert.Null(assignment.ClearedUtc);
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

    private sealed class FakeGamesTableClient(GameEntity gameEntity) : TableClient
    {
        public GameEntity GameEntity { get; private set; } = Clone(gameEntity);

        public override Task<Response<T>> GetEntityAsync<T>(
            string partitionKey,
            string rowKey,
            IEnumerable<string>? select = null,
            CancellationToken cancellationToken = default)
        {
            if (!string.Equals(partitionKey, GameEntity.PartitionKey, StringComparison.Ordinal)
                || !string.Equals(rowKey, GameEntity.RowKey, StringComparison.Ordinal))
            {
                throw new RequestFailedException((int)HttpStatusCode.NotFound, "Entity was not found.");
            }

            return Task.FromResult(Response.FromValue((T)(ITableEntity)Clone(GameEntity), new FakeResponse((int)HttpStatusCode.OK)));
        }

        public override Task<Response> UpdateEntityAsync<T>(
            T entity,
            ETag ifMatch,
            TableUpdateMode mode = TableUpdateMode.Merge,
            CancellationToken cancellationToken = default)
        {
            var tableEntity = entity as TableEntity ?? throw new InvalidOperationException("Expected table entity.");
            GameEntity.BotAssignmentsJson = tableEntity.GetString(nameof(GameEntity.BotAssignmentsJson)) ?? GameEntity.BotAssignmentsJson;
            GameEntity.ETag = new ETag("\"updated\"");
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
            BotAssignmentsJson = entity.BotAssignmentsJson
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