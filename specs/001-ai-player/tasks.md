# Tasks: AI-Controlled Player Turns

**Input**: Design documents from `/specs/001-ai-player/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: Add targeted regression tests for controller resolution, AI authorization, bot assignment lifecycle, and history/reload behavior because the specification requires independent story validation.

**Organization**: Tasks are grouped by user story so each story can be implemented and validated independently.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish shared controller-mode vocabulary and reusable test scaffolding for the feature.

- [X] T001 Define controller-mode records, constants, and JSON serialization for AI seat state in `src/Boxcars/Data/BotAutomationModels.cs`
- [X] T002 [P] Add reusable dedicated-bot and ghost-seat fixture helpers in `tests/Boxcars.Engine.Tests/Fixtures/BotTurnServiceTestHarness.cs`
- [X] T003 [P] Add server AI actor configuration and defaults in `src/Boxcars/Data/BotOptions.cs` and `src/Boxcars/Program.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Refactor shared control-resolution, persistence, and authorization rules that all user stories depend on.

**CRITICAL**: No user story work can begin until this phase is complete.

- [X] T004 Refactor durable bot assignment fields for `ControllerMode`, optional `ControllerUserId`, and clear-reason semantics in `src/Boxcars/Data/BotAutomationModels.cs` and `src/Boxcars/Data/GameEntity.cs`
- [X] T005 Implement seat-controller resolution for `HumanDirect`, `HumanDelegated`, `AiBotSeat`, and `AiGhost` in `src/Boxcars/Data/PlayerControlRules.cs` and `src/Boxcars/Services/GamePresenceService.cs`
- [X] T006 Update game persistence and conflict handling for AI seat assignments in `src/Boxcars/Services/GameService.cs`
- [X] T007 Update authoritative action authorization for server-authored AI turns in `src/Boxcars/GameEngine/GameEngineService.cs`
- [X] T008 [P] Add controller-resolution and reconnect/release regression coverage in `tests/Boxcars.Engine.Tests/Unit/GamePresenceServiceTests.cs`
- [X] T009 [P] Add authorization regression coverage for AI-controlled seats in `tests/Boxcars.Engine.Tests/Unit/ForcedSaleActionTests.cs`
- [X] T010 [P] Plumb controller-mode state into board models in `src/Boxcars/Data/BoardTurnViewState.cs`, `src/Boxcars/Data/PlayerBoardModel.cs`, and `src/Boxcars/Services/GameBoardStateMapper.cs`

**Checkpoint**: Foundation ready. Dedicated bot seats and ghost-controlled seats can now be implemented without relying on delegated-human control hacks.

---

## Phase 3: User Story 1 - Let a bot take its turn (Priority: P1) MVP

**Goal**: Allow dedicated bot seats and ghosted disconnected human seats to resolve eligible turns automatically through the server loop.

**Independent Test**: Start one game with a dedicated bot seat and one game with a disconnected ghosted human seat, then verify the server resolves region choice, purchase, auction, movement, and sell behavior without manual input for that seat.

### Tests for User Story 1

- [X] T011 [P] [US1] Add dedicated bot seat auto-turn coverage in `tests/Boxcars.Engine.Tests/Unit/BotTurnResolutionTests.cs`
- [X] T012 [P] [US1] Add ghost-mode stop-condition coverage for reconnect and release flows in `tests/Boxcars.Engine.Tests/Unit/GamePresenceServiceTests.cs`
- [X] T013 [P] [US1] Add deterministic fallback coverage for AI-backed phases in `tests/Boxcars.Engine.Tests/Unit/BotTurnResolutionTests.cs`

### Implementation for User Story 1

