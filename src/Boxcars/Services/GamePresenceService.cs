namespace Boxcars.Services;

public sealed class GamePresenceService
{
    private readonly object _sync = new();
    private readonly Dictionary<string, Dictionary<string, HashSet<string>>> _connectionsByGameAndUser = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, bool>> _mockPresenceByGameAndUser = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string GameId, string UserId)> _connectionLookup = new(StringComparer.Ordinal);

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
        }

        if (changed)
        {
            PresenceChanged?.Invoke(gameId);
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
            PresenceChanged?.Invoke(gameId);
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
            PresenceChanged?.Invoke(gameId);
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
            PresenceChanged?.Invoke(gameId);
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
            }
        }

        if (changed)
        {
            PresenceChanged?.Invoke(gameId);
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
}
