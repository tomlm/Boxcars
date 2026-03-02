# Implementation Plan: BoxCars Shell Application Pages

**Branch**: `002-shell-app-pages` | **Date**: 2026-02-27 | **Spec**: `/specs/002-shell-app-pages/spec.md`
**Input**: Feature specification from `/specs/002-shell-app-pages/spec.md`

## Summary

Rewrite the default Blazor Server template pages into the BoxCars shell application using Microsoft Fluent UI Blazor components. Remove EF Core and SQL Server entirely — replace with Azure Table Storage (Azurite locally) for all data including Identity user accounts. Implement custom Identity stores (`IUserStore<T>` family) backed by table storage. Replace the Home page with a public landing page directing visitors to email sign-in. Replace Counter, Weather, and Auth demo pages with an authenticated dashboard showing profile identity (nickname + thumbnail URL), stubbed stats area, and join-or-create game action. Replace sidebar layout with a Fluent UI top navigation bar using `FluentHeader` and `FluentProfileMenu`. Add placeholder game page for routing. Use SignalR with global broadcast for real-time dashboard refresh. Remove Bootstrap and adopt Fluent UI design system throughout.

## Technical Context

**Language/Version**: C# on .NET 8 (current project target `net8.0`)  
**Primary Dependencies**: ASP.NET Core Blazor Server, ASP.NET Core SignalR, ASP.NET Core Identity (custom table storage stores), `Azure.Data.Tables`, `Microsoft.FluentUI.AspNetCore.Components` v4.x  
**Storage**: Azure Table Storage exclusively — Azurite emulator for local development, Azure Table Storage in production. No SQL Server, no EF Core.  
**Testing**: xUnit + bUnit (recommended, not mandated by constitution for this feature scope)  
**Target Platform**: Modern web browsers (desktop and mobile), server-hosted ASP.NET Core app  
**Project Type**: Web application (server-rendered interactive UI)  
**Performance Goals**: SC-001 (95% auth-to-dashboard in <30s); dashboard primary action visible on first render  
**Constraints**: One active game per player, async I/O with CancellationToken propagation, minimal architecture layers per constitution Principle III, Azure Table Storage only, no EF Core  
**Scale/Scope**: Shell application pages only (landing, dashboard, placeholder game page, profile settings); full gameplay deferred

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Phase 0 Gate Review

- **I. Gameplay Fidelity**: PASS — Feature does not implement or alter Rail Baron rules. Shell pages only.
- **II. Real-Time Multiplayer First**: PASS — Plan includes SignalR for dashboard state refresh with global broadcast; game state will be server-authoritative when gameplay is implemented. Placeholder game page establishes the routing pattern.
- **III. Simplicity & Ship Fast**: PASS — Single existing project extended. Custom Identity stores are necessary complexity (user explicitly requires Azure Table Storage only). No speculative abstractions beyond what's needed for the storage swap. Fluent UI provides built-in layout/profile components that reduce custom code.
- **Naming Conventions**: PASS — `UsersTable`, `GamesTable`, `GamePlayersTable`, `UserEmailIndexTable`, `UserNameIndexTable`, `NicknameIndexTable`, `PlayerActiveGameIndexTable` — all mono tables following `<PluralObjectName>Table` convention.
- **Coding Conventions**: PASS — All storage operations async with `CancellationToken`. LINQ extension-method syntax.

### Post-Phase 1 Design Re-check

- **I. Gameplay Fidelity**: PASS — No game rules involved.
- **II. Real-Time Multiplayer First**: PASS — SignalR hub for global dashboard refresh; server-authoritative game state via table storage.
- **III. Simplicity & Ship Fast**: PASS — Extends existing project in-place. Custom Identity stores are the minimal implementation needed. 7 tables (3 core + 4 indexes) — each justified by a specific lookup pattern. Fluent UI replaces Bootstrap with richer built-in components (fewer custom CSS/layout workarounds).
- **Naming Conventions**: PASS — All tables follow mono table naming convention.
- **Coding Conventions**: PASS — Async I/O with `CancellationToken` throughout.

## Project Structure

### Documentation (this feature)

```text
specs/002-shell-app-pages/
├── plan.md              # This file
├── research.md          # Phase 0: technical research (R1–R11)
├── data-model.md        # Phase 1: Azure Table Storage tables (7 tables)
├── quickstart.md        # Phase 1: local dev setup (Azurite)
├── contracts/
│   └── realtime-events.md  # Phase 1: SignalR hub contract
└── tasks.md             # Phase 2 output (NOT created by /speckit.plan)
```

