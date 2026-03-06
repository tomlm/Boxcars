# Tasks: Observable Game Engine Object Model

**Input**: Design documents from `/specs/004-observable-game-model/`  
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/game-engine-api.md, quickstart.md

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Align project scaffolding and test baseline for engine development.

- [X] T001 Update engine package references in src/Boxcars.GameEngine/Boxcars.GameEngine.csproj (remove DI/Azure infra-only dependencies, keep minimal library dependencies)
- [X] T002 Verify test project references in tests/Boxcars.GameEngine.Tests/Boxcars.GameEngine.Tests.csproj and enable unit test discovery
- [X] T003 [P] Create root engine observable base in src/Boxcars.GameEngine/ObservableBase.cs
- [X] T004 [P] Create randomness abstractions in src/Boxcars.GameEngine/IRandomProvider.cs and src/Boxcars.GameEngine/DefaultRandomProvider.cs

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core domain primitives required before user stories.

**âš ï¸ CRITICAL**: No user story work should start before this phase completes.

- [X] T005 Create engine enums in src/Boxcars.GameEngine/Domain/Enums.cs
- [X] T006 [P] Create dice/value objects in src/Boxcars.GameEngine/Domain/DiceResult.cs and src/Boxcars.GameEngine/Domain/Route.cs
- [X] T007 [P] Create observable domain entities in src/Boxcars.GameEngine/Domain/Player.cs, src/Boxcars.GameEngine/Domain/Railroad.cs, and src/Boxcars.GameEngine/Domain/Turn.cs
- [X] T008 [P] Create domain event args in src/Boxcars.GameEngine/Events/ (DestinationAssignedEventArgs.cs, DestinationReachedEventArgs.cs, UsageFeeChargedEventArgs.cs, AuctionStartedEventArgs.cs, AuctionCompletedEventArgs.cs, TurnStartedEventArgs.cs, GameOverEventArgs.cs, PlayerBankruptEventArgs.cs, LocomotiveUpgradedEventArgs.cs)
- [X] T009 Create payout lookup in src/Boxcars.GameEngine/PayoutTable.cs
- [X] T010 Create persistence DTO in src/Boxcars.GameEngine/Persistence/GameState.cs
- [X] T011 Create GameEngine shell and constructor validation in src/Boxcars.GameEngine/Domain/GameEngine.cs
- [X] T012 [P] Create deterministic random test double in tests/Boxcars.GameEngine.Tests/TestDoubles/FixedRandomProvider.cs
- [X] T013 [P] Create reusable fixtures in tests/Boxcars.GameEngine.Tests/Fixtures/GameEngineFixture.cs

**Checkpoint**: Foundation complete; user stories can proceed independently.

---

## Phase 3: User Story 1 - Initialize and Inspect a Game (Priority: P1) ðŸŽ¯ MVP

**Goal**: Construct a valid game object graph with players, railroads, and initial turn state.

**Independent Test**: Create `GameEngine` with valid/invalid player lists and assert populated players/railroads/current turn defaults.

### Tests for User Story 1

- [X] T014 [P] [US1] Add constructor validation tests in tests/Boxcars.GameEngine.Tests/Unit/InitializationTests.cs
- [X] T015 [P] [US1] Add initialization defaults tests in tests/Boxcars.GameEngine.Tests/Unit/InitializationTests.cs

### Implementation for User Story 1

- [X] T016 [US1] Implement player list/home city/current city initialization in src/Boxcars.GameEngine/Domain/GameEngine.cs
- [X] T017 [US1] Implement railroad projection from map definition in src/Boxcars.GameEngine/Domain/GameEngine.cs
- [X] T018 [US1] Implement initial turn state and game status transitions in src/Boxcars.GameEngine/Domain/GameEngine.cs

**Checkpoint**: US1 independently functional.

---

## Phase 4: User Story 2 - Observe State Changes in Real Time (Priority: P1)

**Goal**: Ensure all mutable scalar and collection state emits proper notifications synchronously.

**Independent Test**: Subscribe to `PropertyChanged`/`CollectionChanged`, mutate state through actions, verify event names and order.

### Tests for User Story 2

- [X] T019 [P] [US2] Add property notification coverage tests in tests/Boxcars.GameEngine.Tests/Unit/ObservabilityTests.cs
- [X] T020 [P] [US2] Add collection notification tests in tests/Boxcars.GameEngine.Tests/Unit/ObservabilityTests.cs

### Implementation for User Story 2

