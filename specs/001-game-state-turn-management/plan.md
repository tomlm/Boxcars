# Implementation Plan: Game State and Turn Management Cleanup

**Branch**: `[001-game-state-turn-management]` | **Date**: 2026-03-07 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/001-game-state-turn-management/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Clean up the game-board turn loop so the server-restored game snapshot drives the board reliably after reload, the active player gets live move/cost feedback while planning segments, arrival state is surfaced clearly, and only the controlling participant can mutate that turn. The implementation stays within the existing Blazor Server + MudBlazor + SignalR + Azure Table flow by reusing `GameEventEntity.SerializedGameState` as the authoritative reload source, extending the action/event payloads for partial-turn route state, decomposing the `GameBoard` page into focused turn-management UI sections, and tightening server-side authorization around player-slot ownership instead of trusting display names alone.

## Technical Context

**Language/Version**: C# 12 / .NET 8 LTS  
**Primary Dependencies**: ASP.NET Core Blazor Server, MudBlazor, ASP.NET Core SignalR, Azure.Data.Tables, `Boxcars.Engine` domain model, `System.Text.Json`  
**Storage**: Azure Table Storage `GamesTable` with `GameEntity` setup row and `GameEventEntity` event rows containing `EventData` and `SerializedGameState`  
**Testing**: xUnit via `tests/Boxcars.Engine.Tests` and `tests/Boxcars.GameEngine.Tests`; targeted component/service coverage for turn orchestration and snapshot restore behavior  
**Target Platform**: Server-rendered web app for modern desktop/mobile browsers  
**Project Type**: Blazor Server web application plus shared engine/domain library  
**Performance Goals**: Board reload restores from the latest persisted event in one load cycle; turn-planning feedback updates in the same interaction cycle; committed turn actions broadcast state changes to connected clients immediately after persistence  
**Constraints**: Server-authoritative game rules and turn validation; no raw HTML or inline styles in new UI; use MudBlazor layout/components; async I/O with `CancellationToken`; preserve official Rail Baron movement, arrival, payout, and turn-order rules; reuse existing table layout instead of adding tables unless justified  
**Scale/Scope**: One game board flow spanning `GameBoard`, map interaction, game-engine action processing, and event persistence for 2-6 players with an append-only event history per game

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Research Gate (v1.6.0)

| Principle | Status | Notes |
|-----------|--------|-------|
| **I. Gameplay Fidelity** | ✅ PASS | The plan keeps official Rail Baron movement exhaustion, destination arrival, payout resolution, and turn advancement authoritative in the engine/service layer. Any arrival buy prompt remains a UI surface over existing rule flow rather than a rule change. |
| **II. Real-Time Multiplayer** | ✅ PASS | Reload uses persisted server snapshots; live actions remain server-authoritative and broadcast through the existing SignalR game hub. Player-choice restrictions are enforced server-side, not just disabled in the UI. |
| **III. Simplicity** | ✅ PASS | Reuses current `GameBoard`, `MapComponent`, `GameEngineService`, `GameEventEntity`, and `GameState` pipeline. No new project or storage table is required. UI cleanup is handled by component decomposition inside the existing app. |
| **Technology Stack** | ✅ PASS | Matches current stack: .NET 8, Blazor Server, MudBlazor, Azure Table Storage, SignalR, xUnit. |
| **Naming Conventions** | ✅ PASS | Existing `GamesTable`/`GameEventEntity` naming remains valid; any new DTOs/components will use plural collection names and domain-oriented component names. |
| **Coding Conventions** | ✅ PASS | Planning assumes async storage and hub work with `CancellationToken`, extension-method LINQ style, and no blocking calls. |
| **Blazor UI Conventions** | ✅ PASS | UI work is explicitly componentized and MudBlazor-based, with no inline styles and parameter/event-callback communication between board sections. |

### Post-Design Gate

| Principle | Status | Notes |
|-----------|--------|-------|
| **I. Gameplay Fidelity** | ✅ PASS | Data model keeps current-trip traveled segments, selected route segments, arrival payout, and move exhaustion explicit so reload and turn completion reflect official rules consistently. |
| **II. Real-Time Multiplayer** | ✅ PASS | Design introduces a player-control binding in the action contract so authenticated participants can only mutate their own active player while all clients still observe current partial-turn state. |
| **III. Simplicity** | ✅ PASS | The design preserves the current event-sourced snapshot model. It adds focused DTO fields and UI components rather than a new orchestration layer. |

**No constitution violations requiring complexity justification.**

## Project Structure

### Documentation (this feature)

```text
specs/001-game-state-turn-management/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── turn-management-ui-contract.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/Boxcars/
├── Components/
│   ├── Pages/
│   │   └── GameBoard.razor
│   └── Map/
│       ├── GameMapComponent.razor
│       ├── MapComponent.razor
│       └── PlayerBoard.razor
├── Data/
│   ├── GameEntity.cs
│   ├── GameEventEntity.cs
│   ├── PlayerBoardModel.cs
│   └── Maps/
│       └── PlayerMapState.cs
├── GameEngine/
│   ├── IGameEngine.cs
│   ├── PlayerAction.cs
│   └── GameEngineService.cs
├── Hubs/
│   └── GameHub.cs
└── Services/
    └── GameService.cs

src/Boxcars.Engine/
├── Domain/
│   ├── GameEngine.cs
│   ├── Player.cs
│   └── Turn.cs
├── Events/
│   └── DomainEvents.cs
└── Persistence/
    └── GameState.cs

tests/
├── Boxcars.Engine.Tests/
└── Boxcars.GameEngine.Tests/
```

**Structure Decision**: Reuse the existing Blazor web app and `Boxcars.Engine` domain library. This feature is a cross-cutting turn-management slice, so changes stay in the current page/components, the in-app game-engine service/action contracts, and the snapshot DTOs already used for Azure Table persistence and reload.

## Design Focus

### Server-authoritative reload

- Keep latest `GameEventEntity.SerializedGameState` as the only authoritative reload source.
- Extend the event/action payloads only where needed so partial-turn route selections and committed movement can be reapplied visually without reconstructing from heuristics.
- Preserve current-trip traveled markers from `UsedSegments` and expose any additional selected-segment preview state separately from completed travel history.

### Turn-planning feedback

- Separate committed travel history from in-progress route selection.
- Surface moves left, selected-segment count, and fee estimate through a dedicated turn-status component instead of embedding everything in one text line.
- Keep local preview feedback immediate in the map component, but validate committed move/end-turn actions against the restored server snapshot.

### Player control enforcement

- Replace display-name-only action ownership with a stable player-control contract derived from `GameEntity.PlayersJson` and the authenticated user identity.
- Maintain client-side disabled states for usability, but treat them as advisory only.

## Complexity Tracking

No constitution violations requiring justification.