### Source Code (existing project, extended in-place)

```text
src/Boxcars/
├── Boxcars.csproj                    # MODIFY: swap NuGet packages (remove EF Core, add Table Storage + Fluent UI + SignalR)
├── Program.cs                        # MODIFY: remove EF Core, add Table Storage + custom Identity + SignalR + Fluent UI
├── appsettings.json                  # MODIFY: remove ConnectionStrings, add AzureTableStorage section
├── appsettings.Development.json      # MODIFY: add Azurite connection string
├── Components/
│   ├── _Imports.razor                # MODIFY: add Fluent UI usings
│   ├── App.razor                     # MODIFY: add Fluent UI CSS/theme, remove Bootstrap
│   ├── Routes.razor                  # KEEP (already has AuthorizeRouteView)
│   ├── Layout/
│   │   ├── MainLayout.razor          # TRANSFORM: sidebar → FluentLayout + FluentHeader top nav bar
│   │   ├── MainLayout.razor.css      # TRANSFORM: layout styles for Fluent UI
│   │   ├── NavMenu.razor             # DELETE (replaced by FluentHeader content in MainLayout)
│   │   └── NavMenu.razor.css         # DELETE
│   ├── Pages/
│   │   ├── Home.razor                # TRANSFORM: → landing page with auth redirect
│   │   ├── Dashboard.razor           # NEW: authenticated dashboard
│   │   ├── Game.razor                # NEW: placeholder game page
│   │   ├── ProfileSettings.razor     # NEW: nickname/thumbnail settings
│   │   ├── Error.razor               # KEEP as-is
│   │   ├── Counter.razor             # DELETE
│   │   ├── Weather.razor             # DELETE
│   │   └── Auth.razor                # DELETE
│   └── Account/                      # KEEP: existing Identity scaffold pages
├── Data/
│   ├── ApplicationUser.cs            # REWRITE: ITableEntity implementation (no IdentityUser)
│   ├── GameEntity.cs                 # NEW: ITableEntity for games
│   ├── GamePlayerEntity.cs           # NEW: ITableEntity for game-player joins
│   ├── IndexEntity.cs                # NEW: shared ITableEntity for all index tables
│   ├── ApplicationDbContext.cs       # DELETE
│   └── Migrations/                   # DELETE (entire directory)
├── Identity/
│   ├── TableStorageUserStore.cs      # NEW: IUserStore + IUserEmailStore + IUserPasswordStore + etc.
│   └── TableNames.cs                 # NEW: string constants for all 7 table names
├── Hubs/
│   └── BoxCarsHub.cs                 # NEW: SignalR hub (empty — events dispatched from services)
├── Services/
│   ├── PlayerProfileService.cs       # NEW: profile CRUD (nickname uniqueness, thumbnail update)
│   └── GameService.cs                # NEW: create/join/query game state + SignalR broadcast
└── wwwroot/
    ├── app.css                       # MODIFY: replace Bootstrap body styles with Fluent CSS variables
    └── bootstrap/                    # DELETE (replaced by Fluent UI)
```

**Structure Decision**: Extend the existing single Blazor Server project in-place. New `Identity/` folder groups the custom store implementation. `Data/` folder holds table entity POCOs. `Hubs/` for SignalR. `Services/` for business logic. No new solution projects — Principle III. Bootstrap removed, Fluent UI adopted.

## Complexity Tracking

### Custom Identity Stores — Justified Complexity

The custom `IUserStore<T>` implementation is non-trivial compared to the default EF Core stores. This is justified because:

- **User requirement**: Azure Table Storage only — no SQL, no EF Core. This is a non-negotiable constraint.
- **Interfaces required**: `IUserStore`, `IUserEmailStore`, `IUserPasswordStore`, `IUserSecurityStampStore`, `IUserLockoutStore` — each is small (2-5 methods) and well-documented.
- **Constitution alignment**: The implementation is the minimum needed. No abstractions beyond what Identity requires.
- **Risk mitigation**: The Identity scaffold pages (Login, Register, etc.) are unchanged — they use `UserManager<ApplicationUser>` which delegates to the custom store transparently.

### 7 Tables (4 Indexes) — Justified Complexity

Four index tables (`UserEmailIndexTable`, `UserNameIndexTable`, `NicknameIndexTable`, `PlayerActiveGameIndexTable`) alongside three core tables. Justified because:

- Azure Table Storage has no secondary indexes — index tables are the standard pattern for O(1) lookups by non-key fields.
- Each index serves a specific, required lookup: email login, username login, nickname uniqueness, active game constraint.
- Constitution mono table naming applied consistently.
