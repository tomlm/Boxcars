# Implementation Plan: Redo Table Storage

**Branch**: `002-redo-table-storage` | **Date**: 2026-03-06 | **Spec**: `/specs/002-redo-table-storage/spec.md`
**Input**: Feature specification from `/specs/002-redo-table-storage/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Consolidate persistence to two Azure Table Storage tables (`UsersTable`, `GamesTable`) with three domain entities (`UserEntity`, `GameEntity`, `GameEventEntity`), route all UI-triggered game mutations through the server-authoritative game engine, and align the create-game journey to explicit player-slot + color selection before game creation. Remove/de-scope storage tables and code paths outside this model while preserving multiplayer reconnection and event timeline fidelity.

## Technical Context

**Language/Version**: C# / .NET 8 (`net8.0`)  
**Primary Dependencies**: ASP.NET Core Blazor Server, ASP.NET Core SignalR, Azure.Data.Tables, Microsoft Fluent UI Blazor  
**Storage**: Azure Table Storage (`UsersTable`, `GamesTable`)  
**Testing**: `dotnet test` with xUnit test projects (`tests/Boxcars.Engine.Tests`, `tests/Boxcars.GameEngine.Tests`) plus focused integration checks for storage flows  
**Target Platform**: Modern web browsers via Blazor Server
**Project Type**: Web application (single server-rendered app with engine library projects)  
**Performance Goals**: Preserve spec SC-001..SC-004, especially reliable event persistence before broadcast and reconnect resume from latest snapshot  
**Constraints**: Server-authoritative multiplayer state, async I/O with `CancellationToken`, Fluent UI components only for UI, no additional storage tables beyond scope  
**Scale/Scope**: Existing multiplayer game flows; one create-game flow update; migration/removal of unused table/entity access paths in current app

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Research Gate Review

- **Gameplay Fidelity (Principle I)**: PASS — Storage redesign does not alter Rail Baron rules; it changes persistence shape and recovery mechanics only.
- **Real-Time Multiplayer First (Principle II)**: PASS — Game engine remains server authority for all UI-triggered actions and event publication.
- **Simplicity & Ship Fast (Principle III)**: PASS — Reduces storage complexity by collapsing to two tables and removing auxiliary table/index entities.
- **Tech Stack & UI Constraints**: PASS — Remains in C#/.NET, Blazor Server, SignalR, Azure Table Storage, Fluent UI components.

### Post-Design Gate Review (after Phase 1 artifacts)

- **Gameplay Fidelity (Principle I)**: PASS — Data model and contracts preserve deterministic event order/history without rule changes.
- **Real-Time Multiplayer First (Principle II)**: PASS — Contracts require persistence by game engine before client broadcast; reconnect resumes from persisted snapshots.
- **Simplicity & Ship Fast (Principle III)**: PASS — Design explicitly removes legacy tables/entities and avoids new projects/frameworks.
- **Violations**: None.

## Project Structure

### Documentation (this feature)

```text
specs/[###-feature]/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/
├── Boxcars/
│   ├── Components/
│   │   └── Pages/
│   │       ├── Dashboard.razor
│   │       ├── CreateGame.razor
│   │       └── GameBoard.razor
│   ├── Data/
│   │   └── (table entity records and constants)
│   ├── Identity/
│   │   └── TableStorageUserStore.cs
│   ├── Services/
│   │   ├── PlayerProfileService.cs
│   │   └── GameService.cs
│   ├── GameEngine/
│   │   ├── GameEngineService.cs
│   │   └── GameStateBroadcastService.cs
│   └── Program.cs
├── Boxcars.Engine/
└── Boxcars.GameEngine/

tests/
├── Boxcars.Engine.Tests/
└── Boxcars.GameEngine.Tests/

specs/
└── 002-redo-table-storage/
    ├── spec.md
    ├── plan.md
    ├── research.md
    ├── data-model.md
    ├── quickstart.md
    └── contracts/
```

**Structure Decision**: Keep the existing single Blazor Server application plus engine libraries and tests. Implement storage consolidation and create-game flow changes inside current `src/Boxcars` services, identity, data, and page components.

## Phase 0 Output

- `research.md` resolves implementation choices for key schema shape, event ordering, snapshot strategy, Beatles seed behavior, and create-game validation constraints.

## Phase 1 Output

- `data-model.md` defines `UserEntity`, `GameEntity`, `GameEventEntity`, relationships, validation rules, and state transitions.
- `contracts/storage-and-gameflow-contract.md` defines external behavior for authentication profile sync, game creation persistence, event persistence/broadcast ordering, and reconnect load semantics.
- `quickstart.md` defines end-to-end validation scenarios mapped to P1/P2/P3 user stories.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| None | N/A | N/A |
