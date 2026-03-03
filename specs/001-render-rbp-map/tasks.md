# Tasks: RBP Map Board Rendering

**Input**: Design documents from `/specs/001-render-rbp-map/`
**Prerequisites**: `plan.md` (required), `spec.md` (required), `research.md`, `data-model.md`, `contracts/`

**Tests**: No explicit TDD or automated test requirement was specified in the feature spec; this task list focuses on implementation and manual validation paths in `quickstart.md`.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare project files and UI surface for map-board feature work

- [X] T001 Add map-board page component scaffold in src/Boxcars/Components/Pages/MapBoard.razor
- [X] T002 Add map-board style scaffold in src/Boxcars/Components/Pages/MapBoard.razor.css
- [X] T003 Wire map-board component host into src/Boxcars/Components/Pages/Game.razor

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Build shared parser/view models and service registration required by all user stories

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T004 Create map domain models in src/Boxcars/Data/Maps/MapDefinition.cs
- [X] T005 [P] Create board viewport and load result models in src/Boxcars/Data/Maps/BoardViewport.cs
- [X] T006 [P] Create board element models (City, TrainDot, LineSegment) in src/Boxcars/Data/Maps/BoardElements.cs
- [X] T007 Create parser service contract in src/Boxcars/Services/Maps/IMapParserService.cs
- [X] T008 Implement base tolerant section parser skeleton in src/Boxcars/Services/Maps/RbpMapParserService.cs
- [X] T009 Register map parser and board state services in src/Boxcars/Program.cs
- [X] T030 Define async parser/resolver service signatures with CancellationToken in src/Boxcars/Services/Maps/IMapParserService.cs
- [X] T031 Implement CancellationToken propagation in src/Boxcars/Services/Maps/RbpMapParserService.cs and src/Boxcars/Services/Maps/MapBackgroundResolver.cs
- [X] T032 Verify no blocking async calls (.Result/.Wait/GetAwaiter().GetResult()) in map loading/render pipeline files

**Checkpoint**: Foundation ready - user story implementation can now begin

---

## Phase 3: User Story 1 - Load and View a Complete Board (Priority: P1) 🎯 MVP

**Goal**: Load a valid `.rbp`/`.rb3` map and render full board layers (background, city rectangles, labels, train dots, overlays)

**Independent Test**: Load sample USA map and verify complete first render alignment and same-session map replacement

### Implementation for User Story 1

- [X] T010 [US1] Implement required section parsing for header/city/label/re*/map/sep in src/Boxcars/Services/Maps/RbpMapParserService.cs
- [X] T011 [P] [US1] Implement background asset resolution policy in src/Boxcars/Services/Maps/MapBackgroundResolver.cs
- [X] T012 [US1] Implement board projection/normalization from map scale bounds in src/Boxcars/Services/Maps/BoardProjectionService.cs
- [X] T013 [P] [US1] Add Fluent file-load controls to map-board UI in src/Boxcars/Components/Pages/MapBoard.razor
- [X] T014 [US1] Implement layered board rendering (background, cities, labels, dots, lines) in src/Boxcars/Components/Pages/MapBoard.razor
- [X] T015 [US1] Connect load workflow and render state transitions in src/Boxcars/Components/Pages/MapBoard.razor
- [X] T016 [US1] Implement same-session map reload replacement behavior in src/Boxcars/Components/Pages/MapBoard.razor
- [X] T036 [US1] Preserve exact source city label text through parse→view-model→render pipeline in src/Boxcars/Services/Maps/RbpMapParserService.cs and src/Boxcars/Components/Pages/MapBoard.razor

**Checkpoint**: User Story 1 is fully functional and independently testable

---

## Phase 4: User Story 2 - Inspect Board Readability and Zoom (Priority: P2)

**Goal**: Ensure readable markers/labels and implement zoom behavior (wheel + scroll bar) with anchoring and bounds

**Independent Test**: Verify readability and zoom behavior from 25%–300% including fit-to-board default and anchor modes

### Implementation for User Story 2

- [X] T017 [US2] Implement fit-to-board initialization and zoom clamp logic (25%–300%) in src/Boxcars/Services/Maps/BoardViewportService.cs
- [X] T018 [US2] Implement cursor-centered mouse wheel zoom handling in src/Boxcars/Components/Pages/MapBoard.razor
- [X] T019 [US2] Implement viewport-centered scroll bar zoom control in src/Boxcars/Components/Pages/MapBoard.razor
- [X] T020 [US2] Apply unified zoom transform across all board layers in src/Boxcars/Components/Pages/MapBoard.razor
- [X] T021 [P] [US2] Improve marker/label readability styling across zoom levels in src/Boxcars/Components/Pages/MapBoard.razor.css

**Checkpoint**: User Stories 1 and 2 both work independently

---

