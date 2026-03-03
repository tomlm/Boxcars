# Tasks: Map Interaction Modes

**Input**: Design documents from `/specs/003-map-modes/`
**Prerequisites**: plan.md (required), spec.md (required for user stories)

**Tests**: No explicit test-first or TDD requirement was specified in the feature spec; test tasks are not included.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Every task includes an exact file path

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare map interaction scaffolding for this feature.

- [X] T001 Add map interaction mode state scaffold in `src/Boxcars/Components/Pages/MapBoard.razor`
- [X] T002 Add mode-toggle control styling scaffold in `src/Boxcars/Components/Pages/MapBoard.razor.css`
- [X] T003 Add route service registration in `src/Boxcars/Program.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core routing infrastructure required before user story implementation.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T004 Create map route graph/path service in `src/Boxcars/Services/Maps/MapRouteService.cs`
- [X] T005 [P] Add route overlay render models in `src/Boxcars/Data/Maps/BoardElements.cs`
- [X] T006 Update projection outputs for route-selectable nodes/segments in `src/Boxcars/Services/Maps/BoardProjectionService.cs`
- [X] T007 Wire projected route metadata into map board state in `src/Boxcars/Components/Pages/MapBoard.razor`

**Checkpoint**: Foundation ready - user story implementation can now begin.

---

## Phase 3: User Story 1 - Build a route from Chicago (Priority: P1) 🎯 MVP

**Goal**: In Route mode, allow auto-path route selection from Chicago to any reachable clicked node and render selected segments as a solid black line.

**Independent Test**: Switch to Route mode on `/game/{id}` and click reachable nodes; map shows a solid black contiguous route from Chicago to latest endpoint.

### Implementation for User Story 1

- [X] T008 [US1] Resolve default Chicago start node on map load in `src/Boxcars/Components/Pages/MapBoard.razor`
- [X] T009 [US1] Implement route-mode node click handling with auto-path expansion in `src/Boxcars/Components/Pages/MapBoard.razor`
- [X] T010 [P] [US1] Render selected route segments as solid black overlay lines in `src/Boxcars/Components/Pages/MapBoard.razor`
- [X] T011 [P] [US1] Add route overlay visual styles (solid black emphasis) in `src/Boxcars/Components/Pages/MapBoard.razor.css`
- [X] T012 [P] [US1] Enforce unreachable-node no-op handling for route clicks in `src/Boxcars/Services/Maps/MapRouteService.cs`

**Checkpoint**: User Story 1 is fully functional and testable as MVP.

---

## Phase 4: User Story 2 - Inspect railroads (Priority: P2)

**Goal**: In Rail mode, selecting any segment highlights the full railroad and remains isolated from Route mode state.

**Independent Test**: Switch to Rail mode and click segments on different railroads; highlight moves to the selected railroad and route overlay state is unaffected.

### Implementation for User Story 2

- [X] T013 [US2] Gate railroad segment selection logic to Rail mode interactions in `src/Boxcars/Components/Pages/MapBoard.razor`
- [X] T014 [P] [US2] Clear/suppress route overlay rendering while in Rail mode in `src/Boxcars/Components/Pages/MapBoard.razor`
- [ ] T015 [P] [US2] Add selected-railroad visual mode treatment in `src/Boxcars/Components/Pages/MapBoard.razor.css`

**Checkpoint**: User Stories 1 and 2 work independently with mode-isolated behavior.

---

## Phase 5: User Story 3 - Undo route by clicking prior node (Priority: P3)

**Goal**: In Route mode, clicking a node already in the selected route truncates all segments past that node.

**Independent Test**: Build a multi-segment route, click a prior node in that route, and verify downstream segments are removed while earlier segments remain.

### Implementation for User Story 3

- [X] T016 [US3] Implement route truncation-on-prior-node click logic in `src/Boxcars/Components/Pages/MapBoard.razor`
- [X] T017 [P] [US3] Add route truncation helper method for prefix extraction in `src/Boxcars/Services/Maps/MapRouteService.cs`
- [X] T018 [P] [US3] Keep endpoint re-click as no-op in route selection handler in `src/Boxcars/Components/Pages/MapBoard.razor`

**Checkpoint**: All user stories are independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final consistency, quality, and validation across all stories.

- [ ] T019 [P] Refine route-node hitbox and cursor affordances in `src/Boxcars/Components/Pages/MapBoard.razor.css`
- [ ] T020 Update map interaction warnings/messages for Chicago-missing and invalid clicks in `src/Boxcars/Components/Pages/MapBoard.razor`
- [ ] T021 Run full feature build validation and capture notes in `specs/003-map-modes/plan.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - starts immediately.
- **Foundational (Phase 2)**: Depends on Setup completion - BLOCKS all user stories.
- **User Stories (Phase 3+)**: Depend on Foundational completion.
- **Polish (Phase 6)**: Depends on completion of desired user stories.

### User Story Dependencies

- **User Story 1 (P1)**: Starts after Foundational; no dependency on other stories.
- **User Story 2 (P2)**: Starts after Foundational; should remain independently testable from US1.
- **User Story 3 (P3)**: Starts after Foundational; depends on route selection flow from US1.

### Within Each User Story

- State/model wiring before interaction handlers.
- Interaction handlers before visual polish.
- Story-level validation after implementation tasks.

### Story Completion Order (Dependency Graph)

- US1 → US2
- US1 → US3

(US2 and US3 may proceed in parallel after US1 route foundation is stable.)

---

## Parallel Execution Examples

### User Story 1

- Run T010 and T012 in parallel after T009 begins stabilizing route selection contract.

### User Story 2

- Run T014 and T015 in parallel after T013 establishes Rail mode gating.

### User Story 3

- Run T017 and T018 in parallel after T016 defines truncation behavior.

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational.
3. Complete Phase 3: User Story 1.
4. Validate Route mode from Chicago end-to-end.

### Incremental Delivery

1. Setup + Foundational → base ready.
2. Deliver US1 (MVP route planning).
3. Deliver US2 (railroad inspection mode isolation).
4. Deliver US3 (route undo/truncation behavior).
5. Finish with polish and final validation.

### Parallel Team Strategy

1. One developer handles `MapRouteService.cs` while another prepares `MapBoard.razor` mode UI scaffolding.
2. After foundational merge, split US2 (rail mode) and US3 (undo) across developers.
3. Integrate in final polish phase.

---

## Notes

- [P] tasks indicate no blocking dependency on incomplete tasks in different files.
- Task ordering is execution-first; IDs are sequential and implementation-ready.
- Each story remains independently testable per spec requirements.