- [X] T014 [US1] Remove delegated-control bootstrap for dedicated bot seats in `src/Boxcars/Services/BotTurnService.cs`
- [X] T015 [US1] Resolve active bot assignments by controller mode and missing-definition state in `src/Boxcars/Services/BotTurnService.cs`
- [X] T016 [US1] Stamp AI-authored actions with the server actor identity while preserving represented-seat attribution in `src/Boxcars/Services/BotTurnService.cs` and `src/Boxcars/GameEngine/PlayerAction.cs`
- [X] T017 [US1] Update automatic turn branching so only AI-controlled seats execute in the automatic loop in `src/Boxcars/GameEngine/GameEngineService.cs`
- [X] T018 [US1] Stop ghost-mode AI immediately on reconnect or release in `src/Boxcars/Services/GamePresenceService.cs` and `src/Boxcars/Services/BotTurnService.cs`
- [X] T019 [US1] Update bot assignment save flows for dedicated bot seats versus ghosted human seats in `src/Boxcars/Components/GameBoard/BotAssignmentDialog.razor`, `src/Boxcars/Components/Pages/GameBoard.razor`, and `src/Boxcars/Services/GameService.cs`
- [X] T020 [US1] Update player-card and board-status UX so dedicated bot seats never require `TAKE CONTROL` in `src/Boxcars/Components/Map/PlayerBoard.razor`, `src/Boxcars/Components/Pages/GameBoard.razor`, and `src/Boxcars/Services/GameBoardStateMapper.cs`

**Checkpoint**: User Story 1 should now support automatic dedicated-bot turns and ghost-mode turns with correct start/stop semantics.

---

## Phase 4: User Story 2 - Define bot identity and strategy (Priority: P2)

**Goal**: Keep the shared dashboard-managed bot library authoritative for both dedicated bot seats and ghost assignments.

**Independent Test**: Create, edit, and delete global bot definitions from the dashboard, assign them to dedicated bot seats and ghosted human seats, and verify each seat uses the current referenced definition without copying a snapshot.

### Tests for User Story 2

- [X] T021 [P] [US2] Add bot definition CRUD and optimistic-concurrency coverage in `tests/Boxcars.Engine.Tests/Unit/BotDefinitionServiceTests.cs`
- [X] T022 [P] [US2] Add assignment-isolation coverage for multiple seats using different bot definitions in `tests/Boxcars.Engine.Tests/Unit/BotTurnResolutionTests.cs`

### Implementation for User Story 2

- [X] T023 [US2] Align bot definition persistence and audit data with the global library contract in `src/Boxcars/Data/BotStrategyDefinitionEntity.cs` and `src/Boxcars/Services/BotDefinitionService.cs`
- [X] T024 [US2] Update dashboard bot-library CRUD UX and conflict messaging in `src/Boxcars/Components/Pages/DashboardBotLibraryPanel.razor` and `src/Boxcars/Components/Pages/Bots.razor`
- [X] T025 [US2] Surface empty-library, deleted-definition, and reassignment states in `src/Boxcars/Components/GameBoard/BotAssignmentDialog.razor` and `src/Boxcars/Components/Pages/GameBoard.razor`
- [X] T026 [US2] Keep active gameplay assignments linked to edited or deleted global bot definitions in `src/Boxcars/Services/BotTurnService.cs` and `src/Boxcars/Services/GameBoardStateMapper.cs`

**Checkpoint**: User Story 2 should now provide a global bot library whose current definitions drive live gameplay assignments.

---

## Phase 5: User Story 3 - Preserve bot actions as normal game history (Priority: P3)

**Goal**: Ensure AI-driven actions remain trustworthy in history, reload, and multiplayer synchronization.

**Independent Test**: Let a dedicated bot seat and a ghosted human seat complete several automated phases, reload the game, and verify connected clients and restored state show the same recorded outcomes.

### Tests for User Story 3

- [X] T027 [P] [US3] Add AI history and replay coverage in `tests/Boxcars.Engine.Tests/Unit/BotActionHistoryTests.cs`
- [X] T028 [P] [US3] Add reload and broadcast regression coverage for AI-authored actions in `tests/Boxcars.Engine.Tests/Unit/GameEngineServiceAiHistoryTests.cs`

### Implementation for User Story 3

- [X] T029 [US3] Persist server-actor AI metadata through action serialization and history mapping in `src/Boxcars/GameEngine/GameEventSerialization.cs` and `src/Boxcars/Services/GameBoardStateMapper.cs`
- [X] T030 [US3] Record AI-authored actions as represented-seat actions with bot metadata in `src/Boxcars/GameEngine/GameEngineService.cs` and `src/Boxcars/Services/BotTurnService.cs`
- [X] T031 [US3] Restore AI assignment and history fidelity during game reload in `src/Boxcars/Data/GameEntity.cs` and `src/Boxcars/Services/GameService.cs`
- [X] T032 [US3] Surface AI action attribution consistently in gameplay history UI in `src/Boxcars/Components/Pages/GameBoard.razor` and `src/Boxcars/Services/GameBoardStateMapper.cs`

