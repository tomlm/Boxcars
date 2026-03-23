using System.Net;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Boxcars.Data;
using Boxcars.Engine.Domain;
using Boxcars.Engine.Tests;
using Boxcars.GameEngine;
using Boxcars.Hubs;
using Boxcars.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Boxcars.Engine.Tests.Unit;

public class GameSettingsImmutabilityTests
{
    [Fact]
    public async Task UpdatePlayerStatesAsync_PostStartSeatUpdate_PreservesPersistedGameSettings()
    {
        var settings = GameSettingsTestData.Create(
            startingCash: 35_000,
            announcingCash: 150_000,
            winningCash: 325_000,
            keepCashSecret: false,
            startEngine: LocomotiveType.Express,
            superchiefPrice: 45_000,
            expressPrice: 6_000);
        var gameEntity = GameSettingsTestData.CreatePersistedGameEntity("game-1", settings);
        gameEntity.ETag = new ETag("\"game\"");

        var persistedPlayerState = new GamePlayerStateEntity
        {
            PartitionKey = "game-1",
            RowKey = GamePlayerStateEntity.BuildRowKey(0),
            GameId = "game-1",
            SeatIndex = 0,
            PlayerUserId = "alice@example.com",
            DisplayName = "Alice",
            Color = "red",
            ETag = new ETag("\"seat-0\"")
        };

        var gamesTable = new ImmutableSettingsGamesTableClient(gameEntity, persistedPlayerState);
        var service = new GameService(gamesTable, new FakeUsersTableClient(), new FakeHubContext(), new FakeGameEngine());

        var updatedPlayerState = GamePlayerStateProjection.Clone(persistedPlayerState);
        updatedPlayerState.ControllerMode = SeatControllerModes.AI;
        updatedPlayerState.BotDefinitionId = "bot-1";

        var result = await service.UpdatePlayerStatesAsync("game-1", [updatedPlayerState], CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(35_000, gamesTable.GameEntity.StartingCash);
        Assert.Equal(150_000, gamesTable.GameEntity.AnnouncingCash);
        Assert.Equal(325_000, gamesTable.GameEntity.WinningCash);
        Assert.False(gamesTable.GameEntity.KeepCashSecret);
        Assert.Equal("Express", gamesTable.GameEntity.StartEngine);
        Assert.Equal(45_000, gamesTable.GameEntity.SuperchiefPrice);
        Assert.Equal(6_000, gamesTable.GameEntity.ExpressPrice);
    }

    [Fact]
    public async Task SynchronizeStateAsync_StartedGame_DoesNotRewritePersistedGameSettings()
    {
        var settings = GameSettingsTestData.Create(
            startingCash: 35_000,
            announcingCash: 150_000,
            winningCash: 325_000,
            publicFee: 1_500,
            privateFee: 500,
            keepCashSecret: false,
            startEngine: LocomotiveType.Express,
            superchiefPrice: 45_000,
            expressPrice: 6_000);
        var gameEntity = GameSettingsTestData.CreatePersistedGameEntity("game-1", settings);
        var gamesTable = new EngineImmutableSettingsGamesTableClient(gameEntity);
        var presenceService = new GamePresenceService();
        var service = new GameEngineService(
            new TestWebHostEnvironment(),
            gamesTable,
            presenceService,
            BotTurnServiceTestHarness.CreateService(presenceService),
            new GameSettingsResolver(),
            Options.Create(new BotOptions()),
            NullLogger<GameEngineService>.Instance);

        SetMapReady(service);
        SetMapDefinition(service, Boxcars.Engine.Tests.Fixtures.GameEngineFixture.CreateTestMap());

        var (engine, _) = Boxcars.Engine.Tests.Fixtures.GameEngineFixture.CreateTestEngine();

        await service.SynchronizeStateAsync("game-1", engine.ToSnapshot(), CancellationToken.None);

        Assert.Equal(0, gamesTable.GameEntityUpdateCount);
        Assert.Equal(0, gamesTable.GameEntityAddCount);
        Assert.Equal(35_000, gamesTable.GameEntity.StartingCash);
        Assert.Equal(150_000, gamesTable.GameEntity.AnnouncingCash);
        Assert.Equal(325_000, gamesTable.GameEntity.WinningCash);
        Assert.Equal(1_500, gamesTable.GameEntity.PublicFee);
        Assert.Equal(500, gamesTable.GameEntity.PrivateFee);
        Assert.False(gamesTable.GameEntity.KeepCashSecret);
        Assert.Equal("Express", gamesTable.GameEntity.StartEngine);
    }

    private static void SetMapDefinition(GameEngineService service, Boxcars.Engine.Data.Maps.MapDefinition mapDefinition)
    {
        var field = typeof(GameEngineService).GetField("_mapDefinition", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?? throw new InvalidOperationException("_mapDefinition field was not found.");
        field.SetValue(service, mapDefinition);
    }

    private static void SetMapReady(GameEngineService service)
    {
        var field = typeof(GameEngineService).GetField("_mapReady", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?? throw new InvalidOperationException("_mapReady field was not found.");
        var source = (TaskCompletionSource)(field.GetValue(service) ?? throw new InvalidOperationException("_mapReady was null."));
        source.TrySetResult();
    }

    private sealed class ImmutableSettingsGamesTableClient(GameEntity gameEntity, params GamePlayerStateEntity[] playerStates) : TableClient
    {
        public GameEntity GameEntity { get; } = Clone(gameEntity);
        public List<GamePlayerStateEntity> PlayerStates { get; } = playerStates.Select(Clone).ToList();

        public override Task<Response<T>> GetEntityAsync<T>(
            string partitionKey,
            string rowKey,
            IEnumerable<string>? select = null,
            CancellationToken cancellationToken = default)
        {
            if (typeof(T) == typeof(GameEntity))
            {
                return Task.FromResult(Response.FromValue((T)(ITableEntity)Clone(GameEntity), new FakeResponse((int)HttpStatusCode.OK)));
            }

            if (typeof(T) == typeof(GamePlayerStateEntity))
            {
                var playerState = PlayerStates.Single(existing =>
                    string.Equals(existing.PartitionKey, partitionKey, StringComparison.Ordinal)
                    && string.Equals(existing.RowKey, rowKey, StringComparison.Ordinal));
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
            if (typeof(T) == typeof(GamePlayerStateEntity))
            {
                return AsyncPageable<T>.FromPages(
                [
                    Page<T>.FromValues(PlayerStates.Select(Clone).Cast<T>().ToList(), null, new FakeResponse((int)HttpStatusCode.OK))
                ]);
            }

            return AsyncPageable<T>.FromPages([Page<T>.FromValues([], null, new FakeResponse((int)HttpStatusCode.OK))]);
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

            playerState.ControllerMode = tableEntity.GetString(nameof(GamePlayerStateEntity.ControllerMode)) ?? playerState.ControllerMode;
            playerState.ControllerUserId = tableEntity.GetString(nameof(GamePlayerStateEntity.ControllerUserId)) ?? playerState.ControllerUserId;
            playerState.BotDefinitionId = tableEntity.GetString(nameof(GamePlayerStateEntity.BotDefinitionId)) ?? playerState.BotDefinitionId;
            playerState.ETag = new ETag("\"updated\"");
            return Task.FromResult<Response>(new FakeResponse((int)HttpStatusCode.NoContent));
        }
    }

    private sealed class EngineImmutableSettingsGamesTableClient(GameEntity gameEntity) : TableClient
    {
        public GameEntity GameEntity { get; } = Clone(gameEntity);
        public int GameEntityAddCount { get; private set; }
        public int GameEntityUpdateCount { get; private set; }

        public override Task<Response<T>> GetEntityAsync<T>(
            string partitionKey,
            string rowKey,
            IEnumerable<string>? select = null,
            CancellationToken cancellationToken = default)
        {
            if (typeof(T) != typeof(GameEntity))
            {
                throw new NotSupportedException($"Unsupported entity type: {typeof(T).Name}");
            }

            return Task.FromResult(Response.FromValue((T)(ITableEntity)Clone(GameEntity), new FakeResponse((int)HttpStatusCode.OK)));
        }

        public override Task<Response> AddEntityAsync<T>(T entity, CancellationToken cancellationToken = default)
        {
            if (entity is GameEntity)
            {
                GameEntityAddCount++;
            }

            return Task.FromResult<Response>(new FakeResponse((int)HttpStatusCode.NoContent));
        }

        public override Task<Response> UpdateEntityAsync<T>(
            T entity,
            ETag ifMatch,
            TableUpdateMode mode = TableUpdateMode.Merge,
            CancellationToken cancellationToken = default)
        {
            if (entity is GameEntity)
            {
                GameEntityUpdateCount++;
            }

            if (entity is TableEntity tableEntity && string.Equals(tableEntity.RowKey, "GAME", StringComparison.Ordinal))
            {
                GameEntityUpdateCount++;
            }

            return Task.FromResult<Response>(new FakeResponse((int)HttpStatusCode.NoContent));
        }
    }

    private sealed class FakeUsersTableClient : TableClient;

    private sealed class FakeHubContext : IHubContext<DashboardHub>
    {
        public IHubClients Clients { get; } = new FakeHubClients();
        public IGroupManager Groups => throw new NotSupportedException();

        private sealed class FakeHubClients : IHubClients
        {
            public IClientProxy All => new FakeClientProxy();
            public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => All;
            public IClientProxy Client(string connectionId) => All;
            public IClientProxy Clients(IReadOnlyList<string> connectionIds) => All;
            public IClientProxy Group(string groupName) => All;
            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => All;
            public IClientProxy Groups(IReadOnlyList<string> groupNames) => All;
            public IClientProxy User(string userId) => All;
            public IClientProxy Users(IReadOnlyList<string> userIds) => All;
        }

        private sealed class FakeClientProxy : IClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) => Task.CompletedTask;
        }
    }

    private sealed class FakeGameEngine : IGameEngine
    {
        public event Action<string, GameStateUpdate>? OnStateChanged
        {
            add { }
            remove { }
        }

        public event Action<string, GameActionFailure>? OnActionFailed
        {
            add { }
            remove { }
        }

        public Task<string> CreateGameAsync(CreateGameRequest request, GameCreationOptions? options = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<global::Boxcars.Engine.Persistence.GameState> GetCurrentStateAsync(string gameId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task SynchronizeStateAsync(string gameId, global::Boxcars.Engine.Persistence.GameState state, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public bool IsGameBusy(string gameId) => false;

        public ValueTask EnqueueActionAsync(string gameId, PlayerAction action, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<bool> UndoLastOperationAsync(string gameId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeResponse(int status) : Response
    {
        public override int Status => status;
        public override string ReasonPhrase => string.Empty;
        public override Stream? ContentStream { get; set; }
        public override string ClientRequestId { get; set; } = Guid.NewGuid().ToString("N");
        public override void Dispose() { }
        protected override bool ContainsHeader(string name) => false;
        protected override IEnumerable<HttpHeader> EnumerateHeaders() { yield break; }
        protected override bool TryGetHeader(string name, out string value) { value = string.Empty; return false; }
        protected override bool TryGetHeaderValues(string name, out IEnumerable<string> values) { values = []; return false; }
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

    private static GameEntity Clone(GameEntity entity)
    {
        return new GameEntity
        {
            PartitionKey = entity.PartitionKey,
            RowKey = entity.RowKey,
            Timestamp = entity.Timestamp,
            ETag = entity.ETag,
            GameId = entity.GameId,
            CreatorId = entity.CreatorId,
            MapFileName = entity.MapFileName,
            MaxPlayers = entity.MaxPlayers,
            CurrentPlayerCount = entity.CurrentPlayerCount,
            CreatedAt = entity.CreatedAt,
            StartingCash = entity.StartingCash,
            AnnouncingCash = entity.AnnouncingCash,
            WinningCash = entity.WinningCash,
            RoverCash = entity.RoverCash,
            PublicFee = entity.PublicFee,
            PrivateFee = entity.PrivateFee,
            UnfriendlyFee1 = entity.UnfriendlyFee1,
            UnfriendlyFee2 = entity.UnfriendlyFee2,
            HomeSwapping = entity.HomeSwapping,
            HomeCityChoice = entity.HomeCityChoice,
            KeepCashSecret = entity.KeepCashSecret,
            StartEngine = entity.StartEngine,
            SuperchiefPrice = entity.SuperchiefPrice,
            ExpressPrice = entity.ExpressPrice,
            SettingsSchemaVersion = entity.SettingsSchemaVersion
        };
    }

    private static GamePlayerStateEntity Clone(GamePlayerStateEntity entity)
    {
        return GamePlayerStateProjection.Clone(entity);
    }
}