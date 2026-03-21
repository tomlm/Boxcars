# Implementation Plan: AI Advice Chat

**Branch**: `001-ai-advice-chat` | **Date**: 2026-03-20 | **Spec**: `S:\github\Boxcars\specs\001-ai-advice-chat\spec.md`
**Input**: Feature specification from `S:\github\Boxcars\specs\001-ai-advice-chat\spec.md`

## Summary

Add a lower-right advisor entry point to the game board that opens a MudBlazor sidebar chat experience for the current player context. Keep the feature advisory-only by deriving each reply from the latest authoritative game snapshot and current controlled-seat context on demand, reuse the existing server-side OpenAI configuration and HTTP client infrastructure, and keep conversation history scoped to the current board page session for the MVP.

## Technical Context

**Language/Version**: C# / .NET 10.0  
**Primary Dependencies**: ASP.NET Core Blazor Server, MudBlazor, SignalR, Azure.Data.Tables, existing `HttpClient`-based OpenAI integration, existing `GameBoardStateMapper`/board projection services  
**Storage**: Azure Table storage for authoritative game and player-state rows; no new durable chat storage for MVP, session-scoped conversation state only  
**Testing**: xUnit in `tests/Boxcars.Engine.Tests`, focused service/state-mapper regression tests, plus solution build validation  
**Target Platform**: Blazor Server web app for modern desktop and mobile browsers  
**Project Type**: Server-authoritative multiplayer web application  
**Performance Goals**: Sidebar opens immediately, greeting appears without waiting on a remote model call, and advisory replies complete within the configured AI timeout while never blocking gameplay state updates  
**Constraints**: Preserve server authority; never expose OpenAI credentials to the client; use latest authoritative state per question; keep advice explicitly non-authoritative; remain usable on desktop/mobile layouts; stay within existing MudBlazor/Blazor patterns  
**Scale/Scope**: One advisory conversation per connected board page session, multiple simultaneous games, 2-8 seats per game, active-player and delegated-seat guidance driven from current session context

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Gameplay Fidelity**: The advisor is read-only and advisory. It can explain likely strategy and board position, but it cannot mutate game state or resolve rules. All answers must be derived from the same authoritative snapshot and map inputs the board already uses.
- **Real-Time Multiplayer First**: The design reuses live board state from the server-authoritative game flow. Advice requests refresh context per question so replies track the current multiplayer board rather than stale client-owned state.
- **Simplicity & Ship Fast**: The MVP uses the existing Blazor Server circuit and current OpenAI configuration rather than adding a new hub, background worker, or durable chat transcript store.
- **Advisory Outputs Are Derived, Not Decisive**: The feature is explicitly an advisory projection. It must state advice as guidance, not as authoritative rule execution, and it must reuse current server-side game-state projections.
- **Stable Guidance Review**: A reusable pattern may emerge for freeform OpenAI text responses alongside the existing option-selection client. That guidance is still feature-specific until another non-bot conversational surface needs the same abstraction.

**Post-Design Re-check**: The Phase 1 design keeps conversation history session-scoped, computes context from the live authoritative snapshot at send time, and avoids client-only rule logic. No constitution violations require special exemption.

## Project Structure

### Documentation (this feature)

```text
specs/001-ai-advice-chat/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── advice-generation-contract.md
│   └── gameplay-advisor-sidebar-contract.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── Boxcars/
│   ├── Components/
│   │   ├── GameBoard/
│   │   └── Pages/
│   ├── Data/
│   ├── Hubs/
│   └── Services/
├── Boxcars.Engine/
│   ├── Domain/
│   └── Persistence/
└── Boxcars.GameEngine/

tests/
├── Boxcars.Engine.Tests/
└── Boxcars.GameEngine.Tests/
```

**Structure Decision**: Keep implementation inside the existing Blazor app and current test projects. Add the advisor UI under `src/Boxcars/Components/GameBoard` and `src/Boxcars/Components/Pages`, add request/context models under `src/Boxcars/Data`, and add server-side advisory orchestration in `src/Boxcars/Services`. Do not introduce a new project or a separate chat backend for the MVP.

## Phase 0: Research Consolidation

1. Reuse the existing server-side OpenAI configuration and HTTP plumbing, but extend it to support freeform advisory text responses instead of only `selectedOptionId` decision payloads.
2. Keep advisory requests user-initiated from the Blazor Server board experience rather than adding a new SignalR hub or background precomputation path.
3. Build each advice request from the latest authoritative `GameState`, seat control context, and relevant player-state rows at send time so replies are board-aware and current.
4. Keep chat transcript history scoped to the current board page session for MVP to avoid unnecessary durable storage and concurrency complexity.
5. Use a floating lower-right entry point plus a MudBlazor drawer/sidebar so the map and status surfaces remain visible while the advisor is open.

## Phase 1: Design

### Data model changes

- Add a session-scoped advisor conversation model with ordered message items, request state, and open/closed sidebar state.
- Add an advisory context snapshot model that captures the current game, current controlled seat, phase, resources, owned railroads, destination state, and relevant board pressure facts.
- Add a freeform OpenAI response model separate from the existing bot `selectedOptionId` result shape.

### Server orchestration changes

- Introduce an advisory service responsible for refreshing authoritative context, assembling the advisory prompt payload, and returning freeform assistant text.
- Reuse existing OpenAI configuration and HTTP client wiring; do not expose model keys or raw provider calls to the browser.
- Refresh authoritative state on each send request so the chat answers current board questions from live game state rather than stale component state.

### UI/state-mapping changes

- Add a lower-right advisor icon on `GameBoard.razor` that toggles an in-page chat drawer/sidebar.
- Add dedicated chat components under `Components/GameBoard` for conversation transcript, composer, loading/failure state, and advisory disclaimer.
- Keep the board usable while the advisor is open and ensure responsive behavior on desktop and mobile.

### Testing design

- Add unit/service tests for advisory context assembly and freeform response parsing/failure handling.
- Add UI-facing regression coverage for greeting, message append behavior, and latest-state refresh semantics where practical in current test infrastructure.
- Validate that advice requests do not affect authoritative game-state mutation paths and that fallback/error states surface cleanly.

## Phase 2: Implementation Strategy

1. Extend the OpenAI integration to support advisory text completion results alongside existing bot option selection.
2. Add advisory request/context/message models and a dedicated `GameBoardAdviceService` that assembles current authoritative context on demand.
3. Add a new GameBoard advisor drawer component set and wire it into the lower-right board layout.
4. Seed the conversation with the fixed greeting on first open, retain conversation history for the current page session, and refresh context on each submitted question.
5. Add failure handling, advisory disclaimers, and loading states that preserve gameplay continuity.
6. Add focused regression tests and final build validation.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| None currently anticipated | N/A | The MVP fits inside the existing Blazor app, service layer, and OpenAI configuration without new projects or durable chat infrastructure |
