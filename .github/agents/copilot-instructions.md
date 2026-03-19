# Boxcars Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-02-26

## Active Technologies
- C# / .NET 8 (`net8.0`) + ASP.NET Core Blazor Server, MudBlazor components, existing map stack (`MapBoard`, `MapRouteService`, board projection/viewport services) (001-add-route-suggestion)
- Azure Table Storage for player/game ownership state; in-memory map graph context for route calculation (001-add-route-suggestion)
- C# 12 on .NET 8 (LTS) + .NET base class libraries, existing Boxcars solution projects, ASP.NET Core-compatible interfaces for host integration (002-rail-baron-engine)
- Durable persisted game state via repository-backed store (Azure Table storage adapter in host layer) (002-rail-baron-engine)
- C# 12 on .NET 8 (LTS) + .NET BCL, ASP.NET Core-compatible contracts, Azure.Data.Tables for persistence adapter (002-rail-baron-engine)
- Azure Table Storage with records organized by `GameId`: one current full-state snapshot blob + per-action immutable log rows (002-rail-baron-engine)
- C# 12 / .NET 8 (LTS) + `System.ComponentModel` (INotifyPropertyChanged), `System.Collections.ObjectModel` (ObservableCollection), `System.Text.Json` (serialization). No third-party dependencies for the engine library itself. (004-observable-game-model)
- Azure Table Storage — game state serialized as JSON blob property via `System.Text.Json` (004-observable-game-model)
- C# / .NET 8 (`net8.0`) + ASP.NET Core Blazor Server, ASP.NET Core SignalR, Azure.Data.Tables, Microsoft Fluent UI Blazor (002-redo-table-storage)
- Azure Table Storage (`UsersTable`, `GamesTable`) (002-redo-table-storage)
- C# 12 / .NET 8 LTS + ASP.NET Core Blazor Server, MudBlazor, ASP.NET Core SignalR, Azure.Data.Tables, `Boxcars.Engine` domain model, `System.Text.Json` (001-game-state-turn-management)
- Azure Table Storage `GamesTable` with `GameEntity` setup row and `GameEventEntity` event rows containing `EventData` and `SerializedGameState` (001-game-state-turn-management)
- C# on .NET 10 (SDK installed: 10.0.102) + ASP.NET Core Blazor Server, ASP.NET Core SignalR, ASP.NET Core Authentication (OpenID Connect providers), `MudBlazor`, `Azure.Data.Tables` (001-initial-shell-app)
- C# 12 / .NET 8 (LTS) + Blazor Server, MudBlazor, ASP.NET Core SignalR, Azure.Data.Tables, existing `Boxcars.Engine` domain model (001-purchase-railroads)
- Azure Table Storage for persisted game snapshots/events; app configuration via `appsettings.json` / `appsettings.Development.json` (001-purchase-railroads)
- C# / .NET 10.0 + ASP.NET Core Blazor Server, MudBlazor, ASP.NET Core SignalR, Azure.Data.Tables (001-sell-railroads)
- Azure Table storage for persisted game/event snapshots (001-sell-railroads)
- C# / .NET 10 + ASP.NET Core Blazor Server, MudBlazor, SignalR, Azure Data Tables, Boxcars.Engine domain services (001-pick-region-destination)
- Azure Table storage with serialized authoritative `GameState` snapshots and event timeline records (001-pick-region-destination)
- C# / .NET 10 for the web app, .NET 8 xUnit test projects + ASP.NET Core Blazor Server, MudBlazor, SignalR, Azure.Data.Tables, ASP.NET Core Identity, `HttpClient` for OpenAI API access (001-ai-player)
- Azure Table storage with a new `BotsTable` for global bot definitions plus persisted bot-assignment metadata inside game records in `GamesTable`; transient connection ownership remains in `GamePresenceService` (001-ai-player)
- C# / .NET 10.0 + ASP.NET Core Blazor Server, SignalR, MudBlazor, Azure.Data.Tables, built-in `HttpClient`-based OpenAI client (001-ai-player)
- Azure Table storage (`GamesTable`, `BotsTable`, existing event/game entities) (001-ai-player)

## Project Structure

```text
backend/
frontend/
tests/
```

## Commands

# Add commands for C# on .NET 10 (SDK installed: 10.0.102)

## Code Style

C# on .NET 10 (SDK installed: 10.0.102): Follow standard conventions

## Recent Changes
- 001-ai-player: Added C# / .NET 10.0 + ASP.NET Core Blazor Server, SignalR, MudBlazor, Azure.Data.Tables, built-in `HttpClient`-based OpenAI client
- 001-ai-player: Added C# / .NET 10 for the web app, .NET 8 xUnit test projects + ASP.NET Core Blazor Server, MudBlazor, SignalR, Azure.Data.Tables, ASP.NET Core Identity, `HttpClient` for OpenAI API access
- 001-pick-region-destination: Added C# / .NET 10 + ASP.NET Core Blazor Server, MudBlazor, SignalR, Azure Data Tables, Boxcars.Engine domain services


<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
