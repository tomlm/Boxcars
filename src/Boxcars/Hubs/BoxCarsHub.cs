using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Boxcars.Hubs;

[Authorize]
public class BoxCarsHub : Hub
{
}

public static class BoxCarsHubEvents
{
	public const string RouteSuggestionUpdated = "RouteSuggestionUpdated";
}
