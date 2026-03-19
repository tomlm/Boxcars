using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Boxcars.Services;

public sealed class GamePresenceCircuitHandler : CircuitHandler
{
    private readonly GameCircuitPresenceTracker _presenceTracker;
    private readonly GamePresenceService _gamePresenceService;

    public GamePresenceCircuitHandler(GameCircuitPresenceTracker presenceTracker, GamePresenceService gamePresenceService)
    {
        _presenceTracker = presenceTracker;
        _gamePresenceService = gamePresenceService;
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        foreach (var registration in _presenceTracker.Snapshot())
        {
            _gamePresenceService.RemoveConnection(registration.GameId, registration.UserId, registration.ConnectionId);
        }

        return Task.CompletedTask;
    }
}