# Tasks: Better Routing for Unfriendly Destinations

**Input**: Design documents from `/specs/005-better-unfriendly-routing/`  
**Prerequisites**: `plan.md`, `spec.md`

**Tests**: Tests are required for this feature because the spec explicitly requires focused regression coverage for route ranking, fee semantics, bonus-out evaluation, and tie breaks.

**Organization**: Tasks are grouped by user story so each story can be implemented and tested independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story the task belongs to (`US1`, `US2`, `US3`)
- Every task includes exact file paths

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare the feature documentation and shared test targets

- [ ] T001 Update feature notes and implementation assumptions in `specs/005-better-unfriendly-routing/plan.md`
- [ ] T002 Identify route-suggestion fixture cases to extend in `tests/Boxcars.Engine.Tests/Unit/MapRouteServiceTests.cs`, `tests/Boxcars.Engine.Tests/Unit/RouteSuggestionTests.cs`, and `tests/Boxcars.Engine.Tests/Unit/GameEngineSettingsFeeTests.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared routing primitives and evaluation data needed by all user stories

**⚠️ CRITICAL**: No user story work should begin until this phase is complete

- [ ] T003 Extend shared advisory routing state and ranking metadata in `src/Boxcars/Data/Maps/RouteSuggestionModels.cs`
- [ ] T004 Implement shared effective-fee evaluation hooks for route suggestion in `src/Boxcars.Engine/Domain/GameEngine.cs`
- [ ] T005 Implement shared candidate comparison scaffolding for arrival cost, exit outlook, and deterministic ordering in `src/Boxcars/Services/Maps/MapRouteService.cs`
- [ ] T006 Expose reusable network-strength comparison inputs needed by routing tie breaks in `src/Boxcars/Services/NetworkCoverageService.cs`

**Checkpoint**: Shared route-ranking infrastructure is ready for user-story work

---

## Phase 3: User Story 1 - Choose the cheapest unfriendly-destination plan (Priority: P1) 🎯 MVP

**Goal**: Pick the route with the best combined arrival-plus-next-turn fee outlook for unfriendly destinations

**Independent Test**: Build unfriendly-destination scenarios where the best route is not the cheapest immediate arrival path, then verify the selected route minimizes the combined arrival and exit outlook.

### Tests for User Story 1 ⚠️

> **NOTE**: Write these tests first and verify they fail before implementation.

- [ ] T007 [P] [US1] Add route-service tests for combined arrival-plus-exit route ranking in `tests/Boxcars.Engine.Tests/Unit/MapRouteServiceTests.cs`
- [ ] T008 [P] [US1] Add engine-level route suggestion tests for unfriendly destinations in `tests/Boxcars.Engine.Tests/Unit/RouteSuggestionTests.cs`
- [ ] T009 [P] [US1] Add configured-fee and grandfathered-fee regression tests for route ranking in `tests/Boxcars.Engine.Tests/Unit/GameEngineSettingsFeeTests.cs`

### Implementation for User Story 1

- [ ] T010 [US1] Extend route suggestion request and result data for post-arrival outlook details in `src/Boxcars/Data/Maps/RouteSuggestionModels.cs`
- [ ] T011 [US1] Implement combined arrival-plus-next-turn route evaluation in `src/Boxcars/Services/Maps/MapRouteService.cs`
- [ ] T012 [US1] Feed authoritative ownership, effective fee, and destination context into `SuggestRouteForPlayer` in `src/Boxcars.Engine/Domain/GameEngine.cs`
- [ ] T013 [US1] Surface unfriendly-destination evaluation details in route suggestion projections in `src/Boxcars/Data/Maps/RouteSuggestionProjection.cs`

**Checkpoint**: User Story 1 is independently functional and testable

---

## Phase 4: User Story 2 - Account for bonus-out opportunities (Priority: P2)

**Goal**: Prefer routes with better authoritative bonus-out odds when heading into an unfriendly destination

**Independent Test**: Compare equal-cost unfriendly-destination routes where only one has better `BonusOut` probability and verify that route is selected.

### Tests for User Story 2 ⚠️

- [ ] T014 [P] [US2] Add exact bonus-out probability ranking tests in `tests/Boxcars.Engine.Tests/Unit/MapRouteServiceTests.cs`
- [ ] T015 [P] [US2] Add locomotive-specific bonus-out tests for `Express` and `Superchief` in `tests/Boxcars.Engine.Tests/Unit/RouteSuggestionTests.cs`

### Implementation for User Story 2

- [ ] T016 [US2] Extend route suggestion inputs with authoritative locomotive and bonus-roll context in `src/Boxcars/Data/Maps/RouteSuggestionModels.cs`
- [ ] T017 [US2] Implement exact `BonusOut` probability evaluation in `src/Boxcars/Services/Maps/MapRouteService.cs`
- [ ] T018 [US2] Supply authoritative locomotive and bonus-roll inputs from `src/Boxcars.Engine/Domain/GameEngine.cs`
- [ ] T019 [US2] Pass the richer route-suggestion context through map recomputation in `src/Boxcars/Components/Map/MapComponent.razor`

**Checkpoint**: User Stories 1 and 2 are independently functional and testable

---

## Phase 5: User Story 3 - Prefer strategically better fee recipients when costs tie (Priority: P3)

**Goal**: Break equal-cost routes by least-cash owner, weakest network, and payment spreading

**Independent Test**: Create tied unfriendly-destination routes with different owner beneficiaries and verify the planner applies the strategic tie breaks deterministically.

### Tests for User Story 3 ⚠️

- [ ] T020 [P] [US3] Add least-cash and weakest-network tie-break tests in `tests/Boxcars.Engine.Tests/Unit/MapRouteServiceTests.cs`
- [ ] T021 [P] [US3] Add owner-spread tie-break tests in `tests/Boxcars.Engine.Tests/Unit/RouteSuggestionTests.cs`
- [ ] T022 [P] [US3] Add projection/detail tests for chosen-route rationale in `tests/Boxcars.Engine.Tests/Unit/RouteSuggestionProjectionTests.cs`

### Implementation for User Story 3

- [ ] T023 [US3] Extend route-ranking metadata for fee recipients and payment spread in `src/Boxcars/Data/Maps/RouteSuggestionModels.cs`
- [ ] T024 [US3] Implement least-cash, weakest-network, and spread-payment tie breaks in `src/Boxcars/Services/Maps/MapRouteService.cs`
- [ ] T025 [US3] Reuse authoritative network metrics for route tie breaking in `src/Boxcars/Services/NetworkCoverageService.cs`
- [ ] T026 [US3] Expose route-selection rationale needed by the UI in `src/Boxcars/Data/Maps/RouteSuggestionProjection.cs` and `src/Boxcars/Components/Map/MapComponent.razor`

**Checkpoint**: All user stories are independently functional and testable

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final validation across the whole feature

- [ ] T027 Re-run and stabilize route suggestion regression coverage in `tests/Boxcars.Engine.Tests/Unit/MapRouteServiceTests.cs`, `tests/Boxcars.Engine.Tests/Unit/RouteSuggestionTests.cs`, `tests/Boxcars.Engine.Tests/Unit/GameEngineSettingsFeeTests.cs`, and `tests/Boxcars.Engine.Tests/Unit/RouteSuggestionProjectionTests.cs`
- [ ] T028 Review route-suggestion outputs for advisory-only behavior and deterministic ranking in `src/Boxcars.Engine/Domain/GameEngine.cs` and `src/Boxcars/Services/Maps/MapRouteService.cs`
- [ ] T029 Update implementation notes in `specs/005-better-unfriendly-routing/plan.md` if final route-ranking behavior differs from the current planning assumptions

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: Starts immediately
- **Phase 2 (Foundational)**: Depends on Phase 1 and blocks all user stories
- **Phase 3 (US1)**: Starts after Phase 2 and delivers the MVP
- **Phase 4 (US2)**: Starts after Phase 2 and depends on the shared routing model from US1
- **Phase 5 (US3)**: Starts after Phase 2 and depends on the shared routing model from US1
- **Phase 6 (Polish)**: Starts after the desired user stories are complete

### User Story Dependencies

- **US1**: No dependency on other user stories; this is the MVP
- **US2**: Depends on the richer route-ranking structure introduced for US1
- **US3**: Depends on the richer route-ranking structure introduced for US1 and the network metrics exposed during Phase 2

### Within Each User Story

- Tests must be added before implementation and should fail first
- Shared models before ranking logic
- Ranking logic before projections/UI plumbing
- Engine wiring after service-level ranking behavior is defined

### Parallel Opportunities

- `T007`, `T008`, and `T009` can run in parallel
- `T014` and `T015` can run in parallel
- `T020`, `T021`, and `T022` can run in parallel
- `T013`, `T025`, and `T026` can run in parallel once their dependencies are satisfied

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 and Phase 2
2. Complete User Story 1
3. Validate combined arrival-plus-exit route ranking
4. Stop and review before bonus-out and strategic tie-break work

### Incremental Delivery

1. Deliver US1 for combined arrival and exit planning
2. Add US2 for exact bonus-out probability handling
3. Add US3 for strategic tie breaks and route rationale details
4. Finish with full regression coverage and deterministic-validation review

### Parallel Team Strategy

1. One developer handles shared route model and service scaffolding
2. One developer expands engine/test fixtures in parallel after the model stabilizes
3. After US1 lands, bonus-out and tie-break work can proceed in parallel if coordination stays tight around `RouteSuggestionModels.cs` and `MapRouteService.cs`

---

## Notes

- `[P]` tasks touch different files and can run in parallel
- Each user story remains independently testable from the route suggestion surface
- This feature must keep advisory routing derived from authoritative rules and settings
- Avoid introducing a separate client-side rules engine or hard-coded fee table
