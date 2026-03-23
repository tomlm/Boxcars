# Implementation Plan: Game Creation Settings

**Branch**: `001-game-settings` | **Date**: 2026-03-21 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-game-settings/spec.md`

## Summary

Add an immutable typed game-settings object to the create-game flow, persist each setting on direct nullable `GameEntity` properties, resolve defaults for legacy rows and missing values, and thread the resulting runtime settings through the authoritative engine, board projections, and advice/UI mapping so cash thresholds, fees, home-selection behavior, cash visibility, starting engine, and engine prices all come from the saved game instead of hard-coded defaults or app-wide options.

## Technical Context

**Language/Version**: C# / .NET 10  
**Primary Dependencies**: Blazor Server, MudBlazor, Azure.Data.Tables, ASP.NET Core SignalR  
**Storage**: Azure Table Storage via direct `GameEntity` setting columns in `GamesTable` plus existing event snapshot persistence  
**Testing**: xUnit with `dotnet test` in `tests/Boxcars.Engine.Tests` and targeted regression coverage for engine and service layers  
**Target Platform**: Modern desktop and mobile web browsers backed by a server-authoritative Blazor Server app  
**Project Type**: Blazor Server web application with a shared engine library  
**Performance Goals**: Preserve current turn-processing behavior and realtime updates; settings resolution must be constant-time per game load and must not add extra round trips during turn execution  
**Constraints**: Settings are immutable after game start, server-authoritative, legacy games without persisted settings must continue using defaults, UI must follow MudBlazor-only conventions, async I/O must continue to propagate `CancellationToken`  
**Scale/Scope**: One feature spanning create-game UI, request/persistence models, authoritative engine construction/restoration, board state mapping, advisory surfaces, and regression tests

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

**Pre-research gate**

- **Gameplay Fidelity**: Pass. Cash thresholds, route-fee rules, engine prices, and home-selection behavior will move into one authoritative per-game rules object so house-rule variation remains explicit and separated from the core engine rather than implemented ad hoc in UI code.
- **Real-Time Multiplayer First**: Pass. Settings remain server-owned, persisted with the game, and used by the server engine and projections that already broadcast authoritative state.
- **Simplicity & Ship Fast**: Pass. The design keeps settings on the existing `GameEntity` row and current create-game flow instead of introducing new tables or a separate settings service.
- **Advisory Outputs Are Derived, Not Decisive**: Pass. Board previews, purchase options, advice text, and cash-display logic will read the same resolved game settings used by server-side rule resolution.
- **Stable Guidance Becomes Standard**: No constitutional promotion proposed. The guidance here is feature-local: per-game rule values belong in persisted game settings and should be resolved server-side.

**Post-design re-check**

- Pass. Phase 1 keeps immutable rules on direct nullable `GameEntity` columns, resolves them once per game load with legacy fallback into a typed runtime settings object, and passes that object into the existing authoritative engine/service seams.
- No constitution violations require justification. The design avoids a new persistence model, avoids client-side rule engines, and keeps rule variation explicit and auditable.

## Project Structure

### Documentation (this feature)

```text
specs/001-game-settings/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── create-game-settings-contract.md
│   └── game-settings-runtime-contract.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── Boxcars/
│   ├── Components/
│   │   └── Pages/
│   │       └── CreateGame.razor
│   ├── Data/
│   │   ├── GameCreationModels.cs
│   │   ├── GameEntity.cs
│   │   └── PlayerBoardModel.cs
│   ├── GameEngine/
│   │   └── GameEngineService.cs
│   ├── Services/
│   │   ├── GameBoardAdviceService.cs
│   │   ├── GameBoardStateMapper.cs
│   │   ├── GameService.cs
│   │   └── PurchaseRulesOptions.cs
│   └── Program.cs
├── Boxcars.Engine/
│   ├── Domain/
│   │   ├── GameEngine.cs
│   │   └── Player.cs
│   └── Persistence/
│       └── GameState.cs
└── Boxcars.GameEngine/

tests/
├── Boxcars.Engine.Tests/
└── Boxcars.GameEngine.Tests/
```

**Structure Decision**: Keep the feature within the existing Blazor app, authoritative engine library, and existing test projects. Extend `GameEntity` with direct persisted setting properties and reuse current service/engine boundaries rather than introducing new projects or storage tables.

## Complexity Tracking

No constitution exceptions or added architectural complexity require tracking for this feature.
