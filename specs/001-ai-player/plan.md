# Implementation Plan: AI-Controlled Player Turns

**Branch**: `001-ai-player` | **Date**: 2026-03-17 | **Spec**: `S:\github\Boxcars\specs\001-ai-player\spec.md`
**Input**: Feature specification from `S:\github\Boxcars\specs\001-ai-player\spec.md`

## Summary

Refactor AI turn ownership so dedicated bot seats and ghost-controlled human seats execute entirely through the server-side automatic turn loop. Keep the rule engine's player model unchanged, introduce an explicit seat-controller mode in application data, remove the requirement that bot seats be represented through delegated human control, and preserve existing multiplayer/event-history behavior.

## Technical Context

**Language/Version**: C# / .NET 10.0  
**Primary Dependencies**: ASP.NET Core Blazor Server, SignalR, MudBlazor, Azure.Data.Tables, built-in `HttpClient`-based OpenAI client  
**Storage**: Azure Table storage (`GamesTable`, `BotsTable`, existing event/game entities)  
**Testing**: xUnit via `tests/Boxcars.Engine.Tests` and targeted service-level regression coverage  
**Target Platform**: Blazor Server web app for modern desktop/mobile browsers  
**Project Type**: Server-authoritative multiplayer web application  
**Performance Goals**: AI-controlled turns must resolve without blocking the table beyond the configured automatic bot delay and OpenAI timeout windows  
**Constraints**: Preserve server authority; do not expose OpenAI credentials to clients; keep replay/history fidelity; maintain graceful reconnect semantics; avoid requiring a human to "take control" of dedicated bot seats  
**Scale/Scope**: 2-8 concurrent players per game, multiple simultaneous games, mixed human/direct, human/delegated, dedicated bot, and ghost-controlled seats

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Gameplay Fidelity**: The refactor does not change turn legality or rule outcomes. It changes only who authors eligible actions and how AI ownership is modeled outside the engine. Forced-sale, movement, auction, and purchase validation remain in the authoritative engine path.
- **Real-Time Multiplayer First**: AI execution remains server-side in `GameEngineService.AdvanceAutomaticTurnFlowAsync`, ensuring one authoritative action source across all clients and preventing duplicate client-side AI execution.
- **Simplicity & Ship Fast**: The design keeps the engine player model intact and adds one focused seat-controller concept in app-layer data rather than introducing separate engine player subclasses. Existing `BotAssignment` and presence infrastructure are evolved rather than replaced wholesale.
- **Advisory Outputs Are Derived, Not Decisive**: Movement suggestions and purchase/sell analysis remain advisory helpers. AI decisions continue to be validated against authoritative legal options before commit.
- **Stable Guidance Review**: If the seat-controller model proves durable across both dedicated bot seats and ghost mode, it may qualify as stable orchestration guidance later. For now it remains feature-specific planning guidance and should not be promoted to the constitution yet.

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
│   └── Services/
├── Boxcars.Engine/
│   ├── Domain/
│   └── Persistence/
└── Boxcars.GameEngine/

tests/
├── Boxcars.Engine.Tests/
└── Boxcars.GameEngine.Tests/
```

**Structure Decision**: Keep the implementation inside the existing Blazor web app orchestration layer (`src/Boxcars`) and its current test projects. Do not introduce a new project. The rule engine remains unchanged except for any validation hooks required to recognize server-authored AI actions.

## Phase 0: Research Consolidation

1. Confirm the seat-controller model that distinguishes `HumanDirect`, `HumanDelegated`, `AiBotSeat`, and `AiGhost` without changing engine player types.
2. Confirm how server-authored AI actions should be authorized inside `GameEngineService` while preserving the existing human action validation path.
3. Confirm which current `BotAssignment` fields are durable requirements versus artifacts of the old delegated-control model.

## Phase 1: Design

### Data model changes

- Add explicit seat-controller mode data in the application layer.
- Refactor `BotAssignment` so dedicated bot seats do not require a human delegated controller while ghost mode still records enough provenance to stop on reconnect/release.
- Preserve live references from gameplay automation to global bot definitions in `BotsTable`.

### Server orchestration changes

- Make `GameEngineService.AdvanceAutomaticTurnFlowAsync` the sole execution point for AI turns.
- Remove the requirement that dedicated bot seats bootstrap delegated control in `BotTurnService.EnsureBotSeatAssignmentsAsync`.
- Introduce a server-owned actor identity for AI-authored actions and update authorization rules accordingly.

### UI/state-mapping changes

- Update player-card and board state mapping to surface controller mode rather than equating bot seats with delegated human control.
- Ensure dedicated bot seats never show `TAKE CONTROL` as a prerequisite for normal turn execution.
- Keep ghost-mode controls limited to disconnected human seats.

### Testing design

- Add regression coverage for dedicated bot seats acting with no delegated controller.
- Add regression coverage for ghost mode on disconnected human seats.
- Add regression coverage for reconnect/release transitions and server-authored AI action authorization.

## Phase 2: Implementation Strategy

1. Introduce the seat-controller mode model and controller-resolution helper in the app/data layer.
2. Refactor `BotAssignment` persistence and validation rules to separate dedicated bot seats from ghost-controlled human seats.
3. Update bot action creation to use server-owned AI authorship instead of human `ControllerUserId` authorship.
4. Update `GameEngineService` authorization and automatic-turn branching to honor explicit controller mode.
5. Update board/player-card state mapping and control visibility to remove the dedicated-bot "take control" assumption.
6. Add regression tests before removing the remaining delegated-bot compatibility code.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Application-layer seat controller mode | Needed to separate bot seats from ghost mode without changing the engine player model | Reusing delegated human control for bot seats conflates human authority with server-owned AI execution and keeps the current awkward UX |
