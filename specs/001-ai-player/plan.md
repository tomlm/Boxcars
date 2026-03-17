# Implementation Plan: AI-Controlled Player Turns

**Branch**: `001-ai-player` | **Date**: 2026-03-16 | **Spec**: `/specs/001-ai-player/spec.md`
**Input**: Feature specification from `/specs/001-ai-player/spec.md`

## Summary

Add a global dashboard-managed bot library plus in-game bot assignment for disconnected seats already under delegated control. The implementation keeps all bot orchestration server-side: dashboard CRUD persists bot definitions in Azure Table storage, game state stores active assignment references, the server builds a phase-specific decision prompt from authoritative state and the selected bot strategy, OpenAI resolves only the eligible decision phases, and the server validates, falls back, records, and broadcasts bot actions through the existing game-history and SignalR flow.

## Technical Context

**Language/Version**: C# / .NET 10 for the web app, .NET 8 xUnit test projects  
**Primary Dependencies**: ASP.NET Core Blazor Server, MudBlazor, SignalR, Azure.Data.Tables, ASP.NET Core Identity, `HttpClient` for OpenAI API access  
**Storage**: Azure Table storage with a new `BotsTable` for global bot definitions plus persisted bot-assignment metadata inside game records in `GamesTable`; transient connection ownership remains in `GamePresenceService`  
**Testing**: xUnit test projects in `tests/Boxcars.Engine.Tests` and `tests/Boxcars.GameEngine.Tests`, plus targeted service/component validation where practical  
**Target Platform**: Blazor Server web app for modern desktop and mobile browsers  
**Project Type**: Real-time multiplayer web application  
**Performance Goals**: Bot decisions must not stall turn progression; OpenAI-backed phases should resolve within a bounded server timeout and always produce a legal committed outcome or deterministic fallback  
**Constraints**: Preserve Rail Baron rules, keep server authority, propagate all visible changes via existing SignalR flows, use MudBlazor-first UI, avoid inline styles, use async I/O with `CancellationToken`, keep configuration keyed by `OpenAIKey`  
**Scale/Scope**: Global bot library shared by all signed-in users, multiple concurrent games with multiple assigned bots, limited to destination-region, purchase, auction, movement reuse, and deterministic sell support

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Gameplay Fidelity**: Bot automation does not invent new game rules. Legal choices are derived from the same authoritative engine state already used for humans, movement reuses existing suggested-path behavior, and sell fallback stays deterministic and rule-compliant.
- **Real-Time Multiplayer First**: Clients only surface dashboard and assignment UI. Bot decisions, validation, fallback selection, persistence, and turn progression remain server-side, then flow through the existing SignalR broadcast and game-history pipeline.
- **Simplicity & Ship Fast**: The design extends current dashboard, delegated-control, and table-storage patterns instead of adding a second automation service boundary or client-side AI logic. OpenAI integration uses existing `HttpClient` and typed configuration rather than a larger SDK abstraction.
- **Advisory Outputs Are Derived, Not Decisive**: The AI prompt is derived from authoritative state, but the returned choice is never decisive on its own. The server re-validates against current legal actions and substitutes deterministic fallbacks when required.
- **Stable Guidance Becomes Standard**: This feature may establish a reusable pattern for external decision providers using authoritative game snapshots plus validated fallbacks, but that guidance is still feature-local until another feature needs the same external orchestration model.

**Post-Design Re-check**: Pass. The design keeps shared state minimal, stores durable bot data in explicit tables/records, and does not introduce any constitutional violation requiring exception tracking.

## Project Structure

### Documentation (this feature)

```text
specs/001-ai-player/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── dashboard-bot-library-contract.md
│   └── gameplay-bot-assignment-contract.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── Boxcars/
│   ├── Components/
│   │   ├── Map/
│   │   └── Pages/
│   ├── Data/
│   ├── GameEngine/
│   ├── Identity/
│   ├── Services/
│   └── Program.cs
├── Boxcars.Engine/
└── Boxcars.GameEngine/

tests/
├── Boxcars.Engine.Tests/
└── Boxcars.GameEngine.Tests/
```

**Structure Decision**: Extend the existing Blazor Server app in `src/Boxcars` for dashboard UI, delegated-control UI, storage entities, configuration, and orchestration services. Keep rule-facing and turn-resolution logic close to the current game engine/service layer, and cover deterministic fallback logic in the existing xUnit test projects.

## Complexity Tracking

No constitution exceptions or extra complexity justifications are required at planning time.
