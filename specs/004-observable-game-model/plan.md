# Implementation Plan: Observable Game Engine Object Model

**Branch**: `004-observable-game-model` | **Date**: 2026-03-04 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `/specs/004-observable-game-model/spec.md`

## Summary

Build the `GameEngine` as an in-memory observable object model that exposes all Rail Baron game state via `INotifyPropertyChanged` properties, `ObservableCollection<T>` collections, and C# domain events. The engine is a plain C# class (not a DI service) with synchronous action methods (`RollDice`, `MoveAlongRoute`, `DrawDestination`, `SuggestRoute`, `SaveRoute`, `BuyRailroad`, `AuctionRailroad`, etc.) that validate preconditions, mutate state, and fire notifications on the same call path. State is serializable to JSON for Azure Table Storage persistence and restorable into a fully functional instance. A pluggable `IRandomProvider` enables deterministic testing.

## Technical Context

**Language/Version**: C# 12 / .NET 8 (LTS)  
**Primary Dependencies**: `System.ComponentModel` (INotifyPropertyChanged), `System.Collections.ObjectModel` (ObservableCollection), `System.Text.Json` (serialization). No third-party dependencies for the engine library itself.  
**Storage**: Azure Table Storage — game state serialized as JSON blob property via `System.Text.Json`  
**Testing**: xUnit (existing `Boxcars.GameEngine.Tests` project scaffolded)  
**Target Platform**: Blazor Server (.NET 8) — engine runs server-side, Blazor renders UI  
**Project Type**: Class library (`Boxcars.GameEngine`)  
**Performance Goals**: N/A — in-memory object model with no I/O on the hot path  
**Constraints**: All mutations synchronous (FR-008), single-threaded by design, JSON serializable state < 32KB for Azure Table Storage string property limit  
**Scale/Scope**: 2–6 players, 28 railroads, ~200 cities, ~2000 train dots per standard U21 map

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Research Gate (v1.5.0)

| Principle | Status | Notes |
|-----------|--------|-------|
| **I. Gameplay Fidelity** | ✅ PASS | Rules verified against https://www.railgamefans.com/rbp/rb21rules.htm. Payout tables, use fees, dice mechanics, locomotive upgrades, destination draws, declare/rover behavior, and establishment fee logic are in feature scope and must match published rules. |
| **II. Real-Time Multiplayer** | ✅ PASS | Engine is the server-authoritative state layer. Observable properties enable push-based UI updates via Blazor/SignalR. No client-side state determination. SignalR integration is a separate feature concern. |
| **III. Simplicity** | ✅ PASS | `INotifyPropertyChanged` is the simplest observable pattern in .NET. No reactive frameworks, no DI abstractions, no extra projects beyond the existing scaffold. Plain `new GameEngine(...)` construction. |
| **Technology Stack** | ✅ PASS | C# / .NET 8, Azure Table Storage (JSON blob), xUnit tests. No new external dependencies for the engine library. |
| **Naming Conventions** | ✅ PASS | Collections use plural names (`Players`, `Railroads`, `OwnedRailroads`). Storage entity uses `GamesTable` convention. |
| **Coding Conventions** | ✅ PASS | No I/O in engine (all sync). LINQ extension-method syntax. Microsoft .NET style guidelines. |
| **Blazor UI Conventions** | N/A | This feature is engine-only; no Blazor components created. |

### Post-Design Gate

| Principle | Status | Notes |
|-----------|--------|-------|
| **I. Gameplay Fidelity** | ✅ PASS | Data model maps all game entities. Action methods enforce turn phase rules. Payout table, use fees, bonus rolls, non-reuse, declare/rover behavior, and establishment fee logic are modeled to match official rules. |
| **II. Real-Time Multiplayer** | ✅ PASS | Observable properties + domain events provide the notification surface for SignalR push. `GameEngine.ToSnapshot()` supports persistence between sessions. |
| **III. Simplicity** | ✅ PASS | Single class library, no DI, no async, no reactive frameworks. `ObservableBase` is ~20 lines. `GameState` DTO for serialization avoids circular reference complexity. |

**No constitution violations requiring complexity justification.**

## Project Structure

### Documentation (this feature)

