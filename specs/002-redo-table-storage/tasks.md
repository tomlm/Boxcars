# Tasks: Redo Table Storage

**Input**: Design documents from `/specs/002-redo-table-storage/`  
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/, quickstart.md

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare routing, shared models, and feature scaffolding for implementation.

- [X] T001 Add Create Game page route entry and navigation wiring in src/Boxcars/Program.cs
- [X] T002 Add shared create-game request/slot DTOs in src/Boxcars/Data/GameCreationModels.cs
- [X] T003 [P] Add game event serialization helper for snapshot payloads in src/Boxcars/GameEngine/GameEventSerialization.cs

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core storage model consolidation required before any user story work.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T004 Refactor table constants to only active tables (`UsersTable`, `GamesTable`) in src/Boxcars/Identity/TableNames.cs
- [X] T005 Replace legacy game table entities with `GameEntity` contract fields in src/Boxcars/Data/GameEntity.cs
- [X] T006 Introduce `GameEventEntity` contract model and migrate snapshot usage in src/Boxcars/Data/GameEventEntity.cs
- [X] T007 Create `UserEntity` storage model (`PartitionKey=USER`, `RowKey={Email}`) in src/Boxcars/Data/ApplicationUser.cs
- [X] T008 Update dependency registrations for revised services and engine contracts in src/Boxcars/Program.cs
- [X] T009 Create legacy storage de-scope inventory and target removal list in specs/002-redo-table-storage/tasks.md

**Checkpoint**: Foundation ready - user story work can proceed.

---

## Phase 3: User Story 1 - Create and Start a Game with Explicit Players (Priority: P1) 🎯 MVP

**Goal**: Allow creator to select user + color for each slot, create immutable game settings, and navigate Dashboard -> Create Game -> Game page.

**Independent Test**: From Dashboard open Create Game, assign slots/colors, create game, and verify game page opens with Start Game action.

### Implementation for User Story 1

- [X] T010 [US1] Implement create-game page UI with per-slot user/color selectors in src/Boxcars/Components/Pages/CreateGame.razor
- [X] T011 [P] [US1] Add create-game page view-state and validation logic in src/Boxcars/Components/Pages/CreateGame.razor.cs
- [X] T012 [US1] Change dashboard create action to navigate to Create Game page in src/Boxcars/Components/Pages/Dashboard.razor
- [X] T013 [US1] Update game creation service API to accept selected players/colors and persist immutable settings in src/Boxcars/Services/GameService.cs
- [X] T014 [US1] Update game-engine create entrypoint to consume ordered player/color assignments in src/Boxcars/GameEngine/IGameEngine.cs
- [X] T015 [US1] Implement game-engine create flow persistence for `GameEntity` in src/Boxcars/GameEngine/GameEngineService.cs
- [X] T016 [US1] Ensure game page exposes Start Game action after create navigation in src/Boxcars/Components/Pages/GameBoard.razor

**Checkpoint**: User Story 1 is fully functional and testable.

---

## Phase 4: User Story 2 - Persist and Replay Game Timeline (Priority: P2)

**Goal**: Persist every game action with event payload + snapshot and restore latest state/history on reconnect.

**Independent Test**: Perform several actions, reconnect, and verify latest state and ordered history are restored from `GamesTable`.

### Implementation for User Story 2

- [X] T017 [US2] Replace per-action snapshot persistence with `GameEventEntity` writes in src/Boxcars/GameEngine/GameEngineService.cs
- [X] T018 [US2] Enforce persist-before-broadcast ordering for UI-triggered actions in src/Boxcars/GameEngine/GameStateBroadcastService.cs
- [X] T019 [US2] Implement reconnect restore from latest `GameEventEntity` snapshot in src/Boxcars/GameEngine/GameEngineService.cs
- [X] T020 [US2] Add ordered event-history query projection in src/Boxcars/Services/GameService.cs
- [X] T021 [US2] Render action history from event timeline records in src/Boxcars/Components/Pages/GameBoard.razor
- [X] T022 [US2] Remove remaining legacy snapshot-table reads/writes in src/Boxcars/GameEngine/GameEngineService.cs
- [X] T023 [US2] Add reconnection resilience validation runbook and pass criteria for SC-003 in specs/002-redo-table-storage/quickstart.md

