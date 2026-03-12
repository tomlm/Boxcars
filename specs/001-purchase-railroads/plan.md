# Implementation Plan: Purchase Phase Buying and Map Analysis

**Branch**: `001-purchase-railroads` | **Date**: 2026-03-10 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `/specs/001-purchase-railroads/spec.md`

## Summary

Implement the purchase phase as a real, server-authoritative buying workflow that lets the active player complete exactly one purchase action: buy one railroad or buy one eligible engine upgrade. The design keeps the existing turn-processing pipeline (`PurchaseRailroadAction`, `BuyEngineAction`, `DeclinePurchaseAction`) and existing purchase phase in the engine, replaces the stubbed purchase dialog with inline page controls composed of an options combobox, BUY button, DECLINE button, synchronized map selection, and railroad overlay information, keeps the `Map` and `Information` tabs, adds current/projected network coverage calculations for railroad selections, introduces map-analysis services that compute railroad/city/region/trip summary data for both player reference and recommendation inputs, and introduces typed game settings for the Superchief price so the server validates engine upgrade prices from configuration rather than hardcoded values.

## Technical Context

**Language/Version**: C# 12 / .NET 8 (LTS)  
**Primary Dependencies**: Blazor Server, MudBlazor, ASP.NET Core SignalR, Azure.Data.Tables, existing `Boxcars.Engine` domain model  
**Storage**: Azure Table Storage for persisted game snapshots/events; app configuration via `appsettings.json` / `appsettings.Development.json`  
**Testing**: xUnit in `tests/Boxcars.Engine.Tests`; targeted component/service tests if already supported by repo patterns, otherwise engine/service regression tests  
**Target Platform**: Modern desktop/mobile browsers via Blazor Server  
**Project Type**: Web application (`src/Boxcars`) plus shared engine library (`src/Boxcars.Engine`)  
**Performance Goals**: Purchase controls and selection changes should feel immediate; projected coverage recalculation and map-analysis report generation for the standard map should stay within normal interactive server latency and not require extra persistence round-trips  
**Constraints**: Server-authoritative purchase validation, MudBlazor-first UI, no inline CSS, tabbed Map/Information experience, inline taskbar purchase controls, synchronized map/combobox selection, configurable Superchief price from app settings, preserve existing purchase→use-fees turn flow  
**Scale/Scope**: Standard Rail Baron play (2–6 players, 28 railroads, one active purchase phase at a time, standard U21 map and current event-sourced game state)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Research Gate (v1.7.0)

| Principle | Status | Notes |
|-----------|--------|-------|
| **I. Gameplay Fidelity** | ✅ PASS | Purchase phase remains a rule-enforced server action. Railroad purchases, engine upgrades, affordability checks, and fee-step advancement stay aligned with current Rail Baron turn rules. Superchief pricing is explicitly surfaced as a configurable rule value instead of an implicit constant. |
| **II. Real-Time Multiplayer** | ✅ PASS | UI remains a client of the server-authoritative action queue. Purchase confirmation still flows through `GameEngineService` and persisted game events, preserving multiplayer synchronization. |
| **III. Simplicity** | ✅ PASS | Reuses existing purchase phase, player actions, and map component. Adds focused network-analysis and settings services instead of new architectural layers or a new frontend stack. Replaces the modal concept with page-owned controls rather than introducing a dialog-service workflow. |
| **Technology Stack** | ✅ PASS | Uses existing .NET 8, Blazor Server, MudBlazor, SignalR, and Azure Table Storage stack. |
| **Naming Conventions** | ✅ PASS | New models/services can follow existing domain-focused naming (`PurchaseOption`, `NetworkCoverageSnapshot`, `GameRulesOptions`). |
| **Coding Conventions** | ✅ PASS | Configuration binding and persistence-facing code remain async where needed; engine/domain mutations stay synchronous. |
| **Blazor UI Conventions** | ✅ PASS | Uses MudBlazor components, keeps the purchase UI decomposed into its own component, and avoids inline styling. |

### Post-Design Gate

| Principle | Status | Notes |
|-----------|--------|-------|
| **I. Gameplay Fidelity** | ✅ PASS | Design keeps one purchase action per phase, preserves server-side affordability enforcement, and avoids client-derived outcomes. Current/projected network stats are informational only and do not alter rules. |
| **II. Real-Time Multiplayer** | ✅ PASS | All committed purchases still flow through `GameEngineService.ProcessTurn()`, persisted events, and shared state broadcasts. Selection/highlight state remains client/session-scoped until a purchase action is confirmed. |
| **III. Simplicity** | ✅ PASS | The design introduces one typed options object for purchase prices plus targeted map-analysis services reusing existing map graph data. No extra projects, no speculative rule engine abstraction, and no separate dialog service workflow. |

