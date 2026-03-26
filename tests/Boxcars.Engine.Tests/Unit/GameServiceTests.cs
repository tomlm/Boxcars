using System.Net;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Boxcars.Data;
using Boxcars.GameEngine;
using Boxcars.Hubs;
using Boxcars.Services;
using Microsoft.AspNetCore.SignalR;

namespace Boxcars.Engine.Tests.Unit;

public class GameServiceTests
{
    [Fact]
    public async Task UpdateSeatStatesAsync_ControlChange_ProjectsUpdatedSeatState()
    {
        var gameEntity = new GameEntity
        {
            PartitionKey = "game-1",
            RowKey = "GAME",
            GameId = "game-1",
            ETag = new ETag("\"game\""),
            Seats =
            [
                new GameSeatDefinition
                {
                    SeatIndex = 0,
                    PlayerUserId = "alice@example.com",
                    DisplayName = "Alice",
                    Color = "red"
                }
            ]
        };
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
        var gamesTable = new FakeGamesTableClient(gameEntity, persistedPlayerState);
        var service = new GameService(gamesTable, new FakeUsersTableClient(), new FakeHubContext(), new FakeGameEngine());

        var updatedPlayerState = GamePlayerStateProjection.Clone(persistedPlayerState);
        updatedPlayerState.ControllerMode = SeatControllerModes.AI;
        updatedPlayerState.BotControlStatus = BotControlStatuses.Active;
        updatedPlayerState.BotControlActivatedUtc = new DateTimeOffset(2026, 3, 23, 0, 0, 0, TimeSpan.Zero);

        var result = await service.UpdateSeatStatesAsync("game-1", [updatedPlayerState], CancellationToken.None);

        Assert.True(result.Succeeded);

        var seatState = Assert.Single(result.PlayerStates);
        Assert.Equal(SeatControllerModes.AI, seatState.ControllerMode);
        Assert.Equal(BotControlStatuses.Active, seatState.BotControlStatus);
        Assert.Equal(updatedPlayerState.BotControlActivatedUtc, seatState.BotControlActivatedUtc);
    }

    private sealed class FakeGamesTableClient(GameEntity gameEntity, params GamePlayerStateEntity[] playerStates) : TableClient
    {
        public GameEntity GameEntity { get; private set; } = Clone(gameEntity);
        public List<GamePlayerStateEntity> PlayerStates { get; } = playerStates.Select(Clone).ToList();

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
            playerState.AuctionPlanTurnNumber = tableEntity.GetInt32(nameof(GamePlayerStateEntity.AuctionPlanTurnNumber)) ?? playerState.AuctionPlanTurnNumber;
            playerState.AuctionPlanRailroadIndex = tableEntity.GetInt32(nameof(GamePlayerStateEntity.AuctionPlanRailroadIndex)) ?? playerState.AuctionPlanRailroadIndex;
            playerState.AuctionPlanStartingPrice = tableEntity.GetInt32(nameof(GamePlayerStateEntity.AuctionPlanStartingPrice)) ?? playerState.AuctionPlanStartingPrice;
            playerState.AuctionPlanMaximumBid = tableEntity.GetInt32(nameof(GamePlayerStateEntity.AuctionPlanMaximumBid)) ?? playerState.AuctionPlanMaximumBid;
            playerState.BotControlActivatedUtc = tableEntity.GetDateTimeOffset(nameof(GamePlayerStateEntity.BotControlActivatedUtc)) ?? playerState.BotControlActivatedUtc;
            playerState.BotControlClearedUtc = tableEntity.GetDateTimeOffset(nameof(GamePlayerStateEntity.BotControlClearedUtc)) ?? playerState.BotControlClearedUtc;
            playerState.BotControlStatus = tableEntity.GetString(nameof(GamePlayerStateEntity.BotControlStatus)) ?? playerState.BotControlStatus;
            playerState.BotControlClearReason = tableEntity.GetString(nameof(GamePlayerStateEntity.BotControlClearReason)) ?? playerState.BotControlClearReason;
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

        public override Task<Response> AddEntityAsync<T>(T entity, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Response>(new FakeResponse((int)HttpStatusCode.NoContent));
        }
    }

    private sealed class FakeUsersTableClient : TableClient;

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
            return Task.FromResult(new global::Boxcars.Engine.Persistence.GameState
            {
                Players =
                [
                    new global::Boxcars.Engine.Persistence.PlayerState
                    {
                        Name = "Alice"
                    }
                ]
            });
        }

        public Task StartGameAsync(string gameId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task SynchronizeStateAsync(string gameId, global::Boxcars.Engine.Persistence.GameState state, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public bool IsGameBusy(string gameId)
        {
            throw new NotSupportedException();
        }

        public ValueTask EnqueueActionAsync(string gameId, PlayerAction action, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<bool> UndoLastOperationAsync(string gameId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<bool> UndoToEventAsync(string gameId, string targetEventRowKey, string targetDescription, string actorUserId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeHubContext : IHubContext<DashboardHub>
    {
        public IHubClients Clients { get; } = new FakeHubClients();
        public IGroupManager Groups { get; } = new FakeGroupManager();
    }

    private sealed class FakeHubClients : IHubClients
    {
        private static readonly IClientProxy Proxy = new FakeClientProxy();

        public IClientProxy All => Proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Client(string connectionId) => Proxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Proxy;
        public IClientProxy Group(string groupName) => Proxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => Proxy;
        public IClientProxy User(string userId) => Proxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => Proxy;
    }

    private sealed class FakeClientProxy : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
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
            Name = entity.Name,
            GameDate = entity.GameDate,
            State = entity.State,
            MapFileName = entity.MapFileName,
            MaxPlayers = entity.MaxPlayers,
            CurrentPlayerCount = entity.CurrentPlayerCount,
            CreatedAt = entity.CreatedAt,
            StartedAt = entity.StartedAt,
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
            SettingsSchemaVersion = entity.SettingsSchemaVersion,
            SeatsJson = entity.SeatsJson
        };
    }

    private static GamePlayerStateEntity Clone(GamePlayerStateEntity entity)
    {
        return GamePlayerStateProjection.Clone(entity);
    }
}
