# Tasks: Game State and Turn Management Cleanup

**Input**: Design documents from `/specs/001-game-state-turn-management/`
**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/`, `quickstart.md`

**Tests**: No standalone test-writing tasks are included because the specification did not explicitly request TDD or new automated tests. Validation is covered through the quickstart scenarios and targeted execution of existing affected test projects during implementation.

**Organization**: Tasks are grouped by user story to enable independent implementation and validation.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the shared UI/state scaffolding used across the feature.

- [X] T001 [P] Create shared board-state models in `src/Boxcars/Data/BoardTurnViewState.cs`, `src/Boxcars/Data/TurnMovementPreview.cs`, `src/Boxcars/Data/ArrivalResolutionModel.cs`, and `src/Boxcars/Data/PlayerControlBinding.cs`
- [X] T002 [P] Create decomposed MudBlazor board components in `src/Boxcars/Components/GameBoard/TurnStatusPanel.razor`, `src/Boxcars/Components/GameBoard/TurnActionPanel.razor`, `src/Boxcars/Components/GameBoard/ArrivalResolutionPanel.razor`, and `src/Boxcars/Components/GameBoard/EventTimelinePanel.razor`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Establish shared mapping and persistence contracts that all user stories depend on.

**⚠️ CRITICAL**: No user story work should begin until this phase is complete.

- [X] T003 Create board-state mapping and registration in `src/Boxcars/Services/GameBoardStateMapper.cs` and `src/Boxcars/Program.cs`
- [X] T004 [P] Extend persisted action/snapshot contracts for preview route state and player-control metadata in `src/Boxcars/GameEngine/PlayerAction.cs`, `src/Boxcars/Data/GameEventEntity.cs`, and `src/Boxcars.Engine/Persistence/GameState.cs`

**Checkpoint**: Shared board-state models and persistence contracts are ready for story implementation.

---

## Phase 3: User Story 1 - Restore the live board from saved play (Priority: P1) 🎯 MVP

**Goal**: Reload a game from the latest persisted event and restore active-player state, selected route preview, and traveled-path markers on the board.

**Independent Test**: Save a game mid-turn, reload the board, and verify the active player, current phase, moves left, selected route, and traveled X markers all match the latest event snapshot.

### Implementation for User Story 1

- [X] T005 [US1] Round-trip selected route preview and current-trip traveled segments through snapshot restore in `src/Boxcars.Engine/Domain/GameEngine.cs` and `src/Boxcars.Engine/Persistence/GameState.cs`
- [X] T006 [P] [US1] Project restored route preview and traveled markers into board/map models in `src/Boxcars/Data/Maps/PlayerMapState.cs` and `src/Boxcars/Services/GameBoardStateMapper.cs`
- [X] T007 [P] [US1] Reapply restored selected route nodes and segment keys on board load in `src/Boxcars/Components/Map/MapComponent.razor` and `src/Boxcars/Components/Map/GameMapComponent.razor`
- [X] T008 [US1] Refactor board load and reconnect flow to hydrate decomposed components from the latest snapshot in `src/Boxcars/Components/Pages/GameBoard.razor` and `src/Boxcars/Components/GameBoard/EventTimelinePanel.razor`

**Checkpoint**: Reloaded games reproduce the latest persisted board state without manual reselection.

---

## Phase 4: User Story 2 - Plan movement with live turn feedback (Priority: P1)

**Goal**: Let the active player preview legal movement with live moves-left and fee feedback while blocking over-selection and premature turn completion.

**Independent Test**: Start a move phase with known movement allowance, select route segments one by one, and verify moves left and fee estimate update immediately while illegal extra selections and early `END TURN` attempts are rejected.

### Implementation for User Story 2

- [X] T009 [P] [US2] Implement live movement-preview calculation for selected nodes, moves left, and fee estimate in `src/Boxcars/Components/Map/MapComponent.razor` and `src/Boxcars/Data/TurnMovementPreview.cs`
- [X] T010 [P] [US2] Render turn-planning feedback and disabled action states in `src/Boxcars/Components/GameBoard/TurnStatusPanel.razor` and `src/Boxcars/Components/GameBoard/TurnActionPanel.razor`
- [X] T011 [US2] Replace placeholder turn automation with route-preview and move-commit flow in `src/Boxcars/Components/Pages/GameBoard.razor` and `src/Boxcars/Components/Map/GameMapComponent.razor`
- [X] T012 [US2] Enforce movement exhaustion, invalid extra-segment preservation, and end-turn preconditions in `src/Boxcars/GameEngine/GameEngineService.cs`, `src/Boxcars/GameEngine/PlayerAction.cs`, and `src/Boxcars.Engine/Domain/GameEngine.cs`

**Checkpoint**: The active player can plan moves with immediate feedback and cannot exceed movement or end the turn early.

---

## Phase 5: User Story 3 - Complete arrival and advance the turn (Priority: P2)

**Goal**: Surface arrival resolution clearly, apply payout correctly, and advance to the next player only after a valid turn completion.

**Independent Test**: Move a player onto their destination, confirm arrival messaging and payout, complete the turn, and verify the next player becomes active across the board and timeline.

### Implementation for User Story 3

- [ ] T013 [P] [US3] Surface arrival-resolution data from engine snapshots and domain events in `src/Boxcars.Engine/Domain/GameEngine.cs`, `src/Boxcars.Engine/Events/DomainEvents.cs`, and `src/Boxcars/Data/ArrivalResolutionModel.cs`
- [ ] T014 [P] [US3] Render arrival notification and purchase-opportunity prompt in `src/Boxcars/Components/GameBoard/ArrivalResolutionPanel.razor` and integrate it in `src/Boxcars/Components/Pages/GameBoard.razor`
- [ ] T015 [US3] Update move commit, payout refresh, and next-player activation broadcasting in `src/Boxcars/GameEngine/GameEngineService.cs`, `src/Boxcars/Services/GameService.cs`, and `src/Boxcars/Components/Pages/GameBoard.razor`
- [ ] T016 [US3] Refresh player summary state for arrival and active-turn changes in `src/Boxcars/Components/Map/PlayerBoard.razor` and `src/Boxcars/Data/PlayerBoardModel.cs`

**Checkpoint**: Arrival, payout, and next-player progression are visible and correct after a completed turn.

---

## Phase 6: User Story 4 - Restrict actions to the owning player (Priority: P2)

**Goal**: Allow every participant to observe the live board while ensuring only the controlling participant for the active player can mutate that turn.

**Independent Test**: Connect two users to the same game, verify the inactive participant can observe mid-turn state but cannot select segments or end the turn, then verify control transfers correctly when the turn advances.

### Implementation for User Story 4

- [ ] T017 [P] [US4] Resolve authenticated user-to-player bindings from game roster data in `src/Boxcars/Services/GameBoardStateMapper.cs` and `src/Boxcars/Data/PlayerControlBinding.cs`
- [ ] T018 [US4] Validate mutating actions by controlling participant identity instead of display name alone in `src/Boxcars/GameEngine/PlayerAction.cs`, `src/Boxcars/GameEngine/IGameEngine.cs`, and `src/Boxcars/GameEngine/GameEngineService.cs`
- [ ] T019 [P] [US4] Gate board and map interactivity for observers while preserving live state visibility in `src/Boxcars/Components/Pages/GameBoard.razor`, `src/Boxcars/Components/GameBoard/TurnActionPanel.razor`, and `src/Boxcars/Components/Map/MapComponent.razor`
- [ ] T020 [US4] Preserve reconnect and SignalR observer behavior while rejecting unauthorized live mutations in `src/Boxcars/Hubs/GameHub.cs` and `src/Boxcars/Components/Pages/GameBoard.razor`

**Checkpoint**: Multiplayer participants can observe all live board updates, but only the correct player can act.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Remove leftover placeholder behavior and validate the feature end to end.

- [ ] T021 Clean up obsolete placeholder turn-flow code and align event descriptions in `src/Boxcars/Components/Pages/GameBoard.razor` and `src/Boxcars/GameEngine/GameEngineService.cs`
- [ ] T022 [P] Validate the reload, move-preview, arrival, and multi-user control flows against `specs/001-game-state-turn-management/quickstart.md` and update `specs/001-game-state-turn-management/quickstart.md` if any manual verification steps need correction

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1: Setup** has no dependencies and can start immediately.
- **Phase 2: Foundational** depends on Phase 1 and blocks all user-story work.
- **Phase 3: US1** depends on Phase 2.
- **Phase 4: US2** depends on Phase 2 and integrates with the shared board-state mapping from US1 when both are merged.
- **Phase 5: US3** depends on Phase 2 and should follow US2 because arrival and end-turn flow rely on the committed move workflow.
- **Phase 6: US4** depends on Phase 2 and can proceed alongside later story work once the action contract is stable.
- **Phase 7: Polish** depends on completion of the desired user stories.

### User Story Dependencies

- **US1 (P1)**: No dependency on other user stories after foundational work; this is the MVP slice.
- **US2 (P1)**: No hard dependency on US1, but it shares the same board-state surfaces and should merge cleanly with US1 before full validation.
- **US3 (P2)**: Depends on the committed move/end-turn flow from US2.
- **US4 (P2)**: Depends on the foundational player-control contract and should be validated with the board workflow from US1 and US2.

### Suggested Story Completion Order

1. US1
2. US2
3. US3
4. US4

## Parallel Opportunities

- `T001` and `T002` can run in parallel because they create separate data and UI files.
- `T006` and `T007` can run in parallel once snapshot round-tripping is defined.
- `T009` and `T010` can run in parallel because preview calculation and turn-status rendering touch separate files.
- `T013` and `T014` can run in parallel because arrival data plumbing and arrival UI are separate surfaces.
- `T017` and `T019` can run in parallel once the player-binding model is settled.
- `T022` can run in parallel with late cleanup if implementation is functionally complete.

## Parallel Example: User Story 1

```bash
# Once T005 defines the restored route/travel snapshot contract:
Task: T006 Project restored route preview and traveled markers into src/Boxcars/Data/Maps/PlayerMapState.cs and src/Boxcars/Services/GameBoardStateMapper.cs
Task: T007 Reapply restored selected route nodes and segment keys on board load in src/Boxcars/Components/Map/MapComponent.razor and src/Boxcars/Components/Map/GameMapComponent.razor
```

## Parallel Example: User Story 2

```bash
# After foundational board-state models are available:
Task: T009 Implement live movement-preview calculation in src/Boxcars/Components/Map/MapComponent.razor and src/Boxcars/Data/TurnMovementPreview.cs
Task: T010 Render turn-planning feedback in src/Boxcars/Components/GameBoard/TurnStatusPanel.razor and src/Boxcars/Components/GameBoard/TurnActionPanel.razor
```

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational.
3. Complete Phase 3: User Story 1.
4. Validate reload fidelity from the latest persisted event snapshot before moving on.

### Incremental Delivery

1. Deliver US1 to make reloads trustworthy.
2. Add US2 to make turn planning legal and understandable.
3. Add US3 to complete arrival and next-turn progression.
4. Add US4 to harden multiplayer ownership and observer behavior.

### Parallel Team Strategy

1. One developer completes Phase 1 and Phase 2.
2. After foundational work, one developer can take US1/US2 board-state work while another prepares US4 authorization changes.
3. US3 should merge after the move-commit flow from US2 is stable.

## Notes

- `[P]` tasks touch separate files or isolated surfaces and are safe to parallelize.
- Each user story is scoped so it can be validated independently using the scenarios in `spec.md` and `quickstart.md`.
- Avoid reintroducing placeholder queued-action automation in `GameBoard.razor`; all turn mutations should flow through the finalized action contract.