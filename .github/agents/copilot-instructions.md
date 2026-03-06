# Boxcars Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-02-26

## Active Technologies
- C# / .NET 8 (`net8.0`) + ASP.NET Core Blazor Server, Microsoft Fluent UI Blazor components, existing map stack (`MapBoard`, `MapRouteService`, board projection/viewport services) (001-add-route-suggestion)
- Azure Table Storage for player/game ownership state; in-memory map graph context for route calculation (001-add-route-suggestion)
- C# 12 on .NET 8 (LTS) + .NET base class libraries, existing Boxcars solution projects, ASP.NET Core-compatible interfaces for host integration (002-rail-baron-engine)
- Durable persisted game state via repository-backed store (Azure Table storage adapter in host layer) (002-rail-baron-engine)
- C# 12 on .NET 8 (LTS) + .NET BCL, ASP.NET Core-compatible contracts, Azure.Data.Tables for persistence adapter (002-rail-baron-engine)
- Azure Table Storage with records organized by `GameId`: one current full-state snapshot blob + per-action immutable log rows (002-rail-baron-engine)
- C# 12 / .NET 8 (LTS) + `System.ComponentModel` (INotifyPropertyChanged), `System.Collections.ObjectModel` (ObservableCollection), `System.Text.Json` (serialization). No third-party dependencies for the engine library itself. (004-observable-game-model)
- Azure Table Storage — game state serialized as JSON blob property via `System.Text.Json` (004-observable-game-model)
- C# / .NET 8 (`net8.0`) + ASP.NET Core Blazor Server, ASP.NET Core SignalR, Azure.Data.Tables, Microsoft Fluent UI Blazor (002-redo-table-storage)
- Azure Table Storage (`UsersTable`, `GamesTable`) (002-redo-table-storage)

- C# on .NET 10 (SDK installed: 10.0.102) + ASP.NET Core Blazor Server, ASP.NET Core SignalR, ASP.NET Core Authentication (OpenID Connect providers), `Microsoft.FluentUI.AspNetCore.Components`, `Azure.Data.Tables` (001-initial-shell-app)

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
- 002-redo-table-storage: Added C# / .NET 8 (`net8.0`) + ASP.NET Core Blazor Server, ASP.NET Core SignalR, Azure.Data.Tables, Microsoft Fluent UI Blazor
- 004-observable-game-model: Added C# 12 / .NET 8 (LTS) + `System.ComponentModel` (INotifyPropertyChanged), `System.Collections.ObjectModel` (ObservableCollection), `System.Text.Json` (serialization). No third-party dependencies for the engine library itself.
- 002-rail-baron-engine: Added C# 12 on .NET 8 (LTS) + .NET BCL, ASP.NET Core-compatible contracts, Azure.Data.Tables for persistence adapter


<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