## Phase 5: User Story 3 - Handle Invalid or Incomplete Inputs (Priority: P3)

**Goal**: Provide clear map-load errors and block misleading partial renders

**Independent Test**: Load malformed or incomplete files and confirm actionable errors with no partial board display

### Implementation for User Story 3

- [X] T022 [US3] Implement required-section validation and parse error classification in src/Boxcars/Services/Maps/RbpMapParserService.cs
- [X] T023 [P] [US3] Implement unsupported extension and invalid coordinate validation in src/Boxcars/Services/Maps/RbpMapParserService.cs
- [X] T024 [US3] Implement missing/unreadable background handling in src/Boxcars/Services/Maps/MapBackgroundResolver.cs
- [X] T025 [US3] Add user-facing Fluent error presentation for map load failures in src/Boxcars/Components/Pages/MapBoard.razor
- [X] T026 [US3] Enforce no-partial-render state transition on load failure in src/Boxcars/Components/Pages/MapBoard.razor

**Checkpoint**: All user stories are independently functional

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final integration cleanup and scenario verification

- [X] T027 [P] Document map file placement and background lookup conventions in specs/001-render-rbp-map/quickstart.md
- [ ] T028 Run full quickstart validation scenarios and record completion notes in specs/001-render-rbp-map/validation-report.md
- [X] T029 [P] Clean up map-board component structure and naming consistency in src/Boxcars/Components/Pages/MapBoard.razor
- [X] T033 Create timed validation procedure for SC-003 and SC-006 in specs/001-render-rbp-map/quickstart.md (sample size, timing method, pass/fail thresholds)
- [ ] T034 Execute timed validation runs and record results in specs/001-render-rbp-map/validation-report.md
- [ ] T035 Validate wheel cursor-anchor and scrollbar viewport-anchor accuracy (±10 px) and record evidence in specs/001-render-rbp-map/validation-report.md

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies; start immediately
- **Phase 2 (Foundational)**: Depends on Phase 1; blocks all user stories
- **Phase 3 (US1)**: Depends on Phase 2 completion
- **Phase 4 (US2)**: Depends on Phase 2 and uses US1 render pipeline
- **Phase 5 (US3)**: Depends on Phase 2 and integrates with US1 load pipeline
- **Phase 6 (Polish)**: Depends on completion of desired user stories

### User Story Dependencies

- **US1 (P1)**: First MVP slice after foundational work
- **US2 (P2)**: Builds on map rendering pipeline from US1 for zoom/readability behaviors
- **US3 (P3)**: Builds on parser/load pipeline from US1 for robust failures

### Within Each User Story

- Services/state logic before final UI integration
- Rendering and behavior updates before cross-cutting polish
- Complete and verify each story independently before moving on

## Parallel Opportunities

- **Setup**: None marked parallel due to shared files/components
- **Foundational**: T005 and T006 can run in parallel after T004 starts
- **US1**: T011 and T013 can run in parallel while T010/T012 progress
- **US2**: T021 can run in parallel with T018–T020
- **US3**: T023 can run in parallel with T022; T024 can proceed once resolver file exists
- **Polish**: T027 and T029 can run in parallel

## Parallel Example: User Story 1

```bash
# In parallel after parser/model foundations are in place:
Task: "T011 [US1] Implement background asset resolution policy in src/Boxcars/Services/Maps/MapBackgroundResolver.cs"
Task: "T013 [US1] Add Fluent file-load controls to map-board UI in src/Boxcars/Components/Pages/MapBoard.razor"
```

## Parallel Example: User Story 2

```bash
# In parallel once zoom state model is defined:
Task: "T018 [US2] Implement cursor-centered mouse wheel zoom handling in src/Boxcars/Components/Pages/MapBoard.razor"
Task: "T021 [US2] Improve marker/label readability styling across zoom levels in src/Boxcars/Components/Pages/MapBoard.razor.css"
```

## Parallel Example: User Story 3

```bash
# In parallel during validation hardening:
Task: "T023 [US3] Implement unsupported extension and invalid coordinate validation in src/Boxcars/Services/Maps/RbpMapParserService.cs"
Task: "T025 [US3] Add user-facing Fluent error presentation for map load failures in src/Boxcars/Components/Pages/MapBoard.razor"
```

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 (Setup)
2. Complete Phase 2 (Foundational)
3. Complete Phase 3 (US1)
4. Validate US1 independently using `quickstart.md` P1 flow
5. Demo MVP board-load capability

### Incremental Delivery

1. Deliver MVP (US1)
2. Add zoom/readability behaviors (US2)
3. Add failure hardening (US3)
4. Complete polish/cleanup tasks

### Suggested MVP Scope

- **MVP**: Through end of **Phase 3 (US1)** only
- This provides immediate value: valid map load + full board render with deterministic replacement behavior
