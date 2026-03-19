namespace Boxcars.Services;

public sealed class GameCircuitPresenceTracker
{
    private readonly object _sync = new();
    private readonly Dictionary<string, (string UserId, string ConnectionId)> _registrationsByGameId = new(StringComparer.Ordinal);

    public void Register(string gameId, string userId, string connectionId)
    {
        if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(connectionId))
        {
            return;
        }

        lock (_sync)
        {
            _registrationsByGameId[gameId] = (userId, connectionId);
        }
    }

    public void Unregister(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return;
        }

        lock (_sync)
        {
            _registrationsByGameId.Remove(gameId);
        }
    }

    public IReadOnlyList<(string GameId, string UserId, string ConnectionId)> Snapshot()
    {
        lock (_sync)
        {
            return _registrationsByGameId
                .Select(entry => (entry.Key, entry.Value.UserId, entry.Value.ConnectionId))
                .ToList();
        }
    }
}