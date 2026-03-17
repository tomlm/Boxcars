# Tasks: AI-Controlled Player Turns

**Input**: Design documents from `/specs/001-ai-player/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Include focused regression tests for non-trivial turn flow, fallback resolution, and deterministic sell behavior per the Boxcars constitution.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Add the base configuration and storage scaffolding required by the feature.

- [X] T001 Add `BotsTable` naming support in `src/Boxcars/Identity/TableNames.cs`
- [X] T002 Create OpenAI and bot configuration models in `src/Boxcars/Data/BotOptions.cs`
- [X] T003 Register bot configuration and create `BotsTable` on startup in `src/Boxcars/Program.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core bot persistence and orchestration infrastructure that must exist before any user story is completed.

**⚠️ CRITICAL**: No user story can be considered complete until this phase is done.

- [X] T004 Create durable bot entities and assignment models in `src/Boxcars/Data/BotStrategyDefinitionEntity.cs` and `src/Boxcars/Data/BotAutomationModels.cs`
- [X] T005 Persist bot-assignment metadata on game records in `src/Boxcars/Data/GameEntity.cs`
- [X] T006 [P] Implement global bot library CRUD with optimistic concurrency in `src/Boxcars/Services/BotDefinitionService.cs`
- [X] T007 [P] Implement phase-specific prompt building and legal-option shaping in `src/Boxcars/Services/BotDecisionPromptBuilder.cs`
- [X] T008 [P] Implement the server-side OpenAI bot client in `src/Boxcars/Services/OpenAiBotClient.cs`
- [X] T009 Implement bot assignment lookup, fallback selection, and decision orchestration in `src/Boxcars/Services/BotTurnService.cs`
- [X] T010 Wire bot services into the app composition root in `src/Boxcars/Program.cs`

**Checkpoint**: Bot storage, OpenAI integration, and authoritative orchestration are ready for story implementation.

---

## Phase 3: User Story 1 - Let a bot take its turn (Priority: P1) 🎯 MVP

**Goal**: Let a delegated controller assign a bot to a disconnected seat and have the server resolve eligible phases without stalling play.

**Independent Test**: Disconnect a player, take control from another player, assign a bot from the player card, then verify region choice, purchase, auction, movement reuse, sell fallback, and stop conditions all work without manual input for that seat.

### Tests for User Story 1

- [X] T011 [P] [US1] Add bot turn resolution regression tests in `tests/Boxcars.Engine.Tests/Unit/BotTurnResolutionTests.cs`
- [X] T012 [P] [US1] Add deterministic sell evaluation tests in `tests/Boxcars.Engine.Tests/Unit/BotSellImpactEvaluatorTests.cs`

### Implementation for User Story 1

- [X] T013 [P] [US1] Extend player board state for bot assignment visibility and status in `src/Boxcars/Data/PlayerBoardModel.cs`
- [X] T014 [US1] Map bot assignment state into the board view model in `src/Boxcars/Services/GameBoardStateMapper.cs`
- [X] T015 [P] [US1] Create the in-game bot assignment dialog component in `src/Boxcars/Components/GameBoard/BotAssignmentDialog.razor`
- [X] T016 [US1] Add the player-card settings action for bot assignment in `src/Boxcars/Components/Map/PlayerBoard.razor`
- [X] T017 [US1] Handle bot assignment commands and dialog flow in `src/Boxcars/Components/Pages/GameBoard.razor`
- [X] T018 [US1] Execute bot-driven region, purchase, auction, movement reuse, and forced-sale fallback in `src/Boxcars/GameEngine/GameEngineService.cs`
- [X] T019 [US1] Extend action models for bot-attributed automated decisions in `src/Boxcars/GameEngine/PlayerAction.cs`
- [X] T020 [US1] Clear or invalidate bot assignments on release and reconnect in `src/Boxcars/Services/GamePresenceService.cs` and `src/Boxcars/GameEngine/GameEngineService.cs`

**Checkpoint**: A disconnected controlled seat can finish eligible turns through bot automation and stop cleanly on reconnect or release.

---

## Phase 4: User Story 2 - Define bot identity and strategy (Priority: P2)

**Goal**: Provide a dashboard BOT management experience for the shared global bot library and connect it to in-game assignment choices.

**Independent Test**: Create, edit, and delete bots from the dashboard as a signed-in user, then open an in-game assignment dialog and verify the current global definitions appear immediately, including empty-state and conflict behavior.

### Implementation for User Story 2

- [X] T021 [P] [US2] Create dashboard bot library view models in `src/Boxcars/Data/BotLibraryModels.cs`
- [X] T022 [P] [US2] Create the dashboard bot library management component in `src/Boxcars/Components/Pages/DashboardBotLibraryPanel.razor`
- [X] T023 [US2] Integrate the bot library panel into the dashboard page in `src/Boxcars/Components/Pages/Dashboard.razor`
- [X] T024 [US2] Add create, edit, delete, and refresh flows to the dashboard bot library UI in `src/Boxcars/Components/Pages/DashboardBotLibraryPanel.razor`
- [X] T025 [US2] Surface empty-library and missing-definition assignment states in `src/Boxcars/Components/GameBoard/BotAssignmentDialog.razor`
- [X] T026 [US2] Enforce optimistic concurrency conflict handling for bot edits in `src/Boxcars/Services/BotDefinitionService.cs` and `src/Boxcars/Components/Pages/DashboardBotLibraryPanel.razor`

