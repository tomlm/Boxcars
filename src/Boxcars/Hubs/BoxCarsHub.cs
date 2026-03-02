using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Boxcars.Hubs;

[Authorize]
public class BoxCarsHub : Hub
{
}