- [X] T021 [US2] Implement `SetField`-based property change wiring in src/Boxcars.GameEngine/ObservableBase.cs and src/Boxcars.GameEngine/Domain/*.cs
- [X] T022 [US2] Implement owned railroad and player collection change wiring in src/Boxcars.GameEngine/Domain/Player.cs and src/Boxcars.GameEngine/Domain/GameEngine.cs
- [X] T023 [US2] Implement synchronous domain event raising path in src/Boxcars.GameEngine/Domain/GameEngine.cs

**Checkpoint**: US2 independently functional.

---

## Phase 5: User Story 3 - Roll Dice and Move (Priority: P2)

**Goal**: Support turn-phase-safe dice rolling and route movement with fee tracking.

**Independent Test**: Roll in Roll phase, move in Move phase, verify movement/cash/phase updates and invalid-call rejection.

### Tests for User Story 3

- [X] T024 [P] [US3] Add dice roll behavior tests in tests/Boxcars.GameEngine.Tests/Unit/DiceRollTests.cs
- [X] T025 [P] [US3] Add move/non-reuse/phase validation tests in tests/Boxcars.GameEngine.Tests/Unit/MovementTests.cs
- [X] T026 [P] [US3] Add destination draw tests in tests/Boxcars.GameEngine.Tests/Unit/DestinationDrawTests.cs
- [X] T027 [P] [US3] Add turn phase progression tests in tests/Boxcars.GameEngine.Tests/Unit/TurnPhaseTests.cs

### Implementation for User Story 3

- [X] T028 [US3] Implement `DrawDestination()` weighted region/city lookup in src/Boxcars.GameEngine/Domain/GameEngine.cs
- [X] T029 [US3] Implement `RollDice()` with locomotive-specific behavior in src/Boxcars.GameEngine/Domain/GameEngine.cs
- [X] T030 [US3] Implement `MoveAlongRoute(int)` including non-reuse enforcement in src/Boxcars.GameEngine/Domain/GameEngine.cs
- [X] T031 [US3] Implement usage-fee calculation and `UsageFeeCharged` event logic in src/Boxcars.GameEngine/Domain/GameEngine.cs
- [X] T032 [US3] Implement destination arrival payout via src/Boxcars.GameEngine/PayoutTable.cs and src/Boxcars.GameEngine/Domain/GameEngine.cs

**Checkpoint**: US3 independently functional.

---

## Phase 6: User Story 4 - Suggest and Save a Route (Priority: P2)

**Goal**: Provide route recommendation and route persistence on active player.

**Independent Test**: With destination set, `SuggestRoute()` returns valid route and `SaveRoute()` updates `ActiveRoute` with notifications.

### Tests for User Story 4

- [X] T033 [P] [US4] Add route suggestion happy/invalid path tests in tests/Boxcars.GameEngine.Tests/Unit/RouteSuggestionTests.cs
- [X] T034 [P] [US4] Add save route mutation/notification tests in tests/Boxcars.GameEngine.Tests/Unit/RouteSuggestionTests.cs

### Implementation for User Story 4

- [X] T035 [US4] Implement `SuggestRoute()` adapter over map route graph in src/Boxcars.GameEngine/Domain/GameEngine.cs
- [X] T036 [US4] Implement `SaveRoute(Route)` validation and assignment in src/Boxcars.GameEngine/Domain/GameEngine.cs

**Checkpoint**: US4 independently functional.

---

## Phase 7: User Story 5 - Buy or Auction a Railroad (Priority: P3)

**Goal**: Support railroad purchase, auction events, upgrades, and bankruptcy outcomes.

**Independent Test**: Buy unowned railroad with sufficient cash, reject invalid purchases, emit auction events and ownership transfer.

### Tests for User Story 5

- [X] T037 [P] [US5] Add railroad purchase validation/ownership tests in tests/Boxcars.GameEngine.Tests/Unit/RailroadPurchaseTests.cs
- [X] T038 [P] [US5] Add auction event/resolution tests in tests/Boxcars.GameEngine.Tests/Unit/AuctionTests.cs
- [X] T039 [P] [US5] Add locomotive upgrade tests in tests/Boxcars.GameEngine.Tests/Unit/LocomotiveUpgradeTests.cs
- [X] T040 [P] [US5] Add bankruptcy and player elimination tests in tests/Boxcars.GameEngine.Tests/Unit/BankruptcyTests.cs
- [X] T055 [P] [US5] Add declare/rover and undeclare behavior tests in tests/Boxcars.GameEngine.Tests/Unit/WinConditionTests.cs
- [X] T056 [P] [US5] Add establishment fee grandfathering tests in tests/Boxcars.GameEngine.Tests/Unit/UseFeesTests.cs

### Implementation for User Story 5

- [X] T041 [US5] Implement `BuyRailroad(Railroad)` in src/Boxcars.GameEngine/Domain/GameEngine.cs
- [X] T042 [US5] Implement `AuctionRailroad(Railroad)` and `AuctionCompleted` flows in src/Boxcars.GameEngine/Domain/GameEngine.cs
- [X] T043 [US5] Implement `UpgradeLocomotive(LocomotiveType)` in src/Boxcars.GameEngine/Domain/GameEngine.cs
- [X] T044 [US5] Implement bankruptcy elimination and winner-by-last-player logic in src/Boxcars.GameEngine/Domain/GameEngine.cs
- [ ] T057 [US5] Implement declare/rover and undeclare state transitions in src/Boxcars.GameEngine/Domain/GameEngine.cs
- [ ] T058 [US5] Implement establishment fee tracking and application in src/Boxcars.GameEngine/Domain/GameEngine.cs

**Checkpoint**: US5 independently functional.

---

## Phase 8: User Story 6 - Persist and Restore Game State (Priority: P3)

**Goal**: Round-trip all engine state through serializable snapshot DTOs.

**Independent Test**: Serialize after several actions, deserialize into a new engine, verify value parity and continued observability.

### Tests for User Story 6

- [X] T045 [P] [US6] Add snapshot serialization round-trip tests in tests/Boxcars.GameEngine.Tests/Unit/SerializationTests.cs
- [X] T046 [P] [US6] Add restore-and-continue-play tests in tests/Boxcars.GameEngine.Tests/Unit/SerializationTests.cs
- [ ] T059 [P] [US6] Add Azure Table payload compatibility tests (size/shape) in tests/Boxcars.GameEngine.Tests/Unit/SerializationTests.cs

### Implementation for User Story 6

- [X] T047 [US6] Implement `ToSnapshot()` mapping in src/Boxcars.GameEngine/Domain/GameEngine.cs and src/Boxcars.GameEngine/Persistence/GameState.cs
- [X] T048 [US6] Implement `FromSnapshot(...)` reconstruction and reference re-linking in src/Boxcars.GameEngine/Domain/GameEngine.cs
- [X] T049 [US6] Implement serialization safety for route/turn/player internal state in src/Boxcars.GameEngine/Persistence/GameState.cs
- [ ] T060 [US6] Implement Azure Table compatibility guards for serialized payload in src/Boxcars.GameEngine/Persistence/GameState.cs

**Checkpoint**: US6 independently functional.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Final alignment, docs, and full verification.

- [X] T050 [P] Add win-condition edge tests in tests/Boxcars.GameEngine.Tests/Unit/WinConditionTests.cs
- [X] T051 [P] Add use-fee matrix and opponent/bank scenarios in tests/Boxcars.GameEngine.Tests/Unit/UseFeesTests.cs
- [X] T052 [P] Add payout lookup coverage in tests/Boxcars.GameEngine.Tests/Unit/PayoutTests.cs
- [X] T053 Update usage examples to final API in specs/004-observable-game-model/quickstart.md
- [X] T054 Run full solution build/test and record verification notes in specs/004-observable-game-model/quickstart.md

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: starts immediately
- **Phase 2 (Foundational)**: depends on Phase 1 and blocks all user stories
- **Phases 3â€“8 (User Stories)**: depend on Phase 2 completion
- **Phase 9 (Polish)**: depends on completion of selected user stories

### User Story Dependencies

- **US1 (P1)**: starts after Phase 2; no dependency on other stories
- **US2 (P1)**: starts after Phase 2; uses US1 entities but remains independently testable
- **US3 (P2)**: starts after Phase 2; can run parallel with US4 once core entities exist
- **US4 (P2)**: starts after Phase 2; independent route planning slice
- **US5 (P3)**: starts after Phase 2; depends on core turn/movement state but independently testable
- **US6 (P3)**: starts after Phase 2; snapshot concerns independent of UI behavior

### Suggested Story Completion Order

US1 â†’ US2 â†’ US3 â†’ US4 â†’ US5 â†’ US6

---

## Parallel Execution Examples

### US1

- T014 and T015 can run in parallel (same story tests, independent assertions)

### US2

- T019 and T020 can run in parallel

### US3

- T024, T025, T026, T027 can run in parallel

### US4

- T033 and T034 can run in parallel

### US5

- T037, T038, T039, T040 can run in parallel

### US6

- T045 and T046 can run in parallel

---

## Implementation Strategy

### MVP First (US1 only)

1. Complete Phase 1 and Phase 2
2. Complete Phase 3 (US1)
3. Validate with `InitializationTests`
4. Demo engine construction + object graph inspection

### Incremental Delivery

1. Deliver US1 + US2 (observable core)
2. Add US3 + US4 (movement and routing)
3. Add US5 (economy/actions)
4. Add US6 (persistence/restore)
5. Finish Phase 9 full regression and docs