```text
specs/004-observable-game-model/
├── plan.md              # This file
├── research.md          # Phase 0: 13 research decisions
├── data-model.md        # Phase 1: Entity definitions, enums, events, snapshot DTO
├── quickstart.md        # Phase 1: Usage examples and build instructions
├── contracts/
│   └── game-engine-api.md  # Phase 1: Full public API contract
├── checklists/
│   └── requirements.md  # Spec requirements checklist
└── tasks.md             # Phase 2 output (created by /speckit.tasks)
```

### Source Code (repository root)

```text
src/Boxcars.GameEngine/
├── Boxcars.GameEngine.csproj
├── ObservableBase.cs              # Shared INotifyPropertyChanged base class
├── IRandomProvider.cs             # Random abstraction interface
├── DefaultRandomProvider.cs       # Production random implementation
├── PayoutTable.cs                 # Static payout lookup table
├── Domain/
│   ├── GameEngine.cs              # Root object model + action methods
│   ├── Player.cs                  # Player observable entity
│   ├── Railroad.cs                # Railroad observable entity
│   ├── Turn.cs                    # Turn state observable entity
│   ├── Route.cs                   # Route + RouteNode + RouteSegment (immutable)
│   ├── DiceResult.cs              # Dice roll result (immutable)
│   └── Enums.cs                   # GameStatus, TurnPhase, LocomotiveType
├── Events/
│   ├── DestinationAssignedEventArgs.cs
│   ├── DestinationReachedEventArgs.cs
│   ├── UsageFeeChargedEventArgs.cs
│   ├── AuctionStartedEventArgs.cs
│   ├── AuctionCompletedEventArgs.cs
│   ├── TurnStartedEventArgs.cs
│   ├── GameOverEventArgs.cs
│   ├── PlayerBankruptEventArgs.cs
│   └── LocomotiveUpgradedEventArgs.cs
└── Persistence/
    └── GameState.cs               # Serialization snapshot DTO

tests/Boxcars.GameEngine.Tests/
├── Boxcars.GameEngine.Tests.csproj
├── TestDoubles/
│   └── FixedRandomProvider.cs     # Deterministic random for tests
├── Fixtures/
│   └── GameEngineFixture.cs       # Shared test setup helpers
└── Unit/
    ├── InitializationTests.cs
    ├── ObservabilityTests.cs
    ├── DiceRollTests.cs
    ├── MovementTests.cs
    ├── DestinationDrawTests.cs
    ├── RouteSuggestionTests.cs
    ├── RailroadPurchaseTests.cs
    ├── AuctionTests.cs
    ├── UseFeesTests.cs
    ├── PayoutTests.cs
    ├── LocomotiveUpgradeTests.cs
    ├── WinConditionTests.cs
    ├── BankruptcyTests.cs
    ├── TurnPhaseTests.cs
    └── SerializationTests.cs
```

**Structure Decision**: Reuse the existing `Boxcars.GameEngine` class library and `Boxcars.GameEngine.Tests` test project already scaffolded in the solution. The engine project's empty subdirectories (`Contracts/`, `Persistence/`, `Randomness/`, `Rules/`) will be reorganized to match the domain-centric layout above (`Domain/`, `Events/`, `Persistence/`). The `Azure.Data.Tables` and `Microsoft.Extensions.DependencyInjection.Abstractions` NuGet references will be removed since the engine is now a plain object model with no infrastructure dependencies.

## Scope Notes

Core official-rule mechanics required for Gameplay Fidelity are included in this feature scope.

| Mechanic | Scope Decision | Notes |
|----------|----------------|-------|
| **Declare / Rover** | Included | Implement with official declare state, alternate destination, rover penalty, and undeclare behavior. |
| **Establishment** | Included | Implement fee grandfathering logic when fee tiers change. |
| **Home Swap** | Deferred | Optional quality-of-life variant; can be implemented as a clearly separated variant after core rule parity. |

## Complexity Tracking

No constitution violations requiring justification. The design uses:
- 2 projects (library + tests) — both pre-existing
- 0 new NuGet dependencies for the engine
- 0 DI registrations
- 0 async methods
- 1 interface (`IRandomProvider`) — justified by FR-023 (deterministic testing)
