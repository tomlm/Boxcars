# Tasks: Forced Railroad Sales and Auctions

**Input**: Design documents from `/specs/001-sell-railroads/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Tests are required for this feature because it changes non-trivial fee resolution, multiplayer turn flow, and advisory network calculations.

**Organization**: Tasks are grouped by user story so each story can be implemented and validated independently after the foundational phase completes.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish shared action and state scaffolding for forced-sale implementation.

- [X] T001 Add forced-sale and auction player action records in src/Boxcars/GameEngine/PlayerAction.cs
- [X] T002 [P] Create forced-sale board-state models in src/Boxcars/Data/ForcedSalePhaseModel.cs
- [X] T003 [P] Extend persisted snapshot DTOs for forced-sale and auction state in src/Boxcars.Engine/Persistence/GameState.cs

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Build the authoritative turn-state, action-processing, and state-mapping infrastructure required by every user story.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T004 Extend turn state to track fee shortfall, forced-sale selection, and auction session data in src/Boxcars.Engine/Domain/Turn.cs
- [X] T005 Implement snapshot restore and serialization support for forced-sale and auction turn state in src/Boxcars.Engine/Domain/GameEngine.cs
- [X] T006 [P] Extend queued action processing and authorization for forced-sale and auction actions in src/Boxcars/GameEngine/GameEngineService.cs
- [X] T007 [P] Add foundational event serialization and snapshot regression coverage in tests/Boxcars.Engine.Tests/Unit/SerializationTests.cs
- [X] T008 [P] Add baseline board-state mapping support for forced-sale models in src/Boxcars/Services/GameBoardStateMapper.cs

**Checkpoint**: Foundation ready. Forced-sale actions, persisted turn state, and board-state mapping are available for story work.

---

## Phase 3: User Story 1 - Raise cash during fee payment (Priority: P1) 🎯 MVP

**Goal**: Let the active player sell owned railroads to the bank during fee payment until enough cash is raised or no sale options remain.

**Independent Test**: Start `UseFees` with an active player who owes more cash than they hold, sell one owned railroad to the bank, and verify cash increases by half price, ownership is removed, the railroad returns to the unowned pool, and fee resolution either repeats or completes correctly.

### Tests for User Story 1

- [X] T009 [P] [US1] Add forced-sale entry and repeated fee-resolution tests in tests/Boxcars.Engine.Tests/Unit/UseFeesTests.cs
- [X] T010 [P] [US1] Add direct bank-sale action and ownership transfer tests in tests/Boxcars.Engine.Tests/Unit/RailroadPurchaseTests.cs

### Implementation for User Story 1

- [X] T011 [US1] Implement fee shortfall detection and direct bank-sale resolution in src/Boxcars.Engine/Domain/GameEngine.cs
- [X] T012 [US1] Handle SellRailroadAction and forced-sale change summaries in src/Boxcars/GameEngine/GameEngineService.cs
- [X] T013 [US1] Map active forced-sale state, sale candidates, and bank-sale affordances in src/Boxcars/Services/GameBoardStateMapper.cs
- [X] T014 [US1] Add forced-sale controls and bank-sale dispatch to src/Boxcars/Components/Pages/GameBoard.razor
- [X] T015 [US1] Add forced-sale timeline descriptions for bank sales and fee rechecks in src/Boxcars/Services/GameService.cs

**Checkpoint**: User Story 1 is fully functional and independently testable as the MVP.

---

## Phase 4: User Story 2 - Evaluate which railroad to sell (Priority: P2)

**Goal**: Show owned-railroad selection, sale impact, and the always-available Network tab so the player can judge which railroad to liquidate.

**Independent Test**: Enter forced sale, select different owned railroads from the map, and verify the map only selects owned railroads, the sale impact panel updates, and the Network tab shows both current network values and projected sale impact.

### Tests for User Story 2

- [X] T016 [P] [US2] Add sale-impact projection tests for ownership removal in tests/Boxcars.Engine.Tests/Unit/NetworkCoverageServiceTests.cs
- [X] T017 [P] [US2] Add forced-sale board-state mapping tests in tests/Boxcars.Engine.Tests/Unit/ForcedSaleStateMapperTests.cs

### Implementation for User Story 2

- [X] T018 [US2] Implement ownership-removal network projections in src/Boxcars/Services/NetworkCoverageService.cs
- [X] T019 [US2] Map Network tab summaries and selected sale-impact snapshots in src/Boxcars/Services/GameBoardStateMapper.cs
- [X] T020 [P] [US2] Create the network summary UI component in src/Boxcars/Components/GameBoard/NetworkTabPanel.razor
- [X] T021 [P] [US2] Create the sale impact UI component in src/Boxcars/Components/GameBoard/SaleImpactPanel.razor
- [X] T022 [US2] Restrict selectable railroads to the seller's owned lines in src/Boxcars/Components/Map/MapComponent.razor
- [X] T023 [US2] Integrate the Network tab and sale-impact panels into src/Boxcars/Components/Pages/GameBoard.razor

**Checkpoint**: User Stories 1 and 2 both work, and the player can choose the least damaging railroad to sell.

---

## Phase 5: User Story 3 - Auction a railroad to other players (Priority: P3)

**Goal**: Let the active player auction an owned railroad to other active players with bid, pass, and drop-out turns persisted and propagated in order.

**Independent Test**: Start forced sale, launch an auction for an owned railroad, have multiple players bid, pass, or drop out, and verify the winner or bank fallback resolves correctly with synchronized state and event history.

### Tests for User Story 3

- [X] T024 [P] [US3] Add auction turn-order, pass, drop-out, and no-bid fallback tests in tests/Boxcars.Engine.Tests/Unit/AuctionTests.cs
- [X] T025 [P] [US3] Add queued auction action authorization tests in tests/Boxcars.Engine.Tests/Unit/ForcedSaleActionTests.cs

### Implementation for User Story 3

- [X] T026 [US3] Implement persisted auction session state and bid rotation in src/Boxcars.Engine/Domain/GameEngine.cs
- [X] T027 [US3] Add auction pass and drop-out action records in src/Boxcars/GameEngine/PlayerAction.cs
- [X] T028 [US3] Handle auction bid, pass, and drop-out actions in src/Boxcars/GameEngine/GameEngineService.cs
- [X] T029 [US3] Map auction state, participants, and current bidder actions in src/Boxcars/Services/GameBoardStateMapper.cs
- [X] T030 [P] [US3] Create the multiplayer auction panel in src/Boxcars/Components/GameBoard/RailroadAuctionPanel.razor
- [X] T031 [US3] Integrate auction launch, bidder controls, and auction state rendering into src/Boxcars/Components/Pages/GameBoard.razor
- [X] T032 [US3] Add auction event descriptions to the timeline in src/Boxcars/Services/GameService.cs

**Checkpoint**: User Stories 1, 2, and 3 all work, including authoritative multiplayer auction resolution.

---

## Phase 6: User Story 4 - Leave the game when unable to cover debt (Priority: P4)

**Goal**: Eliminate players who still cannot pay after exhausting all railroad sales, while preserving spectator visibility and blocking future gameplay actions.

**Independent Test**: Run fee resolution for a player whose cash plus all sale proceeds is still below the amount owed, complete all required sales, and verify the player becomes spectator-only and is excluded from future actions and auctions.

### Tests for User Story 4

- [X] T033 [P] [US4] Add insolvency elimination coverage in tests/Boxcars.Engine.Tests/Unit/BankruptcyTests.cs
- [X] T034 [P] [US4] Add eliminated-player action restriction tests in tests/Boxcars.Engine.Tests/Unit/ForcedSaleActionTests.cs

### Implementation for User Story 4

- [X] T035 [US4] Implement elimination after exhausted sale options in src/Boxcars.Engine/Domain/GameEngine.cs
- [X] T036 [US4] Enforce spectator-only action restrictions for eliminated players in src/Boxcars/GameEngine/GameEngineService.cs
- [X] T037 [US4] Prevent eliminated players from controlling active-turn UI in src/Boxcars/Data/PlayerControlRules.cs
- [X] T038 [US4] Surface eliminated-player spectator messaging and disabled actions in src/Boxcars/Components/Pages/GameBoard.razor

**Checkpoint**: All user stories are functional, including clean spectator-only elimination.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Final regression coverage, UX cleanup, and scenario validation across all stories.

- [X] T039 [P] Add regression coverage for purchase-to-fee transitions after forced-sale changes in tests/Boxcars.Engine.Tests/Unit/PurchasePhaseActionTests.cs
- [X] T040 [P] Add reconnect and observability regression coverage for forced-sale snapshots in tests/Boxcars.Engine.Tests/Unit/ObservabilityTests.cs
- [ ] T041 Run the forced-sale quickstart scenarios and update validation notes in specs/001-sell-railroads/quickstart.md
- [X] T042 Refine cross-story user-facing sale and auction messaging in src/Boxcars/Services/GameService.cs

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies and can start immediately.
- **Foundational (Phase 2)**: Depends on Phase 1 and blocks all user story work.
- **User Story 1 (Phase 3)**: Depends on Phase 2 and delivers the MVP.
- **User Story 2 (Phase 4)**: Depends on Phase 2 and builds on the forced-sale state exposed by US1.
- **User Story 3 (Phase 5)**: Depends on Phase 2 and reuses the forced-sale shell completed in US1.
- **User Story 4 (Phase 6)**: Depends on Phase 2 and is safest after US1 and US3 because it relies on final fee-recheck outcomes after all sale paths.
- **Polish (Phase 7)**: Depends on the desired user stories being complete.

### User Story Dependencies

- **US1 (P1)**: No user story dependencies after the foundational phase.
- **US2 (P2)**: Can start after the foundational phase, but integration is simplest once US1 exposes the forced-sale entry state.
- **US3 (P3)**: Can start after the foundational phase, but integration is simplest once US1 exposes the forced-sale entry state.
- **US4 (P4)**: Depends on the completed fee-recheck and sale outcomes from US1 and the auction completion paths from US3.

### Within Each User Story

- Tests should be written before the corresponding implementation tasks and should fail first.
- Engine/domain changes should land before queue-processing or UI integration tasks.
- State mapping should precede page/component wiring.
- Each story should reach its checkpoint before the next dependent story is merged.

### Parallel Opportunities

- `T002` and `T003` can run in parallel during Setup.
- `T006`, `T007`, and `T008` can run in parallel during Foundational work once `T004` is in place.
- `T009` and `T010` can run in parallel for US1.
- `T016` and `T017` can run in parallel for US2, as can `T020` and `T021`.
- `T024` and `T025` can run in parallel for US3, and `T030` can proceed once auction state contracts are stable.
- `T033` and `T034` can run in parallel for US4.
- `T039` and `T040` can run in parallel during Polish.

---

## Parallel Example: User Story 1

```text
T009 [US1] Add forced-sale entry and repeated fee-resolution tests in tests/Boxcars.Engine.Tests/Unit/UseFeesTests.cs
T010 [US1] Add direct bank-sale action and ownership transfer tests in tests/Boxcars.Engine.Tests/Unit/RailroadPurchaseTests.cs
```

## Parallel Example: User Story 2

```text
T020 [US2] Create the network summary UI component in src/Boxcars/Components/GameBoard/NetworkTabPanel.razor
T021 [US2] Create the sale impact UI component in src/Boxcars/Components/GameBoard/SaleImpactPanel.razor
```

## Parallel Example: User Story 3

```text
T024 [US3] Add auction turn-order, pass, drop-out, and no-bid fallback tests in tests/Boxcars.Engine.Tests/Unit/AuctionTests.cs
T025 [US3] Add queued auction action authorization tests in tests/Boxcars.Engine.Tests/Unit/ForcedSaleActionTests.cs
T030 [US3] Create the multiplayer auction panel in src/Boxcars/Components/GameBoard/RailroadAuctionPanel.razor
```

## Parallel Example: User Story 4

```text
T033 [US4] Add insolvency elimination coverage in tests/Boxcars.Engine.Tests/Unit/BankruptcyTests.cs
T034 [US4] Add eliminated-player action restriction tests in tests/Boxcars.Engine.Tests/Unit/ForcedSaleActionTests.cs
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational.
3. Complete Phase 3: User Story 1.
4. Validate the US1 checkpoint before expanding to advisory UI or auctions.

### Incremental Delivery

1. Ship US1 for authoritative forced-sale and bank-sale behavior.
2. Add US2 for sale-impact decision support and the always-available Network tab.
3. Add US3 for multiplayer auction flow.
4. Add US4 for clean spectator-only insolvency elimination.
5. Finish with Phase 7 regression and quickstart validation.

### Parallel Team Strategy

1. One developer completes Setup and foundational engine/action work.
2. After Phase 2, one developer can continue US1 while another prepares US2 projection/UI components.
3. Once US1 is stable, auction work in US3 can proceed in parallel with US2 polish.
4. US4 follows once bank-sale and auction completion paths are both authoritative.

---

## Notes

- `[P]` tasks touch different files or depend only on completed foundational work.
- User story labels map each task directly to the corresponding spec story.
- Each story is sliced to remain demonstrable on its own after the checkpoint.
- Tasks intentionally target the existing Boxcars engine, action queue, mapper, and MudBlazor UI structure rather than introducing new projects or parallel rule systems.