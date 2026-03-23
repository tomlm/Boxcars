# Tasks: Game Creation Settings

**Input**: Design documents from `/specs/001-game-settings/`
**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/`, `quickstart.md`

**Tests**: Tests are included because this feature changes configurable rule values, authoritative gameplay logic, and derived advisory outputs.

**Organization**: Tasks are grouped by user story so each story can be implemented and validated independently.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Introduce the shared runtime settings types and reusable test helpers that the rest of the feature builds on.

- [X] T001 Create the shared immutable runtime settings type and defaults in `src/Boxcars.Engine/Persistence/GameSettings.cs`
- [X] T002 [P] Create reusable settings test data helpers in `tests/Boxcars.Engine.Tests/GameSettingsTestData.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Establish the persisted storage shape and runtime resolution path that all user stories depend on.

**⚠️ CRITICAL**: No user story work should begin until this phase is complete.

- [X] T003 Extend direct persisted game setting columns on `src/Boxcars/Data/GameEntity.cs`
- [X] T004 [P] Extend the create-game request contract to carry typed settings input in `src/Boxcars/Data/GameCreationModels.cs`
- [X] T005 Implement direct-column-to-runtime-settings fallback resolution in `src/Boxcars/Services/GameSettingsResolver.cs`
- [X] T006 [P] Register runtime settings resolution and remove the obsolete purchase-rule DI wiring in `src/Boxcars/Program.cs` and `src/Boxcars/Services/PurchaseRulesOptions.cs`
- [X] T007 Update authoritative game creation and load seams to persist and read direct setting columns in `src/Boxcars/GameEngine/GameEngineService.cs`
- [X] T008 [P] Update engine and player constructors to accept runtime settings in `src/Boxcars.Engine/Domain/GameEngine.cs` and `src/Boxcars.Engine/Domain/Player.cs`

**Checkpoint**: Foundation ready. User stories can now proceed.

---

## Phase 3: User Story 1 - Configure a game before play begins (Priority: P1) 🎯 MVP

**Goal**: Let the game creator review defaults, change settings during create-game, and persist those values on the owning game row.

**Independent Test**: Create a new game with default settings and another with customized settings, then verify the stored `GameEntity` columns match the submitted values.

### Tests for User Story 1

- [X] T009 [P] [US1] Add create-game validation tests for typed settings input in `tests/Boxcars.Engine.Tests/GameServiceCreateGameValidationTests.cs`
- [X] T010 [P] [US1] Add game creation persistence tests for direct `GameEntity` setting columns in `tests/Boxcars.Engine.Tests/GameEngineServiceCreateGameSettingsTests.cs`

### Implementation for User Story 1

- [X] T011 [US1] Add settings inputs, defaults, and validation messages to `src/Boxcars/Components/Pages/CreateGame.razor`
- [X] T012 [US1] Validate and normalize create-game settings in `src/Boxcars/Services/GameService.cs`
- [X] T013 [US1] Map create-game settings request values onto direct `GameEntity` columns in `src/Boxcars/GameEngine/GameEngineService.cs`

**Checkpoint**: A game creator can choose settings during game creation and those settings are durably stored per game.

---

## Phase 4: User Story 2 - Lock rule values once gameplay starts (Priority: P2)

**Goal**: Show persisted settings as read-only game information and ensure they cannot be changed once the game has started.

**Independent Test**: Load a game after creation and after gameplay begins, verify the settings are displayed read-only, and confirm that post-start flows do not mutate persisted settings.

### Tests for User Story 2

- [X] T014 [P] [US2] Add read-only settings summary mapping tests for started games in `tests/Boxcars.Engine.Tests/GameBoardSettingsSummaryTests.cs`
- [X] T015 [P] [US2] Add immutability regression tests for post-start update flows in `tests/Boxcars.Engine.Tests/GameSettingsImmutabilityTests.cs`

### Implementation for User Story 2

