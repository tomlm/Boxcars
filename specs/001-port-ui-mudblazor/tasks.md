# Tasks: UI Component Library Migration

**Input**: Design documents from `/specs/001-port-ui-mudblazor/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/, quickstart.md

**Tests**: Automated tests are not explicitly requested in the feature specification; this task list uses build and manual acceptance validation tasks.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Phase 1: Setup (Shared Preparation)

**Purpose**: Prepare migration execution artifacts and implementation guardrails.

- [x] T001 Create UI surface inventory in specs/001-port-ui-mudblazor/ui-surface-inventory.md
- [x] T002 [P] Create control mapping matrix in specs/001-port-ui-mudblazor/control-mapping-matrix.md
- [x] T003 [P] Create responsive verification checklist in specs/001-port-ui-mudblazor/responsive-checklist.md
- [x] T004 Record migration evidence template in specs/001-port-ui-mudblazor/migration-evidence.md

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core runtime and composition changes that MUST be complete before user-story migration.

**⚠️ CRITICAL**: No user story implementation should start until this phase completes.

- [ ] T005 Replace legacy UI package references with MudBlazor in src/Boxcars/Boxcars.csproj
- [x] T006 Update UI service registration to MudBlazor in src/Boxcars/Program.cs
- [x] T007 Replace root stylesheet/theme/provider composition in src/Boxcars/Components/App.razor
- [x] T008 Update global component imports for MudBlazor in src/Boxcars/Components/_Imports.razor
- [x] T009 Remove Fluent-dependent type usage from src/Boxcars/Data/PlayerBoardModel.cs
- [x] T010 [P] Prepare shared shell migration scaffolding in src/Boxcars/Components/Layout/MainLayout.razor
- [x] T011 [P] Prepare shared shell styles for MudBlazor layout primitives in src/Boxcars/Components/Layout/MainLayout.razor.css
- [x] T012 Verify foundational compile health and record build output summary in specs/001-port-ui-mudblazor/migration-evidence.md

**Checkpoint**: Dependency/runtime foundation is complete; user-story migration can begin.

---

## Phase 3: User Story 1 - Use the full app after UI migration (Priority: P1) 🎯 MVP

**Goal**: Deliver fully functional primary user journeys with no legacy control usage in migrated surfaces.

**Independent Test**: Run app and complete landing, sign-in, dashboard, and game-board flows with behavior parity.

### Implementation for User Story 1

- [x] T013 [US1] Migrate top-level route host composition to MudBlazor-compatible layout usage in src/Boxcars/Components/Routes.razor
- [x] T014 [US1] Migrate user menu/profile interactions to MudBlazor components in src/Boxcars/Components/Layout/UserMenu.razor
- [x] T015 [US1] Migrate landing page controls and layout in src/Boxcars/Components/Pages/Home.razor
- [x] T016 [US1] Migrate dashboard controls/dialog/actions in src/Boxcars/Components/Pages/Dashboard.razor
- [x] T017 [US1] Migrate profile settings controls and actions in src/Boxcars/Components/Pages/ProfileSettings.razor
- [x] T018 [US1] Migrate game board page controls and action toolbar in src/Boxcars/Components/Pages/GameBoard.razor
- [x] T019 [P] [US1] Migrate map board control surface to MudBlazor in src/Boxcars/Components/Map/MapComponent.razor
- [x] T020 [P] [US1] Migrate player board cards/persona display to MudBlazor in src/Boxcars/Components/Map/PlayerBoard.razor
- [x] T021 [P] [US1] Align game map wrapper composition with migrated controls in src/Boxcars/Components/Map/GameMapComponent.razor
- [x] T022 [US1] Migrate account shell wrappers used by sign-in flow in src/Boxcars/Components/Account/Shared/AccountLayout.razor
- [x] T023 [P] [US1] Migrate account manage shell wrappers in src/Boxcars/Components/Account/Shared/ManageLayout.razor
- [x] T024 [P] [US1] Migrate account navigation component controls in src/Boxcars/Components/Account/Shared/ManageNavMenu.razor
- [x] T025 [US1] Migrate primary auth entry forms to MudBlazor in src/Boxcars/Components/Account/Pages/Login.razor
- [x] T026 [P] [US1] Migrate account registration flow form in src/Boxcars/Components/Account/Pages/Register.razor
- [x] T027 [P] [US1] Migrate account password reset flow form in src/Boxcars/Components/Account/Pages/ResetPassword.razor
- [x] T028 [US1] Validate legacy removal for migrated surfaces in specs/001-port-ui-mudblazor/migration-evidence.md
- [ ] T029 [US1] Validate primary journey parity (landing/sign-in/dashboard/game-board) in specs/001-port-ui-mudblazor/migration-evidence.md

**Checkpoint**: Primary journeys are functional and independently testable on migrated surfaces.

---

## Phase 4: User Story 2 - Operate with minimal custom styling (Priority: P2)

**Goal**: Minimize custom CSS and raw HTML by using MudBlazor layout/theming primitives.

**Independent Test**: Review migrated surfaces and confirm major sections are component-driven with reduced custom CSS.

### Implementation for User Story 2

- [x] T030 [US2] Replace inline style-heavy dashboard patterns with MudBlazor layout props in src/Boxcars/Components/Pages/Dashboard.razor
- [x] T031 [US2] Replace inline style-heavy profile settings patterns with MudBlazor layout props in src/Boxcars/Components/Pages/ProfileSettings.razor
- [x] T032 [US2] Refactor game board styling to rely on component/layout primitives in src/Boxcars/Components/Pages/GameBoard.razor.css
- [x] T033 [P] [US2] Refactor map control styling to reduce bespoke CSS in src/Boxcars/Components/Map/MapComponent.razor.css
- [x] T034 [P] [US2] Refactor player board styling to reduce bespoke CSS in src/Boxcars/Components/Map/PlayerBoard.razor.css
- [x] T035 [US2] Refactor layout-level CSS to remove legacy layout assumptions in src/Boxcars/Components/Layout/MainLayout.razor.css
- [x] T036 [US2] Document remaining non-replaceable custom CSS rationale in specs/001-port-ui-mudblazor/migration-evidence.md

**Checkpoint**: Styling is largely component/theme-driven and custom CSS is minimized with rationale for remainder.

---

## Phase 5: User Story 3 - Preserve responsive usability across devices (Priority: P3)

**Goal**: Maintain usable, readable interactions on mobile and desktop after migration.

**Independent Test**: Verify responsive behavior at one mobile and one desktop viewport for primary pages.

### Implementation for User Story 3

- [ ] T037 [US3] Implement responsive MudBlazor layout behavior for shell/navigation in src/Boxcars/Components/Layout/MainLayout.razor
- [ ] T038 [US3] Implement responsive behavior for dashboard sections and actions in src/Boxcars/Components/Pages/Dashboard.razor
- [ ] T039 [US3] Implement responsive behavior for game board controls/history panel in src/Boxcars/Components/Pages/GameBoard.razor
- [ ] T040 [P] [US3] Implement responsive behavior for map surface controls in src/Boxcars/Components/Map/MapComponent.razor
- [ ] T041 [P] [US3] Implement responsive behavior for player board cards in src/Boxcars/Components/Map/PlayerBoard.razor
- [ ] T042 [US3] Record mobile/desktop verification results in specs/001-port-ui-mudblazor/responsive-checklist.md

**Checkpoint**: Responsive usability passes for primary pages on mobile and desktop viewports.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final hardening and acceptance validation across all stories.

- [ ] T043 Run final legacy reference scan and record zero-legacy evidence in specs/001-port-ui-mudblazor/migration-evidence.md
- [ ] T044 Run full build verification and record result in specs/001-port-ui-mudblazor/migration-evidence.md
- [ ] T045 Execute quickstart end-to-end validation and capture outcomes in specs/001-port-ui-mudblazor/migration-evidence.md
- [ ] T046 Update implementation notes and completion summary in specs/001-port-ui-mudblazor/quickstart.md

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup completion; blocks all user stories.
- **User Story Phases (Phase 3-5)**: Depend on Foundational completion.
- **Polish (Phase 6)**: Depends on all desired user stories being complete.

### User Story Dependencies

- **US1 (P1)**: Starts immediately after Phase 2 and defines MVP.
- **US2 (P2)**: Starts after Phase 2; preferred after US1 core surfaces are migrated.
- **US3 (P3)**: Starts after Phase 2; preferred after US1 migration and US2 style reductions.

### Within Each User Story

- Shared layout/components first, then page-level surfaces, then validation evidence.
- Tasks marked `[P]` may run in parallel when they touch different files.

### Story Completion Order

US1 → US2 → US3

---

## Parallel Execution Examples

### User Story 1

- Run in parallel after T018:
  - T019 (`src/Boxcars/Components/Map/MapComponent.razor`)
  - T020 (`src/Boxcars/Components/Map/PlayerBoard.razor`)
  - T021 (`src/Boxcars/Components/Map/GameMapComponent.razor`)

- Run in parallel after T022:
  - T023 (`src/Boxcars/Components/Account/Shared/ManageLayout.razor`)
  - T024 (`src/Boxcars/Components/Account/Shared/ManageNavMenu.razor`)
  - T026 (`src/Boxcars/Components/Account/Pages/Register.razor`)
  - T027 (`src/Boxcars/Components/Account/Pages/ResetPassword.razor`)

### User Story 2

- Run in parallel:
  - T033 (`src/Boxcars/Components/Map/MapComponent.razor.css`)
  - T034 (`src/Boxcars/Components/Map/PlayerBoard.razor.css`)

### User Story 3

- Run in parallel:
  - T040 (`src/Boxcars/Components/Map/MapComponent.razor`)
  - T041 (`src/Boxcars/Components/Map/PlayerBoard.razor`)

---

## Implementation Strategy

### MVP First (US1)

1. Complete Phase 1 and Phase 2.
2. Complete Phase 3 (US1).
3. Validate primary user journeys and legacy removal evidence.
4. Demo/deploy MVP if acceptance passes.

### Incremental Delivery

1. Add US2 to reduce custom styling and improve maintainability.
2. Add US3 responsive hardening and viewport validation.
3. Finish with Polish phase for final acceptance evidence.

### Team Parallelization

1. One developer handles foundational runtime composition (T005-T012).
2. After foundation:
   - Dev A: Shell and page migration tasks (US1)
   - Dev B: Map/player board migration tasks (US1/US3)
   - Dev C: CSS minimization and evidence tracking (US2 + docs)

---

## Notes

- `[P]` tasks operate on different files and can be executed concurrently.
- All user story tasks include `[US#]` labels for traceability.
- This task list intentionally emphasizes behavior parity and acceptance evidence per `contracts/ui-migration-contract.md`.