**Checkpoint**: All user stories should now preserve AI turn authorship, synchronization, and reload fidelity.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final validation and cleanup across the feature.

- [X] T033 [P] Update gameplay and dashboard contracts to match final implementation in `specs/001-ai-player/contracts/gameplay-bot-assignment-contract.md` and `specs/001-ai-player/contracts/dashboard-bot-library-contract.md`
- [X] T034 [P] Remove stale delegated-bot-only wording and comments in `src/Boxcars/Components/Pages/GameBoard.razor`, `src/Boxcars/Components/Map/PlayerBoard.razor`, and `src/Boxcars/Services/BotTurnService.cs`
- [ ] T035 Run the end-to-end validation scenarios documented in `specs/001-ai-player/quickstart.md`
- [X] T036 Run focused regression suites in `tests/Boxcars.Engine.Tests/Unit/BotTurnResolutionTests.cs`, `tests/Boxcars.Engine.Tests/Unit/BotActionHistoryTests.cs`, `tests/Boxcars.Engine.Tests/Unit/ForcedSaleActionTests.cs`, and `tests/Boxcars.Engine.Tests/Unit/GamePresenceServiceTests.cs`

---

## Dependencies & Execution Order

### Phase Dependencies

- Phase 1: No dependencies; start immediately.
- Phase 2: Depends on Phase 1; blocks all user stories.
- Phase 3: Depends on Phase 2; delivers the MVP.
- Phase 4: Depends on Phase 2 and can proceed after the MVP is stable.
- Phase 5: Depends on Phases 2 through 4 because history fidelity relies on the final controller and assignment model.
- Phase 6: Depends on all desired user stories being complete.

### User Story Dependencies

- US1 depends only on the foundational controller-mode and authorization work.
- US2 depends on the foundational controller-mode and persistence work, but not on US1 UI completion.
- US3 depends on the final AI action-authoring path from US1 and the live bot-definition linkage from US2.

### Within Each User Story

- Write the story tests first and confirm they fail before implementation.
- Finish controller or persistence model changes before UI-state mapping changes that consume them.
- Complete service and engine changes before broad UI cleanup.

### Parallel Opportunities

- `T002` and `T003` can run in parallel during setup.
- `T008`, `T009`, and `T010` can run in parallel once the foundational model is defined.
- `T011`, `T012`, and `T013` can run in parallel for US1 test coverage.
- `T021` and `T022` can run in parallel for US2 test coverage.
- `T027` and `T028` can run in parallel for US3 test coverage.
- `T033` and `T034` can run in parallel during polish.

---

## Parallel Example: User Story 1

```text
T011 Add dedicated bot seat auto-turn coverage in tests/Boxcars.Engine.Tests/Unit/BotTurnResolutionTests.cs
T012 Add ghost-mode stop-condition coverage in tests/Boxcars.Engine.Tests/Unit/GamePresenceServiceTests.cs
T013 Add deterministic fallback coverage in tests/Boxcars.Engine.Tests/Unit/BotTurnResolutionTests.cs
```

## Parallel Example: User Story 2

```text
T021 Add bot definition CRUD and optimistic-concurrency coverage in tests/Boxcars.Engine.Tests/Unit/BotDefinitionServiceTests.cs
T022 Add assignment-isolation coverage in tests/Boxcars.Engine.Tests/Unit/BotTurnResolutionTests.cs
```

## Parallel Example: User Story 3

```text
T027 Add AI history and replay coverage in tests/Boxcars.Engine.Tests/Unit/BotActionHistoryTests.cs
T028 Add reload and broadcast regression coverage in tests/Boxcars.Engine.Tests/Unit/GameEngineServiceAiHistoryTests.cs
```

---

## Implementation Strategy

### MVP First

1. Complete Phase 1.
2. Complete Phase 2.
3. Complete Phase 3.
4. Validate the dedicated-bot and ghost-mode scenarios from `specs/001-ai-player/quickstart.md`.

### Incremental Delivery

1. Deliver the controller-mode foundation and dedicated bot / ghost execution path in US1.
2. Add the global bot-library and assignment lifecycle refinements in US2.
3. Finish history, reload, and broadcast fidelity in US3.
4. Run the polish validation tasks before merge.

### Suggested MVP Scope

Implement through Phase 3 only for the first shippable increment.