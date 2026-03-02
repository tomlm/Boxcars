# Realtime Events: BoxCars Shell Application Pages

**Feature**: `002-shell-app-pages`  
**Date**: 2026-02-27  
**Transport**: ASP.NET Core SignalR  
**Hub endpoint**: `/hubs/boxcars`  
**Auth**: `[Authorize]` — cookie auth flows automatically for same-origin Blazor Server connections

---

## Hub Class

```csharp
[Authorize]
public class BoxCarsHub : Hub { }
```

The hub is empty — it serves as the connection endpoint. All events are dispatched from server-side services via `IHubContext<BoxCarsHub>`.

---

## Server → Client Events

### DashboardStateRefreshed

Notifies **all connected clients** that game state has changed and they should refresh their dashboard data.

- **Event**: `DashboardStateRefreshed`
- **Target**: `Clients.All` (global broadcast per clarification)
- **Payload**: None — clients re-query their own state on receipt
- **Triggered by**: Any game mutation (create, join, leave, cancel, complete)

**Dispatch pattern**:
```csharp
// In GameService, after any game mutation:
await hubContext.Clients.All.SendAsync("DashboardStateRefreshed", cancellationToken);
```

**Client handling**:
```razor
hubConnection.On("DashboardStateRefreshed", async () =>
{
    await LoadDashboardState();
    await InvokeAsync(StateHasChanged);
});
```

### JoinConflict

Notifies a **specific player** that their join attempt failed because the game state changed.

- **Event**: `JoinConflict`
- **Target**: `Clients.User(playerId)` (targeted via built-in `IUserIdProvider`)
- **Payload**:
  ```json
  {
    "gameId": "guid-value",
    "reason": "Game is no longer available"
  }
  ```
- **Triggered by**: `GameService.JoinGameAsync` when the target game is full, closed, or missing

**Dispatch pattern**:
```csharp
await hubContext.Clients.User(playerId)
    .SendAsync("JoinConflict", new { gameId, reason }, cancellationToken);
```

---

## Connection Lifecycle

1. Dashboard component initializes → creates `HubConnection` to `/hubs/boxcars`
2. Connection established → registers handlers for `DashboardStateRefreshed` and `JoinConflict`
3. On `DashboardStateRefreshed` → re-queries dashboard state from `GameService` and re-renders
4. On `JoinConflict` → shows conflict message via `IMessageService` (Fluent UI MessageBar), re-queries state
5. Dashboard component disposes → disposes `HubConnection`

---

## Design Notes

- **No per-player channels**: Global broadcast keeps the implementation simple. Each client filters server data to its own state during the re-query. This is proportional for the expected player count.
- **No payload on refresh**: The refresh event is a signal, not a data carrier. Clients fetch their own state to avoid stale-payload issues and keep the hub stateless.
- **User targeting**: `Clients.User(id)` works automatically because ASP.NET Core Identity sets `ClaimTypes.NameIdentifier` on the authentication principal, which SignalR's default `IUserIdProvider` reads.
