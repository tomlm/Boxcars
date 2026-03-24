using Azure;
using Azure.Data.Tables;
using Boxcars.Data;
using Boxcars.Engine.Persistence;
using Boxcars.GameEngine;
using Boxcars.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Boxcars.Services;

public sealed class GamePresenceService : IDisposable
{
    private readonly object _sync = new();
    private TableClient? _gamesTable;
    private TableClient? _usersTable;
    private TimeProvider _timeProvider = TimeProvider.System;
    private TimeSpan _connectionExpirationWindow = TimeSpan.FromSeconds(15);
    private Timer? _staleConnectionTimer;
    private readonly Dictionary<string, Dictionary<string, Dictionary<string, DateTimeOffset>>> _connectionsByGameAndUser = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, bool>> _mockPresenceByGameAndUser = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, string>> _delegatedControllersByGameAndUser = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string GameId, string UserId)> _connectionLookup = new(StringComparer.Ordinal);
    private const string EventRowKeyPrefix = "Event_";
    private const string EventRowKeyExclusiveUpperBound = "Event`";

    [ActivatorUtilitiesConstructor]
    public GamePresenceService(TableServiceClient tableServiceClient)
    {
        Initialize(
            tableServiceClient?.GetTableClient(TableNames.GamesTable),
            tableServiceClient?.GetTableClient(TableNames.UsersTable),
            TimeProvider.System,
            connectionExpirationWindow: null,
            cleanupInterval: null);
    }

    public GamePresenceService()
    {
        Initialize(null, null, TimeProvider.System, connectionExpirationWindow: null, cleanupInterval: null);
    }

    public GamePresenceService(TimeProvider timeProvider, TimeSpan? connectionExpirationWindow = null, TimeSpan? cleanupInterval = null)
    {
        Initialize(null, null, timeProvider, connectionExpirationWindow, cleanupInterval);
    }

    public GamePresenceService(
        TableClient gamesTable,
        TableClient usersTable,
        TimeProvider? timeProvider = null,
        TimeSpan? connectionExpirationWindow = null,
        TimeSpan? cleanupInterval = null)
    {
        Initialize(gamesTable, usersTable, timeProvider ?? TimeProvider.System, connectionExpirationWindow, cleanupInterval);
    }

    private void Initialize(
        TableClient? gamesTable,
        TableClient? usersTable,
        TimeProvider timeProvider,
        TimeSpan? connectionExpirationWindow = null,
        TimeSpan? cleanupInterval = null)
    {
        _gamesTable = gamesTable;
        _usersTable = usersTable;
        _timeProvider = timeProvider;
        _connectionExpirationWindow = connectionExpirationWindow ?? TimeSpan.FromSeconds(15);

        var effectiveCleanupInterval = cleanupInterval ?? TimeSpan.FromSeconds(5);
        if (effectiveCleanupInterval > TimeSpan.Zero)
        {
            _staleConnectionTimer = new Timer(
                static state => ((GamePresenceService)state!).PruneStaleConnectionsAndNotify(),
                this,
                effectiveCleanupInterval,
                effectiveCleanupInterval);
        }
    }

    public event Action<GamePresenceChange>? PresenceChanged;

    public bool AddConnection(string gameId, string userId, string connectionId)
    {
        return AddOrRefreshConnection(gameId, userId, connectionId);
    }

    public bool RefreshConnection(string gameId, string userId, string connectionId)
    {
        return AddOrRefreshConnection(gameId, userId, connectionId);
    }

    public IReadOnlyList<string> PruneStaleConnections()
    {
        List<string> changedGameIds;

        lock (_sync)
        {
            changedGameIds = PruneStaleConnectionsCore(_timeProvider.GetUtcNow());
        }

        foreach (var changedGameId in changedGameIds)
        {
            NotifyPresenceChanged(changedGameId);
        }

        return changedGameIds;
    }

    public bool RemoveConnection(string gameId, string userId, string connectionId)
    {
        var changed = false;

        lock (_sync)
        {
            changed = RemoveConnectionCore(gameId, userId, connectionId);
        }

        if (changed)
        {
            NotifyPresenceChanged(gameId);
        }

        return changed;
    }

    public IReadOnlyList<(string GameId, string UserId)> RemoveConnection(string connectionId)
    {
        List<(string GameId, string UserId)> disconnectedEntries = [];

        lock (_sync)
        {
            if (!_connectionLookup.TryGetValue(connectionId, out var existing))
            {
                return [];
            }

            var changed = RemoveConnectionCore(existing.GameId, existing.UserId, connectionId);
            disconnectedEntries = changed ? [(existing.GameId, existing.UserId)] : [];
        }

        foreach (var (gameId, _) in disconnectedEntries)
        {
            NotifyPresenceChanged(gameId);
        }

        return disconnectedEntries;
    }

    public bool IsUserConnected(string gameId, string? userId)
    {
        if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        List<string>? changedGameIds;
        bool isConnected;

        lock (_sync)
        {
            changedGameIds = PruneStaleConnectionsCore(_timeProvider.GetUtcNow());

            var hasLiveConnections = _connectionsByGameAndUser.TryGetValue(gameId, out var users)
                && users.TryGetValue(userId, out var connections)
                && connections.Count > 0;

            if (hasLiveConnections)
            {
                isConnected = true;
            }
            else
            {
                isConnected = _mockPresenceByGameAndUser.TryGetValue(gameId, out var mockUsers)
                    && mockUsers.TryGetValue(userId, out var mockConnected)
                    && mockConnected;
            }
        }

        foreach (var changedGameId in changedGameIds)
        {
            NotifyPresenceChanged(changedGameId);
        }

        return isConnected;
    }

    public bool HasAnyConnectedUsers(string gameId, IEnumerable<string?> userIds)
    {
        ArgumentNullException.ThrowIfNull(userIds);

        if (string.IsNullOrWhiteSpace(gameId))
        {
            return false;
        }

        List<string>? changedGameIds;
        var hasConnectedUser = false;

        lock (_sync)
        {
            changedGameIds = PruneStaleConnectionsCore(_timeProvider.GetUtcNow());

            foreach (var userId in userIds)
            {
                if (string.IsNullOrWhiteSpace(userId))
                {
                    continue;
                }

                if (IsUserConnectedCore(gameId, userId))
                {
                    hasConnectedUser = true;
                    break;
                }
            }
        }

        foreach (var changedGameId in changedGameIds)
        {
            NotifyPresenceChanged(changedGameId);
        }

        return hasConnectedUser;
    }

    public bool EnsureMockConnectionState(string gameId, string userId, bool defaultConnected)
    {
        var changed = false;

        lock (_sync)
        {
            if (!_mockPresenceByGameAndUser.TryGetValue(gameId, out var mockUsers))
            {
                mockUsers = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                _mockPresenceByGameAndUser[gameId] = mockUsers;
            }

            if (!mockUsers.ContainsKey(userId))
            {
                mockUsers[userId] = defaultConnected;
                changed = true;
            }
        }

        if (changed)
        {
            NotifyPresenceChanged(gameId);
        }

        return changed;
    }

    public bool SetMockConnectionState(string gameId, string userId, bool isConnected)
    {
        var changed = false;

        lock (_sync)
        {
            if (!_mockPresenceByGameAndUser.TryGetValue(gameId, out var mockUsers))
            {
                mockUsers = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                _mockPresenceByGameAndUser[gameId] = mockUsers;
            }

            if (!mockUsers.TryGetValue(userId, out var existingState) || existingState != isConnected)
            {
                mockUsers[userId] = isConnected;
                changed = true;

                if (isConnected)
                {
                    changed = ClearIncomingDelegatedControlCore(gameId, userId) || changed;
                }
            }
        }

        if (changed)
        {
            if (isConnected)
            {
                QueueBotControlClear(gameId, userId, "Reconnect");
            }

            NotifyPresenceChanged(gameId);
        }

        return changed;
    }

    public string? GetDelegatedControllerUserId(string gameId, string? slotUserId)
    {
        if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(slotUserId))
        {
            return null;
        }

        lock (_sync)
        {
            return _delegatedControllersByGameAndUser.TryGetValue(gameId, out var controllers)
                && controllers.TryGetValue(slotUserId, out var controllerUserId)
                    ? controllerUserId
                    : null;
        }
    }

    public SeatControllerState ResolveSeatControllerState(string gameId, string? slotUserId, PlayerControlState? activePlayerControl)
    {
        return PlayerControlRules.ResolveSeatControllerState(
            gameId,
            slotUserId,
            IsUserConnected(gameId, slotUserId),
            GetDelegatedControllerUserId(gameId, slotUserId),
            activePlayerControl);
    }

    public SeatControllerState ResolveSeatControllerState(string gameId, string? slotUserId, GameSeatState? activeSeatState)
    {
        return ResolveSeatControllerState(gameId, slotUserId, activeSeatState?.Control);
    }

    public bool TryTakeDelegatedControl(string gameId, string slotUserId, string controllerUserId)
    {
        if (string.IsNullOrWhiteSpace(gameId)
            || string.IsNullOrWhiteSpace(slotUserId)
            || string.IsNullOrWhiteSpace(controllerUserId)
            || string.Equals(slotUserId, controllerUserId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var changed = false;

        lock (_sync)
        {
            if (IsUserConnectedCore(gameId, slotUserId))
            {
                return false;
            }

            if (!_delegatedControllersByGameAndUser.TryGetValue(gameId, out var controllers))
            {
                controllers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _delegatedControllersByGameAndUser[gameId] = controllers;
            }

            if (!controllers.TryGetValue(slotUserId, out var existingControllerUserId)
                || !string.Equals(existingControllerUserId, controllerUserId, StringComparison.OrdinalIgnoreCase))
            {
                controllers[slotUserId] = controllerUserId;
                changed = true;
            }
        }

        if (changed)
        {
            NotifyPresenceChanged(gameId);
        }

        return changed;
    }

    public bool ReleaseDelegatedControl(string gameId, string slotUserId, string controllerUserId)
    {
        if (string.IsNullOrWhiteSpace(gameId)
            || string.IsNullOrWhiteSpace(slotUserId)
            || string.IsNullOrWhiteSpace(controllerUserId))
        {
            return false;
        }

        var changed = false;

        lock (_sync)
        {
            if (_delegatedControllersByGameAndUser.TryGetValue(gameId, out var controllers)
                && controllers.TryGetValue(slotUserId, out var existingControllerUserId)
                && string.Equals(existingControllerUserId, controllerUserId, StringComparison.OrdinalIgnoreCase))
            {
                controllers.Remove(slotUserId);
                if (controllers.Count == 0)
                {
                    _delegatedControllersByGameAndUser.Remove(gameId);
                }

                changed = true;
            }
        }

        if (changed)
        {
            QueueBotControlClear(gameId, slotUserId, "Delegated control released.");
            NotifyPresenceChanged(gameId);
        }

        return changed;
    }

    private bool RemoveConnectionCore(string gameId, string userId, string connectionId)
    {
        _connectionLookup.Remove(connectionId);

        if (!_connectionsByGameAndUser.TryGetValue(gameId, out var users)
            || !users.TryGetValue(userId, out var connections)
            || !connections.Remove(connectionId))
        {
            return false;
        }

        if (connections.Count > 0)
        {
            return false;
        }

        users.Remove(userId);
        if (users.Count == 0)
        {
            _connectionsByGameAndUser.Remove(gameId);
        }

        return true;
    }

    private bool IsUserConnectedCore(string gameId, string userId)
    {
        var hasLiveConnections = _connectionsByGameAndUser.TryGetValue(gameId, out var users)
            && users.TryGetValue(userId, out var connections)
            && connections.Count > 0;

        if (hasLiveConnections)
        {
            return true;
        }

        return _mockPresenceByGameAndUser.TryGetValue(gameId, out var mockUsers)
            && mockUsers.TryGetValue(userId, out var isConnected)
            && isConnected;
    }

    private bool ClearIncomingDelegatedControlCore(string gameId, string slotUserId)
    {
        if (!_delegatedControllersByGameAndUser.TryGetValue(gameId, out var controllers)
            || !controllers.Remove(slotUserId))
        {
            return false;
        }

        if (controllers.Count == 0)
        {
            _delegatedControllersByGameAndUser.Remove(gameId);
        }

        return true;
    }

    private bool AddOrRefreshConnection(string gameId, string userId, string connectionId)
    {
        if (string.IsNullOrWhiteSpace(gameId)
            || string.IsNullOrWhiteSpace(userId)
            || string.IsNullOrWhiteSpace(connectionId))
        {
            return false;
        }

        var changed = false;
        var changedGameIds = new HashSet<string>(StringComparer.Ordinal);

        lock (_sync)
        {
            foreach (var changedGameId in PruneStaleConnectionsCore(_timeProvider.GetUtcNow()))
            {
                changedGameIds.Add(changedGameId);
            }

            if (!_connectionsByGameAndUser.TryGetValue(gameId, out var users))
            {
                users = new Dictionary<string, Dictionary<string, DateTimeOffset>>(StringComparer.OrdinalIgnoreCase);
                _connectionsByGameAndUser[gameId] = users;
            }

            if (!users.TryGetValue(userId, out var connections))
            {
                connections = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
                users[userId] = connections;
            }

            var wasOffline = connections.Count == 0;
            var now = _timeProvider.GetUtcNow();

            if (_connectionLookup.TryGetValue(connectionId, out var existing)
                && (!string.Equals(existing.GameId, gameId, StringComparison.Ordinal)
                    || !string.Equals(existing.UserId, userId, StringComparison.OrdinalIgnoreCase)))
            {
                if (RemoveConnectionCore(existing.GameId, existing.UserId, connectionId))
                {
                    changedGameIds.Add(existing.GameId);
                }

                wasOffline = true;
            }

            connections[connectionId] = now;
            _connectionLookup[connectionId] = (gameId, userId);
            changed = wasOffline;

            if (wasOffline)
            {
                changed = ClearIncomingDelegatedControlCore(gameId, userId) || changed;
            }
        }

        foreach (var changedGameId in changedGameIds)
        {
            NotifyPresenceChanged(changedGameId);
        }

        if (changed)
        {
            QueueBotControlClear(gameId, userId, "Reconnect");
            NotifyPresenceChanged(gameId);
        }

        return changed;
    }

    private List<string> PruneStaleConnectionsCore(DateTimeOffset now)
    {
        var changedGameIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (gameId, users) in _connectionsByGameAndUser.ToList())
        {
            foreach (var (_, connections) in users.ToList())
            {
                foreach (var (connectionId, lastSeenUtc) in connections.ToList())
                {
                    if (!IsConnectionStale(lastSeenUtc, now))
                    {
                        continue;
                    }

                    connections.Remove(connectionId);
                    _connectionLookup.Remove(connectionId);
                    changedGameIds.Add(gameId);
                }
            }

            foreach (var (userId, connections) in users.Where(static pair => pair.Value.Count == 0).ToList())
            {
                users.Remove(userId);
            }

            if (users.Count == 0)
            {
                _connectionsByGameAndUser.Remove(gameId);
            }
        }

        return changedGameIds.ToList();
    }

    private bool IsConnectionStale(DateTimeOffset lastSeenUtc, DateTimeOffset now)
    {
        return now - lastSeenUtc >= _connectionExpirationWindow;
    }

    private void PruneStaleConnectionsAndNotify()
    {
        _ = PruneStaleConnections();
    }

    private void QueueBotControlClear(string gameId, string playerUserId, string clearReason)
    {
        if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(playerUserId))
        {
            return;
        }

        _ = ClearBotControlForPlayerAsync(gameId, playerUserId, clearReason);
    }

    private async Task ClearBotControlForPlayerAsync(string gameId, string playerUserId, string clearReason)
    {
        if (_gamesTable is null)
        {
            return;
        }

        if (!await ShouldClearAiControlForConnectedSeatAsync(playerUserId))
        {
            return;
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                var game = await GetGameEntityAsync(gameId);
                var latestEvent = await GetLatestEventAsync(gameId);
                if (game is null || latestEvent is null || string.IsNullOrWhiteSpace(latestEvent.SerializedGameState))
                {
                    return;
                }

                var snapshot = GameEventSerialization.DeserializeSnapshot(latestEvent.SerializedGameState);
                var playerStates = GameSeatStateProjection.BuildStates(game, snapshot);

                var changed = false;
                foreach (var playerState in playerStates.Where(playerState =>
                             string.Equals(playerState.PlayerUserId, playerUserId, StringComparison.OrdinalIgnoreCase)
                             && string.Equals(playerState.Control.BotControlStatus, BotControlStatuses.Active, StringComparison.OrdinalIgnoreCase)
                             && playerState.Control.BotControlClearedUtc is null))
                {
                    if (playerState.SeatIndex < 0 || playerState.SeatIndex >= snapshot.Players.Count)
                    {
                        continue;
                    }

                    snapshot.Players[playerState.SeatIndex].Control.BotControlClearedUtc = now;
                    snapshot.Players[playerState.SeatIndex].Control.BotControlStatus = BotControlStatuses.Cleared;
                    snapshot.Players[playerState.SeatIndex].Control.BotControlClearReason = clearReason;
                    changed = true;
                }

                if (!changed)
                {
                    return;
                }

                await PersistControlSnapshotAsync(gameId, snapshot, clearReason);

                NotifyPresenceChanged(gameId, metadataChanged: true);
                return;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return;
            }
            catch (RequestFailedException ex) when (ex.Status == 412 && attempt == 0)
            {
                continue;
            }
        }
    }

    private async Task<bool> ShouldClearAiControlForConnectedSeatAsync(string playerUserId)
    {
        if (_usersTable is null || string.IsNullOrWhiteSpace(playerUserId))
        {
            return true;
        }

        var profile = await TryGetUserProfileAsync(playerUserId);
        return profile?.IsBot != true;
    }

    private async Task<ApplicationUser?> TryGetUserProfileAsync(string userId)
    {
        var candidateIds = string.Equals(userId, userId.ToLowerInvariant(), StringComparison.Ordinal)
            ? new[] { userId }
            : new[] { userId, userId.ToLowerInvariant() };

        foreach (var candidateId in candidateIds)
        {
            try
            {
                var response = await _usersTable!.GetEntityAsync<ApplicationUser>("USER", candidateId);
                return response.Value;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
            }
        }

        return null;
    }

    private void NotifyPresenceChanged(string gameId, bool metadataChanged = false)
    {
        var handlers = PresenceChanged;
        if (handlers is null)
        {
            return;
        }

        var change = new GamePresenceChange(gameId, metadataChanged);

        foreach (Action<GamePresenceChange> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(change);
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"GamePresenceService PresenceChanged handler failed for game '{gameId}': {exception}");
            }
        }
    }

    public void Dispose()
    {
        _staleConnectionTimer?.Dispose();
    }

    private async Task<GameEntity?> GetGameEntityAsync(string gameId)
    {
        if (_gamesTable is null)
        {
            return null;
        }

        try
        {
            var response = await _gamesTable.GetEntityAsync<GameEntity>(gameId, "GAME");
            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private async Task<GameEventEntity?> GetLatestEventAsync(string gameId)
    {
        if (_gamesTable is null)
        {
            return null;
        }

        GameEventEntity? latest = null;
        var filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {gameId} and RowKey ge {EventRowKeyPrefix} and RowKey lt {EventRowKeyExclusiveUpperBound}");

        await foreach (var gameEvent in _gamesTable.QueryAsync<GameEventEntity>(filter: filter))
        {
            if (latest is null || string.CompareOrdinal(gameEvent.RowKey, latest.RowKey) > 0)
            {
                latest = gameEvent;
            }
        }

        return latest;
    }

    private async Task PersistControlSnapshotAsync(string gameId, GameState snapshot, string clearReason)
    {
        if (_gamesTable is null)
        {
            return;
        }

        var rowKey = $"Event_{DateTime.UtcNow.Ticks:D20}_{Guid.NewGuid():N}";
        await _gamesTable.AddEntityAsync(new GameEventEntity
        {
            PartitionKey = gameId,
            RowKey = rowKey,
            GameId = gameId,
            EventKind = "SeatControlUpdated",
            EventData = "{}",
            PreviewRouteNodeIdsJson = "[]",
            PreviewRouteSegmentKeysJson = "[]",
            SerializedGameState = GameEventSerialization.SerializeSnapshot(snapshot),
            OccurredUtc = DateTimeOffset.UtcNow,
            CreatedBy = string.Empty,
            ActingUserId = string.Empty,
            ActingPlayerIndex = null,
            ChangeSummary = clearReason
        });
    }
}

public sealed record GamePresenceChange(string GameId, bool MetadataChanged = false);
