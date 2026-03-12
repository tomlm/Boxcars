# Tasks: Purchase Phase Buying and Map Analysis

**Input**: Design documents from `/specs/001-purchase-railroads/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/purchase-phase-ui-contract.md, quickstart.md

**Tests**: Add targeted regression coverage for purchase actions, pricing rules, and map analysis because this feature changes authoritative turn logic and introduces derived analysis data.

**Organization**: Tasks are grouped by user story so each slice can be implemented and validated independently.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Add the configuration, view-model, and component scaffolding the purchase-phase feature builds on.

- [x] T001 Configure purchase rules binding and default Superchief pricing in src/Boxcars/Program.cs, src/Boxcars/appsettings.json, and src/Boxcars/appsettings.Development.json
- [x] T002 Create purchase-phase UI models in src/Boxcars/Data/PurchasePhaseModel.cs, src/Boxcars/Data/PurchaseOptionModel.cs, src/Boxcars/Data/NetworkCoverageModel.cs, and src/Boxcars/Data/MapAnalysisModel.cs
- [x] T003 [P] Scaffold inline purchase components in src/Boxcars/Components/GameBoard/PurchaseTaskbar.razor, src/Boxcars/Components/GameBoard/PurchaseTaskbar.razor.css, src/Boxcars/Components/GameBoard/RailroadPurchaseOverlay.razor, src/Boxcars/Components/GameBoard/RailroadPurchaseOverlay.razor.css, and src/Boxcars/Components/GameBoard/PurchaseAnalysisReport.razor

**Checkpoint**: Configuration and UI/model shells exist, so shared purchase-phase plumbing can begin.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Implement the shared pricing, mapping, and analysis foundations required by every story.

**⚠️ CRITICAL**: No user story work should be considered complete until this phase is finished.

- [x] T004 Extend purchase-phase turn state in src/Boxcars/Data/ArrivalResolutionModel.cs and src/Boxcars/Data/BoardTurnViewState.cs to carry tabs, selection, notifications, and purchase-control visibility
- [x] T005 Implement authoritative engine-upgrade pricing and validation in src/Boxcars.Engine/Domain/GameEngine.cs and src/Boxcars/GameEngine/GameEngineService.cs
- [x] T006 [P] Implement railroad network coverage calculations in src/Boxcars/Services/NetworkCoverageService.cs
- [x] T007 [P] Implement reusable map-analysis and recommendation dataset services in src/Boxcars/Services/MapAnalysisService.cs and src/Boxcars/Services/PurchaseRecommendationService.cs
- [x] T008 Wire purchase-phase model construction into src/Boxcars/Services/GameBoardStateMapper.cs

**Checkpoint**: The server and mapper can now produce authoritative purchase-phase state for the UI.

---

## Phase 3: User Story 1 - Make one purchase action (Priority: P1) 🎯 MVP

**Goal**: Let the active player buy exactly one railroad or one engine upgrade, or decline, using authoritative BUY/DECLINE controls.

**Independent Test**: Start a purchase phase with at least one affordable option, complete either one railroad purchase or one engine upgrade, and verify the correct cash deduction, resulting state change, and transition to fee payment. Also verify skip/no-opportunity cases do not mutate state.

### Implementation for User Story 1

- [x] T009 [US1] Build sorted railroad and engine purchase option composition in src/Boxcars/Services/GameBoardStateMapper.cs
- [x] T010 [P] [US1] Replace the placeholder purchase interaction with taskbar BUY and DECLINE controls in src/Boxcars/Components/GameBoard/PurchaseTaskbar.razor and src/Boxcars/Components/GameBoard/PurchaseTaskbar.razor.css
- [x] T011 [US1] Integrate inline purchase controls and selected-option state into src/Boxcars/Components/Pages/GameBoard.razor and src/Boxcars/Components/Pages/GameBoard.razor.css
- [x] T012 [US1] Dispatch PurchaseRailroadAction, BuyEngineAction, and DeclinePurchaseAction from src/Boxcars/Components/Pages/GameBoard.razor using src/Boxcars/GameEngine/PlayerAction.cs
- [x] T013 [US1] Enforce affordability, single-purchase, skip, and no-opportunity outcomes in src/Boxcars/GameEngine/GameEngineService.cs and src/Boxcars/Services/GameBoardStateMapper.cs
- [x] T014 [US1] Surface purchase-phase result messaging for railroad, engine, decline, and affordability cases in src/Boxcars/GameEngine/GameEngineService.cs and src/Boxcars/Components/Pages/GameBoard.razor

**Checkpoint**: The player can take exactly one valid purchase action, decline, or be auto-skipped to fee payment without incorrect state changes.

---

## Phase 4: User Story 2 - Evaluate the map impact before confirming (Priority: P2)

**Goal**: Keep the map and taskbar selection synchronized so the player can compare railroad impact before buying.

**Independent Test**: On the Map tab, select railroads from both the map and the taskbar and verify the highlight, overlay, combobox selection, and current/projected network statistics always match.

### Implementation for User Story 2

- [x] T015 [US2] Add railroad-selection callbacks and highlight state to src/Boxcars/Components/Map/GameMapComponent.razor and src/Boxcars/Components/Pages/GameBoard.razor
- [x] T016 [P] [US2] Implement railroad overlay rendering for price, access delta, and monopoly delta in src/Boxcars/Components/GameBoard/RailroadPurchaseOverlay.razor and src/Boxcars/Components/GameBoard/RailroadPurchaseOverlay.razor.css
- [x] T017 [US2] Compute current and projected coverage snapshots plus overlay deltas in src/Boxcars/Services/NetworkCoverageService.cs and src/Boxcars/Services/GameBoardStateMapper.cs
- [x] T018 [US2] Synchronize map clicks and taskbar selection across railroad and engine options in src/Boxcars/Components/Pages/GameBoard.razor and src/Boxcars/Components/GameBoard/PurchaseTaskbar.razor
- [x] T019 [US2] Render current/projected network stats and engine outcome details in src/Boxcars/Components/GameBoard/PurchaseTaskbar.razor and src/Boxcars/Components/GameBoard/PurchaseTaskbar.razor.css

**Checkpoint**: Railroad selection is spatially visible and strategically informative before BUY is activated.

---

## Phase 5: User Story 3 - Review map analysis and recommendations (Priority: P3)

**Goal**: Provide a map-derived Information tab report and expose the same structured dataset to recommendation logic.

**Independent Test**: Open the Information tab during a purchase phase and verify it renders railroad, city, region, and trip metrics from the loaded map while preserving the active purchase selection when switching tabs.

### Implementation for User Story 3

- [x] T020 [US3] Generate railroad, city, region, and trip summary metrics from loaded map data in src/Boxcars/Services/MapAnalysisService.cs
- [x] T021 [P] [US3] Shape Information-tab report models in src/Boxcars/Data/MapAnalysisModel.cs and src/Boxcars/Services/GameBoardStateMapper.cs
- [x] T022 [US3] Render the Information tab report in src/Boxcars/Components/GameBoard/PurchaseAnalysisReport.razor and src/Boxcars/Components/Pages/GameBoard.razor
- [x] T023 [US3] Expose a shared recommendation input dataset from src/Boxcars/Services/PurchaseRecommendationService.cs and src/Boxcars/Services/MapAnalysisService.cs
- [x] T024 [US3] Preserve the active purchase selection while switching between Map and Information tabs in src/Boxcars/Components/Pages/GameBoard.razor and src/Boxcars/Components/GameBoard/PurchaseTaskbar.razor

**Checkpoint**: The Information tab is useful to the player and recommendation logic without duplicating analysis code.

---

## Phase 6: User Story 4 - Keep the map visible while deciding (Priority: P4)

**Goal**: Finish the page-owned purchase experience so the board stays visible and the viewport ends in the correct mode for every exit path.

**Independent Test**: Enter and exit the purchase phase through success, decline, skip, and no-opportunity flows and verify the board stays visible, the map starts in railroad-selection mode only when appropriate, and ends in zoomed-out move mode with temporary highlights cleared.

### Implementation for User Story 4

- [x] T025 [US4] Retire the placeholder purchase dialog path in src/Boxcars/Components/GameBoard/PurchaseOpportunityDialog.razor and src/Boxcars/Components/Pages/GameBoard.razor
- [x] T026 [US4] Apply purchase-phase viewport transitions for open, skip, decline, and commit in src/Boxcars/Services/Maps/BoardViewportService.cs and src/Boxcars/Components/Pages/GameBoard.razor
- [x] T027 [US4] Clear temporary railroad highlights and overlays when purchase mode ends in src/Boxcars/Components/Map/GameMapComponent.razor and src/Boxcars/Components/Pages/GameBoard.razor
- [x] T028 [P] [US4] Keep inline purchase controls visible without obscuring the board on desktop and mobile in src/Boxcars/Components/Pages/GameBoard.razor.css and src/Boxcars/Components/GameBoard/PurchaseTaskbar.razor.css

**Checkpoint**: The inline purchase UX preserves map visibility and leaves the board in the correct post-purchase state.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Lock down regression coverage and validate the end-to-end feature against the quickstart scenarios.

- [x] T029 [P] Add regression coverage for purchase actions and configurable pricing in tests/Boxcars.Engine.Tests/Unit/PurchasePhaseActionTests.cs and tests/Boxcars.Engine.Tests/Unit/PurchaseRulesConfigurationTests.cs
- [x] T030 [P] Add regression coverage for map analysis outputs in tests/Boxcars.Engine.Tests/Unit/MapAnalysisTests.cs
- [x] T031 Run the purchase-phase validation scenarios in specs/001-purchase-railroads/quickstart.md

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1: Setup** has no dependencies and can start immediately.
- **Phase 2: Foundational** depends on Phase 1 and blocks all story work.
- **Phase 3: User Story 1** depends on Phase 2 and delivers the MVP purchase flow.
- **Phase 4: User Story 2** depends on Phase 2 and integrates cleanly with the Phase 3 controls.
- **Phase 5: User Story 3** depends on Phase 2 and can proceed once the shared analysis services are available.
- **Phase 6: User Story 4** depends on Phase 2 and finalizes the page-owned map experience around the completed purchase controls.
- **Phase 7: Polish** depends on the stories that are in scope for the release.

### User Story Dependencies

- **US1 (P1)**: Starts after Phase 2 and provides the MVP purchase workflow.
- **US2 (P2)**: Starts after Phase 2 and layers synchronized map evaluation onto the purchase flow.
- **US3 (P3)**: Starts after Phase 2 and layers the Information tab plus recommendation inputs onto the purchase flow.
- **US4 (P4)**: Starts after Phase 2 and finalizes the inline map-visible experience and viewport cleanup behavior.

### Within Each User Story

- Shared mapper or service changes should land before the page/component integration that consumes them.
- Selection-state plumbing should land before BUY/DECLINE enablement logic.
- Derived analysis data should land before Information-tab rendering.
- Viewport cleanup should land before responsive styling polish.

## Parallel Opportunities

- T003 can run in parallel with T001-T002 once the file targets are agreed.
- T006 and T007 can run in parallel after T004-T005 because they touch separate services.
- In US1, T010 can run in parallel with T009 while the mapper contract is being finalized.
- In US2, T016 can run in parallel with T015/T017 because the overlay component is isolated from the map callback and coverage logic.
- In US3, T021 can run in parallel with T020 once the map-analysis output shape is agreed.
- In US4, T028 can run in parallel with T026-T027 after the page-owned control layout is in place.
- T029 and T030 can run in parallel during polish.

## Parallel Example: User Story 1

```text
T009 Build sorted railroad and engine purchase option composition in src/Boxcars/Services/GameBoardStateMapper.cs
T010 Replace the placeholder purchase interaction with taskbar BUY and DECLINE controls in src/Boxcars/Components/GameBoard/PurchaseTaskbar.razor and src/Boxcars/Components/GameBoard/PurchaseTaskbar.razor.css
```

## Parallel Example: User Story 2

```text
T015 Add railroad-selection callbacks and highlight state to src/Boxcars/Components/Map/GameMapComponent.razor and src/Boxcars/Components/Pages/GameBoard.razor
T016 Implement railroad overlay rendering for price, access delta, and monopoly delta in src/Boxcars/Components/GameBoard/RailroadPurchaseOverlay.razor and src/Boxcars/Components/GameBoard/RailroadPurchaseOverlay.razor.css
T017 Compute current and projected coverage snapshots plus overlay deltas in src/Boxcars/Services/NetworkCoverageService.cs and src/Boxcars/Services/GameBoardStateMapper.cs
```

## Parallel Example: User Story 3

```text
T020 Generate railroad, city, region, and trip summary metrics from loaded map data in src/Boxcars/Services/MapAnalysisService.cs
T021 Shape Information-tab report models in src/Boxcars/Data/MapAnalysisModel.cs and src/Boxcars/Services/GameBoardStateMapper.cs
```

## Implementation Strategy

### MVP First

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational.
3. Complete Phase 3: User Story 1.
4. Validate the purchase, decline, skip, and no-opportunity flows before expanding scope.

### Incremental Delivery

1. Deliver US1 to replace the stub with a working authoritative purchase flow.
2. Add US2 to make railroad choices strategically meaningful on the map.
3. Add US3 to surface the map-analysis report and recommendation inputs.
4. Add US4 to finalize the non-modal, map-visible purchase experience and viewport cleanup.
5. Finish with regression coverage and quickstart validation.

### Parallel Team Strategy

1. One developer completes Phase 1 and T004-T005.
2. A second developer can take T006 while another takes T007.
3. After Phase 2, split across US1, US2, and US3 by service/component boundaries.
4. Reserve US4 and Phase 7 for integration hardening once the core stories are merged.

## Notes

- Total tasks: 31
- User story task counts: US1 = 6, US2 = 5, US3 = 5, US4 = 4
- Parallelizable tasks: T003, T006, T007, T010, T016, T021, T028, T029, T030
- The MVP scope is Phase 3 / US1 only.
- All tasks follow the required checklist format: checkbox, task ID, optional `[P]`, required story label for story tasks, and exact file paths.