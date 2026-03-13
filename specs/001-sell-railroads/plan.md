# Implementation Plan: Forced Railroad Sales and Auctions

**Branch**: `001-sell-railroads` | **Date**: 2026-03-12 | **Spec**: `/specs/001-sell-railroads/spec.md`
**Input**: Feature specification from `/specs/001-sell-railroads/spec.md`

## Summary

Implement a forced-sale subflow during `UseFees` that keeps fee resolution authoritative in the engine, lets the active player sell owned railroads either to the bank or through a multiplayer auction, reuses server-derived network analysis for sale-impact advisory UI, and updates the persisted turn snapshot plus player-action pipeline so auction progress and insolvency outcomes propagate in real time to every client.

## Technical Context

**Language/Version**: C# / .NET 10.0  
**Primary Dependencies**: ASP.NET Core Blazor Server, MudBlazor, ASP.NET Core SignalR, Azure.Data.Tables  
**Storage**: Azure Table storage for persisted game/event snapshots  
**Testing**: xUnit engine/service tests via `tests/Boxcars.Engine.Tests` and targeted state-mapper coverage where needed  
**Target Platform**: Modern desktop and mobile browsers connected to the Blazor Server app  
**Project Type**: Real-time multiplayer web application with shared domain engine  
**Performance Goals**: Keep sell/auction actions within the existing interactive turn-update cadence for a single game session, with authoritative state broadcast after each action and no extra client recomputation loops  
**Constraints**: Server remains authoritative; advisory sale-impact values must be derived from shared services; use MudBlazor components for UI; preserve current purchase and fee-resolution flow unless the spec explicitly changes it; persist auction state so reconnections can resume cleanly  
**Scale/Scope**: Single game instances with the standard Rail Baron map data, dozens of railroads, and typical 2-6 player multiplayer sessions

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Gameplay Fidelity**: Pass. Forced sale and auction logic stays inside the authoritative engine and fee-resolution flow so rule behavior remains explicit and testable. The design treats selling at half price and auction fallback as formal game rules rather than UI shortcuts.
- **Real-Time Multiplayer First**: Pass. Auction turns, bids, passes, dropouts, sale outcomes, and elimination states are modeled as authoritative server actions and persisted game events so all clients see the same auction state in order.
- **Simplicity & Ship Fast**: Pass with constraints. The plan extends the existing `UseFees` phase, `PlayerAction` pipeline, `GameEngineService`, and board-state mapping rather than adding a new project or parallel auction subsystem.
- **Advisory Outputs Are Derived, Not Decisive**: Pass. Sale-impact and network-tab projections reuse the existing network coverage services and remain informational only; the engine still decides ownership, cash transfer, fee resolution, and elimination.
- **Stable Guidance Becomes Standard**: No constitutional promotion identified. The design relies on existing Boxcars standards for authoritative engine logic and MudBlazor-based UI; any new sell-flow notes remain feature-specific unless repeated elsewhere.

**Post-Design Re-check**: Pass. The Phase 1 data model and contract keep auction state in persisted turn data, route all multiplayer actions through the existing action queue, and avoid introducing client-only rule logic or new architectural layers.

## Project Structure

### Documentation (this feature)

```text
specs/001-sell-railroads/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── forced-sale-auction-contract.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── Boxcars/
│   ├── Components/
│   │   ├── GameBoard/
│   │   ├── Map/
│   │   └── Pages/
│   │       └── GameBoard.razor
│   ├── Data/
│   ├── GameEngine/
│   │   ├── GameEngineService.cs
│   │   └── PlayerAction.cs
│   ├── Hubs/
│   │   └── GameHub.cs
│   └── Services/
│       ├── GameBoardStateMapper.cs
│       ├── GameService.cs
│       └── NetworkCoverageService.cs
└── Boxcars.Engine/
    ├── Domain/
    │   ├── GameEngine.cs
    │   ├── Player.cs
    │   └── Turn.cs
    ├── Events/
    │   └── DomainEvents.cs
    └── Persistence/
        └── GameState.cs

tests/
└── Boxcars.Engine.Tests/
    ├── Fixtures/
    └── Unit/
```

**Structure Decision**: Use the existing Blazor web app plus shared engine structure. Domain rules and persisted turn state belong in `src/Boxcars.Engine`, action processing and event persistence belong in `src/Boxcars/GameEngine`, and sale/network UI state belongs in `src/Boxcars/Data`, `src/Boxcars/Services`, and `src/Boxcars/Components`.

## Complexity Tracking

No constitution violations currently require exception handling.
