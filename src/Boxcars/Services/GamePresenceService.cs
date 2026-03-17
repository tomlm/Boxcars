using Azure;
using Azure.Data.Tables;
using Boxcars.Data;
using Boxcars.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Boxcars.Services;

public sealed class GamePresenceService : IDisposable
{
    private readonly object _sync = new();
    private readonly TableClient? _gamesTable;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _connectionExpirationWindow;
    private readonly Timer? _staleConnectionTimer;
    private readonly Dictionary<string, Dictionary<string, Dictionary<string, DateTimeOffset>>> _connectionsByGameAndUser = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, bool>> _mockPresenceByGameAndUser = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, string>> _delegatedControllersByGameAndUser = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string GameId, string UserId)> _connectionLookup = new(StringComparer.Ordinal);

    [ActivatorUtilitiesConstructor]
    public GamePresenceService(TableServiceClient tableServiceClient)
        : this(tableServiceClient, TimeProvider.System)
    {
    }

    public GamePresenceService()
        : this(null, TimeProvider.System)
    {
    }

    public GamePresenceService(TimeProvider timeProvider, TimeSpan? connectionExpirationWindow = null, TimeSpan? cleanupInterval = null)
        : this(null, timeProvider, connectionExpirationWindow, cleanupInterval)
    {
    }

    private GamePresenceService(
        TableServiceClient? tableServiceClient,
        TimeProvider timeProvider,
        TimeSpan? connectionExpirationWindow = null,
        TimeSpan? cleanupInterval = null)
    {
        _gamesTable = tableServiceClient?.GetTableClient(TableNames.GamesTable);
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

    public event Action<string>? PresenceChanged;

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
                QueueBotAssignmentClear(gameId, userId, "Reconnect");
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

    public SeatControllerState ResolveSeatControllerState(string gameId, string? slotUserId, BotAssignment? activeBotAssignment)
    {
        return PlayerControlRules.ResolveSeatControllerState(
            gameId,
            slotUserId,
            IsUserConnected(gameId, slotUserId),
            GetDelegatedControllerUserId(gameId, slotUserId),
            activeBotAssignment);
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
            QueueBotAssignmentClear(gameId, slotUserId, "Delegated control released.");
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
            QueueBotAssignmentClear(gameId, userId, "Reconnect");
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

    private void QueueBotAssignmentClear(string gameId, string playerUserId, string clearReason)
    {
        if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(playerUserId))
        {
            return;
        }

        _ = ClearBotAssignmentsForPlayerAsync(gameId, playerUserId, clearReason);
    }

    private async Task ClearBotAssignmentsForPlayerAsync(string gameId, string playerUserId, string clearReason)
    {
        if (_gamesTable is null)
        {
            return;
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var response = await _gamesTable.GetEntityAsync<GameEntity>(gameId, "GAME");
                var gameEntity = response.Value;
                var assignments = BotAssignmentSerialization.Deserialize(gameEntity.BotAssignmentsJson).ToList();
                var changed = false;
                var now = DateTimeOffset.UtcNow;

                for (var index = 0; index < assignments.Count; index++)
                {
                    var assignment = assignments[index];
                    if (!string.Equals(assignment.PlayerUserId, playerUserId, StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(assignment.Status, BotAssignmentStatuses.Active, StringComparison.OrdinalIgnoreCase)
                        || assignment.ClearedUtc is not null
                        || !string.Equals(PlayerControlRules.ResolveBotControllerMode(assignment), SeatControllerModes.AiGhost, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    assignments[index] = assignment with
                    {
                        Status = BotAssignmentStatuses.Cleared,
                        ClearReason = clearReason,
                        ClearedUtc = now
                    };
                    changed = true;
                }

                if (!changed)
                {
                    return;
                }

                var updateEntity = new TableEntity(gameEntity.PartitionKey, gameEntity.RowKey)
                {
                    [nameof(GameEntity.BotAssignmentsJson)] = BotAssignmentSerialization.Serialize(assignments)
                };

                await _gamesTable.UpdateEntityAsync(updateEntity, gameEntity.ETag, TableUpdateMode.Merge);
                NotifyPresenceChanged(gameId);
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

    private void NotifyPresenceChanged(string gameId)
    {
        var handlers = PresenceChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (Action<string> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(gameId);
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
}
