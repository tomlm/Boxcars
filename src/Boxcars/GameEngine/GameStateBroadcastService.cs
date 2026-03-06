using Boxcars.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using RailBaronGameState = global::Boxcars.Engine.Persistence.GameState;

namespace Boxcars.GameEngine;

public sealed class GameStateBroadcastService : IHostedService
{
    private readonly IGameEngine _gameEngine;
    private readonly IHubContext<GameHub> _hubContext;

    public GameStateBroadcastService(IGameEngine gameEngine, IHubContext<GameHub> hubContext)
    {
        _gameEngine = gameEngine;
        _hubContext = hubContext;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _gameEngine.OnStateChanged += OnStateChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _gameEngine.OnStateChanged -= OnStateChanged;
        return Task.CompletedTask;
    }

    private void OnStateChanged(string gameId, RailBaronGameState state)
    {
        _ = _hubContext.Clients.Group(gameId)
            .SendAsync(GameHubEvents.StateUpdated, state, CancellationToken.None);
    }
}
