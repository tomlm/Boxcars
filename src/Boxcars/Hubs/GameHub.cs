using System.Security.Claims;
using Boxcars.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Boxcars.Hubs;

[Authorize]
public sealed class GameHub : Hub
{
    private readonly GamePresenceService _gamePresenceService;

    public GameHub(GamePresenceService gamePresenceService)
    {
        _gamePresenceService = gamePresenceService;
    }

    public async Task JoinGame(string gameId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, gameId);

        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        var becameConnected = _gamePresenceService.AddConnection(gameId, userId, Context.ConnectionId);
        if (becameConnected)
        {
            await Clients.Group(gameId).SendAsync(GameHubEvents.PresenceUpdated);
        }
    }

    public async Task LeaveGame(string gameId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, gameId);

        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        var becameDisconnected = _gamePresenceService.RemoveConnection(gameId, userId, Context.ConnectionId);
        if (becameDisconnected)
        {
            await Clients.Group(gameId).SendAsync(GameHubEvents.PresenceUpdated);
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var disconnectedEntries = _gamePresenceService.RemoveConnection(Context.ConnectionId);
        foreach (var (gameId, _) in disconnectedEntries)
        {
            await Clients.Group(gameId).SendAsync(GameHubEvents.PresenceUpdated);
        }

        await base.OnDisconnectedAsync(exception);
    }
}

public static class GameHubEvents
{
    public const string StateUpdated = "StateUpdated";
    public const string PresenceUpdated = "PresenceUpdated";
    public const string JoinGame = "JoinGame";
    public const string LeaveGame = "LeaveGame";
}
