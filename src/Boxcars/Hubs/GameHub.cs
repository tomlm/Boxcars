using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Boxcars.Hubs;

[Authorize]
public sealed class GameHub : Hub
{
    public Task JoinGame(string gameId)
    {
        return Groups.AddToGroupAsync(Context.ConnectionId, gameId);
    }

    public Task LeaveGame(string gameId)
    {
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, gameId);
    }
}

public static class GameHubEvents
{
    public const string StateUpdated = "StateUpdated";
    public const string JoinGame = "JoinGame";
    public const string LeaveGame = "LeaveGame";
}