**Checkpoint**: User Stories 1 and 2 both work independently.

---

## Phase 5: User Story 3 - Authenticate and Reuse User Profiles (Priority: P3)

**Goal**: Ensure authentication creates/looks up `UserEntity` in `UsersTable` and bootstrap Beatles mock users.

**Independent Test**: Authenticate and verify user profile create/lookup by email plus Beatles seed availability in Create Game selection.

### Implementation for User Story 3

- [X] T024 [US3] Replace multi-index user-store logic with `UsersTable` single-entity operations in src/Boxcars/Identity/TableStorageUserStore.cs
- [X] T025 [US3] Update player profile queries to use `UserEntity` records from `UsersTable` only in src/Boxcars/Services/PlayerProfileService.cs
- [X] T026 [US3] Add Beatles seed initialization for `UsersTable` in src/Boxcars/Program.cs
- [X] T027 [US3] Update profile settings save/load path to match `UserEntity` contract in src/Boxcars/Components/Pages/ProfileSettings.razor
- [X] T028 [US3] Add first-attempt usability measurement procedure and sample protocol for SC-004 in specs/002-redo-table-storage/quickstart.md

**Checkpoint**: All user stories are functional and independently testable.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Remove obsolete artifacts, align docs, and verify end-to-end behavior.

- [X] T029 [P] Remove unused legacy storage entities and table references in src/Boxcars/Data/GamePlayerEntity.cs
- [X] T030 [P] Remove unused legacy storage entities and table references in src/Boxcars/Data/IndexEntity.cs
- [X] T031 Verify and enforce `CancellationToken` propagation across modified I/O methods in src/Boxcars/Services/GameService.cs
- [X] T032 Verify and enforce `CancellationToken` propagation across modified I/O methods in src/Boxcars/Identity/TableStorageUserStore.cs
- [X] T033 Run full build and tests for regression validation in Boxcars.slnx

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: Starts immediately.
- **Phase 2 (Foundational)**: Depends on Phase 1 completion and blocks all story work.
- **Phase 3-5 (User Stories)**: Depend on Phase 2 completion.
- **Phase 6 (Polish)**: Depends on completion of selected user stories.

### User Story Dependencies

- **US1 (P1)**: Starts after Foundational; no dependency on US2/US3.
- **US2 (P2)**: Starts after Foundational; integrates with game flow but remains independently testable.
- **US3 (P3)**: Starts after Foundational; independently testable via auth/profile paths.

### Within Each User Story

- Data contracts before service updates.
- Service updates before UI wiring.
- Core story implementation before polish and regression checks.

---

## Parallel Execution Examples

### User Story 1

- Run in parallel:
  - T011 in src/Boxcars/Components/Pages/CreateGame.razor.cs
  - T012 in src/Boxcars/Components/Pages/Dashboard.razor

### User Story 2

- Run in parallel:
  - T020 in src/Boxcars/Services/GameService.cs
  - T021 in src/Boxcars/Components/Pages/GameBoard.razor

### User Story 3

- Run in parallel:
  - T025 in src/Boxcars/Services/PlayerProfileService.cs
  - T027 in src/Boxcars/Components/Pages/ProfileSettings.razor

---

## Implementation Strategy

### MVP First (US1)

1. Complete Phase 1 and Phase 2.
2. Complete Phase 3 (US1).
3. Validate Dashboard -> Create Game -> Game page with Start Game.

### Incremental Delivery

1. Deliver US1 as MVP.
2. Add US2 for reconnect/timeline resilience.
3. Add US3 for auth-backed profile persistence + seed data.
4. Finish with Phase 6 cleanup and validation.

### Parallel Team Strategy

1. Team completes Phase 1 and Phase 2 together.
2. Then parallelize by story owner:
   - Developer A: US1
   - Developer B: US2
   - Developer C: US3
