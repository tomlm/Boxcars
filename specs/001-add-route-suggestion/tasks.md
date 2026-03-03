# Tasks: Route Suggestion

**Input**: Design documents from `/specs/001-add-route-suggestion/`  
**Prerequisites**: `plan.md` (required), `spec.md` (required), `research.md`, `data-model.md`, `contracts/route-suggestion-ui-contract.md`, `quickstart.md`

**Tests**: Tests are not explicitly requested in the specification, so this task list focuses on implementation and validation via quickstart scenarios.

**Organization**: Tasks are grouped by user story so each story is independently implementable and testable.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare shared feature structures used by all user stories.

- [X] T001 Create route suggestion domain models in `src/Boxcars/Data/Maps/RouteSuggestionModels.cs`
- [X] T002 [P] Add route suggestion request/response contract types in `src/Boxcars/Services/Maps/MapRouteService.cs`
- [X] T003 [P] Add destination and suggestion component state fields in `src/Boxcars/Components/Pages/MapBoard.razor`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core route-cost infrastructure that MUST be complete before user story implementation.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T004 Implement ownership category and per-turn cost rules (`$1000/$5000`) in `src/Boxcars/Services/Maps/MapRouteService.cs`
- [X] T005 Implement weighted cheapest-route search (Dijkstra) in `src/Boxcars/Services/Maps/MapRouteService.cs`
- [X] T006 Implement deterministic tie-break policy (turns, switches, lexical path) in `src/Boxcars/Services/Maps/MapRouteService.cs`
- [X] T007 Implement movement-profile turn-cost handling (`TwoDie`/`ThreeDie`) in `src/Boxcars/Services/Maps/MapRouteService.cs`
- [X] T008 Implement no-route result shape and safe-return behavior in `src/Boxcars/Services/Maps/MapRouteService.cs`
- [X] T027 Implement SignalR event contract for destination/suggestion updates in `src/Boxcars/Hubs/BoxCarsHub.cs`
- [X] T028 Publish route suggestion updates to connected clients via SignalR in `src/Boxcars/Components/Pages/MapBoard.razor`

**Checkpoint**: Route suggestion engine is ready; user story implementation can begin.

---

## Phase 3: User Story 1 - Calculate cheapest route to destination city (Priority: P1) 🎯 MVP

**Goal**: Compute and return the lowest-cost valid route from current point to selected destination.

**Independent Test**: Set a current point and destination, then verify returned route reaches destination and has minimum total cost under ownership rules.

### Implementation for User Story 1

- [X] T009 [US1] Resolve active player travel profile (start node, movement type, color) in `src/Boxcars/Components/Pages/MapBoard.razor`
- [X] T010 [US1] Resolve railroad ownership lookup for route-cost evaluation in `src/Boxcars/Components/Pages/MapBoard.razor`
- [X] T011 [US1] Invoke cheapest-route calculation on destination availability in `src/Boxcars/Components/Pages/MapBoard.razor`
- [X] T012 [US1] Apply computed route result to component route-suggestion state in `src/Boxcars/Components/Pages/MapBoard.razor`
- [X] T013 [US1] Handle no-route outcome with clear user-facing state in `src/Boxcars/Components/Pages/MapBoard.razor`

**Checkpoint**: User Story 1 is functional and independently testable.

---

## Phase 4: User Story 2 - Set destination via mock helper menu (Priority: P2)

**Goal**: Allow right-click city selection of destination through a mock helper menu.

**Independent Test**: Right-click any city, choose destination action, and confirm destination updates and triggers recalculation.

### Implementation for User Story 2

- [X] T014 [US2] Add city-target right-click menu action `Set as destination` in `src/Boxcars/Components/Pages/MapBoard.razor`
- [X] T015 [US2] Add destination menu visibility/position state and dismissal flow in `src/Boxcars/Components/Pages/MapBoard.razor`
- [X] T016 [US2] Implement destination selection handler to set active destination in `src/Boxcars/Components/Pages/MapBoard.razor`
- [X] T017 [US2] Trigger route recalculation when destination changes in `src/Boxcars/Components/Pages/MapBoard.razor`
- [X] T018 [US2] Guard context-menu behavior for non-city targets and unsupported mode in `src/Boxcars/Components/Pages/MapBoard.razor`

**Checkpoint**: User Stories 1 and 2 both work independently.

---

## Phase 5: User Story 3 - Visualize suggested route points (Priority: P3)

**Goal**: Highlight each suggested route point as a circle in the active user’s color.

**Independent Test**: Generate a suggestion and verify every suggested route point is highlighted; changing destination replaces highlights.

### Implementation for User Story 3

