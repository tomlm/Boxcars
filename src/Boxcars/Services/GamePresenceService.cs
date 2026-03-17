using Azure;
using Azure.Data.Tables;
using Boxcars.Data;
using Boxcars.Identity;

namespace Boxcars.Services;

public sealed class GamePresenceService
{
    private readonly object _sync = new();
    private readonly TableClient? _gamesTable;
    private readonly Dictionary<string, Dictionary<string, HashSet<string>>> _connectionsByGameAndUser = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, bool>> _mockPresenceByGameAndUser = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, string>> _delegatedControllersByGameAndUser = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string GameId, string UserId)> _connectionLookup = new(StringComparer.Ordinal);

    public GamePresenceService(TableServiceClient tableServiceClient)
    {
        _gamesTable = tableServiceClient.GetTableClient(TableNames.GamesTable);
    }

    public GamePresenceService()
    {
    }

    public event Action<string>? PresenceChanged;

    public bool AddConnection(string gameId, string userId, string connectionId)
    {
        var changed = false;

        lock (_sync)
        {
            if (_connectionLookup.TryGetValue(connectionId, out var existing)
                && string.Equals(existing.GameId, gameId, StringComparison.Ordinal)
                && string.Equals(existing.UserId, userId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!_connectionsByGameAndUser.TryGetValue(gameId, out var users))
            {
                users = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                _connectionsByGameAndUser[gameId] = users;
            }

            if (!users.TryGetValue(userId, out var connections))
            {
                connections = new HashSet<string>(StringComparer.Ordinal);
                users[userId] = connections;
            }

            var wasOffline = connections.Count == 0;
            connections.Add(connectionId);
            _connectionLookup[connectionId] = (gameId, userId);
            changed = wasOffline;

            if (wasOffline)
            {
                changed = ClearIncomingDelegatedControlCore(gameId, userId) || changed;
            }
        }

        if (changed)
        {
            QueueBotAssignmentClear(gameId, userId, "Reconnect");
            NotifyPresenceChanged(gameId);
        }

        return changed;
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

        lock (_sync)
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
}
