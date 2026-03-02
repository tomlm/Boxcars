# Boxcars Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-02-26

## Active Technologies
- C# on .NET 8 (current project target) + ASP.NET Core Blazor Server, ASP.NET Core SignalR, ASP.NET Core Identity (existing scaffold), Entity Framework Core (existing), `Azure.Data.Tables` (future production storage) (002-shell-app-pages)
- SQL Server LocalDB via EF Core (existing dev setup); Azure Table Storage for production game data (deferred — profile fields added to existing EF model for this feature) (002-shell-app-pages)
- C# on .NET 8 (current project target `net8.0`) + ASP.NET Core Blazor Server, ASP.NET Core SignalR, ASP.NET Core Identity (custom table storage stores), `Azure.Data.Tables`, `Microsoft.FluentUI.AspNetCore.Components` v4.x (002-shell-app-pages)
- Azure Table Storage exclusively — Azurite emulator for local development, Azure Table Storage in production. No SQL Server, no EF Core. (002-shell-app-pages)

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
- 002-shell-app-pages: Added C# on .NET 8 (current project target `net8.0`) + ASP.NET Core Blazor Server, ASP.NET Core SignalR, ASP.NET Core Identity (custom table storage stores), `Azure.Data.Tables`, `Microsoft.FluentUI.AspNetCore.Components` v4.x
- 002-shell-app-pages: Added C# on .NET 8 (current project target) + ASP.NET Core Blazor Server, ASP.NET Core SignalR, ASP.NET Core Identity (existing scaffold), Entity Framework Core (existing), `Azure.Data.Tables` (future production storage)

- 001-initial-shell-app: Added C# on .NET 10 (SDK installed: 10.0.102) + ASP.NET Core Blazor Server, ASP.NET Core SignalR, ASP.NET Core Authentication (OpenID Connect providers), `Microsoft.FluentUI.AspNetCore.Components`, `Azure.Data.Tables`

<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