- [X] T019 [US3] Render suggested-route point circles from suggestion state in `src/Boxcars/Components/Pages/MapBoard.razor`
- [X] T020 [US3] Map suggested node IDs to render coordinates for highlight overlays in `src/Boxcars/Components/Pages/MapBoard.razor`
- [X] T021 [US3] Apply active user color to route-point circles in `src/Boxcars/Components/Pages/MapBoard.razor`
- [X] T022 [US3] Replace prior highlight set atomically on recompute in `src/Boxcars/Components/Pages/MapBoard.razor`
- [X] T023 [US3] Add/update suggestion highlight styles in `src/Boxcars/Components/Pages/MapBoard.razor.css`

**Checkpoint**: All user stories are independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final quality pass across all stories.

- [X] T024 [P] Update feature docs for final behavior in `specs/001-add-route-suggestion/quickstart.md`
- [ ] T025 Run end-to-end scenario validation from `specs/001-add-route-suggestion/quickstart.md`
- [X] T026 [P] Run build verification for feature changes with `Boxcars.slnx` in `Boxcars.slnx`
- [ ] T029 Run SC-004 usability validation (>=90% point identification) and record outcome in `specs/001-add-route-suggestion/validation-report.md`
- [ ] T030 Validate interaction responsiveness target (<200ms perceived update) and record results in `specs/001-add-route-suggestion/validation-report.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies.
- **Phase 2 (Foundational)**: Depends on Phase 1 and blocks all user stories.
	- Includes SignalR propagation setup tasks (`T027`, `T028`) required for constitution alignment.
- **Phase 3 (US1)**: Depends on Phase 2 completion.
- **Phase 4 (US2)**: Depends on Phase 2 completion; integrates with US1 route compute trigger.
- **Phase 5 (US3)**: Depends on Phase 2 completion and consumes suggestion state produced by US1/US2.
- **Phase 6 (Polish)**: Depends on completion of target user stories.

### User Story Dependencies

- **US1 (P1)**: Starts after Foundational; no dependency on other user stories.
- **US2 (P2)**: Starts after Foundational; relies on US1 computation path for full value but remains independently testable as destination-selection flow.
- **US3 (P3)**: Starts after Foundational; relies on suggestion state from US1/US2 and remains independently testable with any available suggestion.

### Within Each User Story

- Build supporting state first.
- Implement core behavior next.
- Add guards/failure handling last.
- Validate against independent test criteria before moving to the next story.

## Parallel Opportunities

- **Setup**: `T002` and `T003` can run in parallel after `T001`.
- **Foundational**: `T004`–`T008` are sequential in one file (`MapRouteService.cs`) and should run serially.
- **Foundational**: `T004`–`T008` are sequential in one file (`MapRouteService.cs`) and should run serially; `T027` can run in parallel in `BoxCarsHub.cs`, then `T028` follows after route state integration is available.
- **US1**: `T009` and `T010` can run in parallel, then `T011`–`T013` serially.
- **US2**: `T014` and `T015` can run in parallel, then `T016`–`T018` serially.
- **US3**: `T019`–`T022` are sequential in `MapBoard.razor`; `T023` can run in parallel with late-stage wiring once overlay classes are named.
- **Polish**: `T024` and `T026` can run in parallel; `T025` follows implementation completion.
- **Polish**: `T024` and `T026` can run in parallel; `T025`, `T029`, and `T030` follow implementation completion.

## Parallel Example: User Story 1

```text
Run in parallel:
- T009 [US1] Resolve active player travel profile in src/Boxcars/Components/Pages/MapBoard.razor
- T010 [US1] Resolve railroad ownership lookup in src/Boxcars/Components/Pages/MapBoard.razor

Then run:
- T011 → T012 → T013
```

## Parallel Example: User Story 2

```text
Run in parallel:
- T014 [US2] Add city-target right-click menu action in src/Boxcars/Components/Pages/MapBoard.razor
- T015 [US2] Add destination menu state in src/Boxcars/Components/Pages/MapBoard.razor

Then run:
- T016 → T017 → T018
```

## Parallel Example: User Story 3

```text
Run sequence in MapBoard.razor:
- T019 → T020 → T021 → T022

Run in parallel with late-stage integration:
- T023 [US3] Update styles in src/Boxcars/Components/Pages/MapBoard.razor.css
```

## Implementation Strategy

### MVP First (User Story 1)

1. Complete Phase 1 (Setup).
2. Complete Phase 2 (Foundational).
3. Complete Phase 3 (US1).
4. Validate US1 independently using cheapest-route scenarios.

### Incremental Delivery

1. Ship MVP with US1.
2. Add US2 destination helper menu and revalidate.
3. Add US3 route-point highlighting and revalidate.
4. Finish with polish/build verification.

### Suggested MVP Scope

- **MVP**: Phase 1 + Phase 2 + Phase 3 (through T013).
- **Post-MVP**: US2/US3 and polish tasks.

## Notes

- All tasks use strict checklist format with ID, optional `[P]`, optional story label, and explicit file path.
- Story labels are used only for user-story phases.
- This plan preserves existing route-node revisit and append-first context-menu behavior constraints.