- [X] T016 [US2] Add the immutable settings summary view model in `src/Boxcars/Data/GameSettingsSummaryModel.cs`
- [X] T017 [US2] Create the read-only settings panel component in `src/Boxcars/Components/GameBoard/GameSettingsPanel.razor`
- [X] T018 [US2] Load and render read-only settings on the game board in `src/Boxcars/Components/Pages/GameBoard.razor` and `src/Boxcars/Services/GameBoardStateMapper.cs`
- [X] T019 [US2] Guard game update paths from mutating persisted settings after creation in `src/Boxcars/Services/GameService.cs` and `src/Boxcars/GameEngine/GameEngineService.cs`

**Checkpoint**: Persisted settings are visible as immutable game metadata and cannot change after game start.

---

## Phase 5: User Story 3 - Have gameplay honor the saved settings (Priority: P3)

**Goal**: Replace hard-coded rule values with per-game settings across engine logic, fee resolution, visibility rules, home selection, and advisory projections.

**Independent Test**: Start games with non-default values and verify opening cash, declaration/win thresholds, rover awards, fee calculations, cash visibility, home-selection behavior, and engine pricing all follow the saved per-game settings.

### Tests for User Story 3

- [X] T020 [P] [US3] Add engine settings tests for starting cash, starting engine, and upgrade pricing in `tests/Boxcars.Engine.Tests/GameEngineSettingsStartupTests.cs`
- [X] T021 [P] [US3] Add authoritative fee-resolution tests for public, private, and unfriendly fee settings in `tests/Boxcars.Engine.Tests/GameEngineSettingsFeeTests.cs`
- [X] T022 [P] [US3] Add cash-visibility and board-projection tests for announcing and secret-cash behavior in `tests/Boxcars.Engine.Tests/GameSettingsVisibilityProjectionTests.cs`
- [X] T023 [P] [US3] Add authoritative home-selection tests for home-city choice, city uniqueness, and swap eligibility in `tests/Boxcars.Engine.Tests/GameSettingsHomeSelectionTests.cs`
- [X] T023A [P] [US3] Add rover-cash threshold regression and legacy-default fallback tests in `tests/Boxcars.Engine.Tests/GameEngineSettingsThresholdTests.cs`
- [X] T023B [P] [US3] Add game-board home setup flow tests for home-city choice and home swapping in `tests/Boxcars.Engine.Tests/GameSettingsHomeSelectionFlowTests.cs`

### Implementation for User Story 3

- [X] T024 [US3] Apply starting cash, starting engine, and engine upgrade prices in `src/Boxcars.Engine/Domain/GameEngine.cs` and `src/Boxcars.Engine/Domain/Player.cs`
- [X] T025 [US3] Apply winning, announcing, and rover cash thresholds in `src/Boxcars.Engine/Domain/GameEngine.cs`
- [X] T026 [US3] Apply public, private, and unfriendly fee settings in `src/Boxcars.Engine/Domain/GameEngine.cs`
- [X] T027 [US3] Apply home-city choice and home-swapping rule flow in `src/Boxcars.Engine/Domain/GameEngine.cs` and `src/Boxcars.Engine/Persistence/GameState.cs`
- [X] T027A [US3] Surface home-selection rule state through board mapping in `src/Boxcars/Services/GameBoardStateMapper.cs`
- [X] T027B [US3] Add player-facing home-city choice and home-swapping flows to `src/Boxcars/Components/Pages/GameBoard.razor` and `src/Boxcars/Services/GameService.cs`
- [X] T028 [US3] Replace hard-coded cash visibility and engine-price projections in `src/Boxcars/Data/PlayerBoardModel.cs` and `src/Boxcars/Services/GameBoardStateMapper.cs`
- [X] T029 [US3] Update advisory and automated purchase projections to use resolved per-game settings in `src/Boxcars/Services/GameBoardAdviceService.cs` and `src/Boxcars/Services/BotTurnService.cs`

**Checkpoint**: Gameplay and projections now use persisted per-game settings end-to-end.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Clean up remaining global-rule remnants and validate the documented feature flow.

