# Boxcars Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-02-26

## Active Technologies
- C# / .NET 8 (`net8.0`) + ASP.NET Core Blazor Server, Microsoft Fluent UI Blazor components, existing map stack (`MapBoard`, `MapRouteService`, board projection/viewport services) (001-add-route-suggestion)
- Azure Table Storage for player/game ownership state; in-memory map graph context for route calculation (001-add-route-suggestion)

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
- 001-add-route-suggestion: Added C# / .NET 8 (`net8.0`) + ASP.NET Core Blazor Server, Microsoft Fluent UI Blazor components, existing map stack (`MapBoard`, `MapRouteService`, board projection/viewport services)
- 001-render-rbp-map: Added C# / .NET 8 (`net8.0`) + ASP.NET Core Blazor Server, Microsoft Fluent UI Blazor components, SignalR, Azure.Data.Tables
- 002-shell-app-pages: Added C# on .NET 8 (current project target `net8.0`) + ASP.NET Core Blazor Server, ASP.NET Core SignalR, ASP.NET Core Identity (custom table storage stores), `Azure.Data.Tables`, `Microsoft.FluentUI.AspNetCore.Components` v4.x


<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