**No constitution violations requiring complexity justification.**

## Project Structure

### Documentation (this feature)

```text
specs/001-purchase-railroads/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── purchase-phase-ui-contract.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/Boxcars/
├── Program.cs
├── appsettings.json
├── appsettings.Development.json
├── Components/
│   ├── GameBoard/
│   │   ├── PurchaseTaskbar.razor
│   │   ├── PurchaseTaskbar.razor.css
│   │   ├── RailroadPurchaseOverlay.razor
│   │   └── PurchaseAnalysisReport.razor
│   ├── Map/
│   │   └── GameMapComponent.razor
│   └── Pages/
│       └── GameBoard.razor
├── Data/
│   ├── ArrivalResolutionModel.cs
│   ├── BoardTurnViewState.cs
│   ├── PurchasePhaseModel.cs
│   ├── PurchaseOptionModel.cs
│   ├── NetworkCoverageModel.cs
│   └── MapAnalysisModel.cs
├── GameEngine/
│   ├── GameEngineService.cs
│   └── PlayerAction.cs
└── Services/
    ├── GameBoardStateMapper.cs
    ├── NetworkCoverageService.cs
    ├── MapAnalysisService.cs
    ├── PurchaseRecommendationService.cs
    ├── PurchaseRulesOptions.cs
    ├── Maps/
    │   ├── BoardViewportService.cs
    │   └── MapRouteService.cs

src/Boxcars.Engine/
├── Domain/
│   ├── Enums.cs
│   ├── GameEngine.cs
│   └── Player.cs
├── Events/
└── Persistence/

tests/Boxcars.Engine.Tests/
├── Fixtures/
│   └── GameEngineFixture.cs
└── Unit/
    ├── RailroadPurchaseTests.cs
    ├── LocomotiveUpgradeTests.cs
    ├── PurchasePhaseActionTests.cs
    ├── PurchaseRulesConfigurationTests.cs
    └── MapAnalysisTests.cs
```

**Structure Decision**: Reuse the existing Blazor Server app and `Boxcars.Engine` library. UI orchestration lives in `src/Boxcars`, authoritative purchase validation remains in `src/Boxcars.Engine` plus `src/Boxcars/GameEngine/GameEngineService.cs`, and regression tests extend the existing engine test suite. New support code should stay narrowly scoped: purchase-phase and map-analysis UI models in `Data`, network/report/recommendation calculation in `Services`, and configurable price rules wired from `Program.cs` and app settings.

## Phase 0 Research Summary

- Keep purchase confirmation on the existing player-action path instead of inventing a parallel purchase API.
- Replace the stubbed purchase dialog with inline page-owned controls that remain tied to `GameBoard.razor`, the live map, and the existing taskbar, with explicit BUY and DECLINE actions.
- Compute current/projected network coverage and the reference report from existing map graph structures rather than adding a separate graph persistence model.
- Add a tabbed `Map` / `Information` purchase UX so the player can switch between live purchase selection and the computed reference report while keeping selection synchronized.
- Move Superchief pricing into typed configuration so both the UI and server validation read the same authoritative value.

## Phase 1 Design Summary

- Expand the arrival/purchase presentation model from a simple message into a purchase-phase view model that carries railroad options, engine upgrade options, selection state, taskbar state, BUY/DECLINE affordances, overlay info, tab state, analysis report data, and network coverage comparison.
- Introduce a dedicated coverage calculator that can evaluate the active player's current railroad network and a hypothetical railroad purchase using map definitions and player-owned railroad indices.
- Introduce a map-analysis service that computes railroad summary rows, city access percentages, region probabilities, and trip-level averages from the loaded map for both the Information tab and recommendation logic.
- Treat the sample report values as illustrative only; preserve the same report categories rather than exact sample-number parity.
- Introduce a recommendation-input layer that consumes the same analysis dataset shown in the UI instead of reparsing rendered text.
- Introduce typed purchase settings for engine upgrade pricing, with Express fixed at $4000 and Superchief bound from configuration with a default of $40000.
- Keep engine and railroad purchases mutually exclusive in one purchase phase, with taskbar/map selection synchronization and the engine/domain layer remaining the final validator for committed actions.

## Complexity Tracking

No constitution violations requiring justification.