- [X] T030 [P] Remove stale app-wide purchase-rule comments, obsolete docs, and any dead cleanup left after migration in `src/Boxcars/Program.cs` and `src/Boxcars/Services/PurchaseRulesOptions.cs`
- [ ] T031 Run quickstart validation and targeted settings regressions from `specs/001-game-settings/quickstart.md` against `tests/Boxcars.Engine.Tests/Boxcars.Engine.Tests.csproj`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup completion and blocks all user stories.
- **User Story 1 (Phase 3)**: Depends on Foundational completion.
- **User Story 2 (Phase 4)**: Depends on Foundational completion and uses the persisted settings established by User Story 1.
- **User Story 3 (Phase 5)**: Depends on Foundational completion and uses the same resolved runtime settings introduced for User Story 1.
- **Polish (Phase 6)**: Depends on the user stories you intend to ship.

### User Story Dependencies

- **US1 (P1)**: No dependency on other user stories; this is the MVP.
- **US2 (P2)**: Can be validated independently after Foundational by loading seeded games, but it integrates best once US1 persistence is in place.
- **US3 (P3)**: Can be developed after Foundational using seeded or created games; it does not require US2 UI work to validate engine behavior.

### Within Each User Story

- Write tests first and ensure they fail before implementation.
- Complete request/model updates before service orchestration.
- Complete service orchestration before UI wiring.
- Complete authoritative engine changes before advisory/UI projection updates.

### Parallel Opportunities

- `T002`, `T004`, `T006`, and `T008` can run in parallel after `T001`/`T003` establish the core types and entity columns.
- US1 tests `T009` and `T010` can run in parallel.
- US2 tests `T014` and `T015` can run in parallel.
- US3 tests `T020` through `T023B` can run in parallel.

---

## Parallel Example: User Story 1

```text
Task: T009 Add create-game validation tests for typed settings input in tests/Boxcars.Engine.Tests/GameServiceCreateGameValidationTests.cs
Task: T010 Add game creation persistence tests for direct GameEntity setting columns in tests/Boxcars.Engine.Tests/GameEngineServiceCreateGameSettingsTests.cs
```

## Parallel Example: User Story 2

```text
Task: T014 Add read-only settings summary mapping tests for started games in tests/Boxcars.Engine.Tests/GameBoardSettingsSummaryTests.cs
Task: T015 Add immutability regression tests for post-start update flows in tests/Boxcars.Engine.Tests/GameSettingsImmutabilityTests.cs
```

## Parallel Example: User Story 3

```text
Task: T020 Add engine settings tests for starting cash, starting engine, and upgrade pricing in tests/Boxcars.Engine.Tests/GameEngineSettingsStartupTests.cs
Task: T021 Add authoritative fee-resolution tests for public, private, and unfriendly fee settings in tests/Boxcars.Engine.Tests/GameEngineSettingsFeeTests.cs
Task: T022 Add cash-visibility and board-projection tests for announcing and secret-cash behavior in tests/Boxcars.Engine.Tests/GameSettingsVisibilityProjectionTests.cs
Task: T023 Add authoritative home-selection tests for home-city choice, city uniqueness, and swap eligibility in tests/Boxcars.Engine.Tests/GameSettingsHomeSelectionTests.cs
Task: T023A Add rover-cash threshold regression and legacy-default fallback tests in tests/Boxcars.Engine.Tests/GameEngineSettingsThresholdTests.cs
Task: T023B Add game-board home setup flow tests for home-city choice and home swapping in tests/Boxcars.Engine.Tests/GameSettingsHomeSelectionFlowTests.cs
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational.
3. Complete Phase 3: User Story 1.
4. Validate that new games persist their selected settings correctly.

### Incremental Delivery

1. Ship US1 to enable settings capture and persistence.
2. Add US2 to make those settings visible and read-only after start.
3. Add US3 to replace hard-coded gameplay and projection logic with per-game settings.

### Parallel Team Strategy

1. One developer completes Setup and Foundational.
2. After Foundational, one developer can handle US2 UI/read-only presentation while another handles US3 engine and projection logic.
3. Merge on the shared runtime settings resolver and regression tests.

---

## Notes

- `[P]` tasks operate on different files or isolated test files and can be worked in parallel.
- User story labels map every implementation task back to a testable story slice.
- The MVP is complete at the end of Phase 3.
- The feature is not done until authoritative gameplay and derived UI/advice outputs both use the same resolved per-game settings.