**Checkpoint**: Signed-in users can manage the shared bot library on the dashboard, and in-game assignment choices reflect the current live definitions.

---

## Phase 5: User Story 3 - Preserve bot actions as normal game history (Priority: P3)

**Goal**: Record bot-driven actions through the same authoritative history and broadcast flow as human actions so reloads and multiplayer views remain trustworthy.

**Independent Test**: Let a bot complete several eligible decisions, reload the game, and verify the restored state, event timeline, and connected observers all show the same results as before reload.

### Tests for User Story 3

- [X] T027 [P] [US3] Add bot history and reload regression tests in `tests/Boxcars.GameEngine.Tests/BotActionHistoryTests.cs`

### Implementation for User Story 3

- [X] T028 [US3] Persist bot-attributed action details in event storage in `src/Boxcars/GameEngine/GameEngineService.cs`
- [X] T029 [US3] Update timeline projection for bot-driven actions in `src/Boxcars/Services/GameService.cs`
- [X] T030 [US3] Project bot activity and assignment status into gameplay state in `src/Boxcars/Services/GameBoardStateMapper.cs`
- [X] T031 [US3] Ensure bot-driven updates restore and rebroadcast correctly in `src/Boxcars/GameEngine/GameEngineService.cs` and `src/Boxcars/Components/Pages/GameBoard.razor`

**Checkpoint**: Bot actions are durable, visible to all players, and restored exactly after reload.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final integration, configuration, and validation work that spans multiple stories.

- [X] T032 [P] Add OpenAI configuration placeholders in `src/Boxcars/appsettings.json` and `src/Boxcars/appsettings.Development.json`
- [X] T033 Refine extracted UI styling for dashboard and player-card bot affordances in `src/Boxcars/Components/Pages/Dashboard.razor.css` and `src/Boxcars/Components/Map/PlayerBoard.razor.css`
- [ ] T034 Run the end-to-end validation scenarios in `specs/001-ai-player/quickstart.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1: Setup**: Starts immediately.
- **Phase 2: Foundational**: Depends on Phase 1 and blocks all user stories.
- **Phase 3: User Story 1**: Starts after Phase 2 and delivers the MVP.
- **Phase 4: User Story 2**: Starts after Phase 2 and builds on the shared bot library services.
- **Phase 5: User Story 3**: Starts after Phase 2 and depends on bot-attributed actions existing in the authoritative flow.
- **Phase 6: Polish**: Starts after the desired user stories are complete.

### User Story Dependencies

- **US1 (P1)**: Depends only on foundational bot storage and orchestration.
- **US2 (P2)**: Depends on foundational bot library services; independent of US1 completion.
- **US3 (P3)**: Depends on foundational services and on US1 bot action execution paths.

### Within Each User Story

- Story-specific tests run before or alongside implementation and must fail before the implementation is considered done.
- Data/view-model updates precede UI integration.
- UI command surfaces precede final orchestration wiring.
- Persistence and broadcast changes complete before story validation.

### Suggested Story Order

1. Finish Setup and Foundational phases.
2. Deliver US1 as the MVP.
3. Add US2 dashboard management.
4. Finish US3 history and reload fidelity.
5. Complete polish and quickstart validation.

---

## Parallel Opportunities

### User Story 1

```text
T011 and T012 can run in parallel.
T013 and T015 can run in parallel.
```

### User Story 2

```text
T021 and T022 can run in parallel.
T024 and T025 can run in parallel after the shared component shell exists.
```

### User Story 3

```text
T027 can run in parallel with T028.
T029 and T030 can run in parallel after bot action persistence is defined.
```

---

## Implementation Strategy

### MVP First

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational.
3. Complete Phase 3: User Story 1.
4. Validate the quickstart flow for disconnected-seat bot control before moving on.

### Incremental Delivery

1. Ship US1 to unblock live-game continuity for disconnected players.
2. Ship US2 to let users manage reusable global bots without gameplay-side authoring.
3. Ship US3 to harden event history, reload fidelity, and observer trust.

### Parallel Team Strategy

1. One developer completes Setup and Foundational work.
2. Once foundational services land, one developer can take US1 gameplay wiring while another builds the US2 dashboard UI.
3. US3 history and reload work can begin as soon as the US1 action path stabilizes.

---

## Notes

- Tasks marked `[P]` are safe to execute in parallel because they target different files or isolated concerns.
- User story labels map directly to the three prioritized scenarios in `spec.md`.
- The task list intentionally keeps OpenAI orchestration server-side and avoids introducing client-side decision logic.