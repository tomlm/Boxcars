using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Boxcars.Hubs;

[Authorize]
public class DashboardHub : Hub
{
}

public static class DashboardHubEvents
{
    public const string RouteSuggestionUpdated = "RouteSuggestionUpdated";
}
