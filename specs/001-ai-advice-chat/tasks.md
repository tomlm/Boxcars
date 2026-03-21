# Tasks: AI Advice Chat

**Input**: Design documents from `S:\github\Boxcars\specs\001-ai-advice-chat\`
**Prerequisites**: `plan.md` (required), `spec.md` (required for user stories), `research.md`, `data-model.md`, `contracts/`

**Tests**: No dedicated test-first tasks are included because the feature spec does not explicitly require TDD. Validation is covered through targeted implementation verification and quickstart/build execution.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the advisor feature files and shared state types used by later stories.

- [X] T001 Create advisor session/state models in `src/Boxcars/Data/AdvisorEntryPointState.cs`, `src/Boxcars/Data/AdvisorConversationSession.cs`, and `src/Boxcars/Data/AdvisorMessage.cs`
- [X] T002 [P] Create advisor context/response models in `src/Boxcars/Data/AdvisorContextSnapshot.cs` and `src/Boxcars/Data/AdvisorResponse.cs`
- [X] T003 [P] Create advisor UI component shells in `src/Boxcars/Components/GameBoard/AdvisorSidebar.razor` and `src/Boxcars/Components/GameBoard/AdvisorSidebar.razor.css`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add the shared advisory plumbing required before any user story can be completed.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T004 Extend freeform OpenAI advisory response handling in `src/Boxcars/Services/OpenAiBotClient.cs`
- [X] T005 [P] Register advisory orchestration in `src/Boxcars/Program.cs`
- [X] T006 Implement authoritative advisor context assembly and request orchestration in `src/Boxcars/Services/GameBoardAdviceService.cs`

**Checkpoint**: Foundation ready; the board can now host advisor UI and request server-generated advisory replies.

---

## Phase 3: User Story 1 - Open The Advisor From The Board (Priority: P1) 🎯 MVP

**Goal**: Let players open and close a lower-right advisor sidebar from the live game board and see the fixed greeting immediately.

**Independent Test**: Open a game board, click the lower-right advisor icon, confirm the sidebar appears with `How can I help?`, then close it without disrupting the board.

### Implementation for User Story 1

- [X] T007 [P] [US1] Add advisor open/close state and lower-right entry point wiring in `src/Boxcars/Components/Pages/GameBoard.razor`
- [X] T008 [P] [US1] Implement advisor sidebar layout, greeting transcript, and close behavior in `src/Boxcars/Components/GameBoard/AdvisorSidebar.razor`
- [X] T009 [US1] Add responsive lower-right launcher and sidebar styling in `src/Boxcars/Components/Pages/GameBoard.razor.css` and `src/Boxcars/Components/GameBoard/AdvisorSidebar.razor.css`
- [X] T010 [US1] Seed the first-open greeting and preserve session-scoped conversation state in `src/Boxcars/Components/Pages/GameBoard.razor` and `src/Boxcars/Data/AdvisorConversationSession.cs`

**Checkpoint**: User Story 1 should now provide a discoverable advisor icon and a non-destructive sidebar chat shell with the required greeting.

---

## Phase 4: User Story 2 - Ask Board-Aware Questions (Priority: P2)

**Goal**: Let players ask freeform questions and receive advisory answers grounded in the latest authoritative board state.

**Independent Test**: Ask a question about the current board, change the game state, ask again, and verify the second answer reflects the newer authoritative state.

### Implementation for User Story 2

- [X] T011 [P] [US2] Implement chat transcript rendering, composer submission, and retry UX in `src/Boxcars/Components/GameBoard/AdvisorSidebar.razor`
- [X] T012 [US2] Refresh authoritative game and player context per submitted question in `src/Boxcars/Services/GameBoardAdviceService.cs`
- [X] T013 [US2] Wire sidebar message submission and async advisory responses in `src/Boxcars/Components/Pages/GameBoard.razor` and `src/Boxcars/Components/GameBoard/AdvisorSidebar.razor`
- [X] T014 [US2] Surface advisory failures, loading states, and non-authoritative copy in `src/Boxcars/Components/GameBoard/AdvisorSidebar.razor` and `src/Boxcars/Services/GameBoardAdviceService.cs`

**Checkpoint**: User Story 2 should now support live board-aware Q&A with refreshed authoritative context on every send.

---

## Phase 5: User Story 3 - Receive Strategy-Tailored Guidance (Priority: P3)

**Goal**: Tailor answers to the controlled player's resources, phase, and strategic situation rather than returning generic board commentary.

**Independent Test**: Compare advice across different controlled seats or materially different cash/engine/railroad states and verify the responses shift with player strategy context.

### Implementation for User Story 3

- [X] T015 [P] [US3] Enrich advisor context with controlled-seat, engine, destination, fee-pressure, and railroad summaries in `src/Boxcars/Services/GameBoardAdviceService.cs`
- [X] T016 [P] [US3] Add strategy-oriented prompt construction and recent-conversation inclusion in `src/Boxcars/Services/GameBoardAdviceService.cs`
- [X] T017 [US3] Rebind advisor context when delegated or controlled seat context changes in `src/Boxcars/Components/Pages/GameBoard.razor` and `src/Boxcars/Services/GameBoardAdviceService.cs`

**Checkpoint**: All three user stories should now be independently functional, with answers tailored to the relevant player's strategy context.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final validation and cross-story refinement.

- [X] T018 [P] Review advisor copy and failure messaging across `src/Boxcars/Components/GameBoard/AdvisorSidebar.razor` and `src/Boxcars/Services/GameBoardAdviceService.cs`
- [ ] T019 Run advisor quickstart validation from `specs/001-ai-advice-chat/quickstart.md`
- [X] T020 Run solution validation with `dotnet build Boxcars.slnx -p:UseAppHost=false`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies; can start immediately.
- **Foundational (Phase 2)**: Depends on Setup completion; blocks all user stories.
- **User Stories (Phases 3-5)**: Depend on Foundational completion.
- **Polish (Phase 6)**: Depends on all desired user stories being complete.

### User Story Dependencies

- **User Story 1 (P1)**: Starts after Foundational completion; no dependency on other stories.
- **User Story 2 (P2)**: Starts after Foundational completion and benefits from the US1 sidebar shell but remains independently testable once the sidebar exists.
- **User Story 3 (P3)**: Starts after Foundational completion and builds on the US2 advisory request flow.

### Within Each User Story

- Shared state and service scaffolding before board wiring.
- Board host wiring before final UI polish.
- Advisory request flow before strategy-tailoring refinements.
- Complete each story checkpoint before moving to lower-priority work.

### Parallel Opportunities

- `T002` and `T003` can run in parallel after `T001`.
- `T005` can run in parallel with `T004` before `T006`.
- `T007` and `T008` can run in parallel for US1 before `T009` and `T010`.
- `T011` can run in parallel with `T012` for US2 before `T013`.
- `T015` and `T016` can run in parallel for US3 before `T017`.
- `T018` and `T019` can run in parallel before the final build validation task.

---

## Parallel Example: User Story 1

```text
Task: "Add advisor open/close state and lower-right entry point wiring in src/Boxcars/Components/Pages/GameBoard.razor"
Task: "Implement advisor sidebar layout, greeting transcript, and close behavior in src/Boxcars/Components/GameBoard/AdvisorSidebar.razor"
```

## Parallel Example: User Story 2

```text
Task: "Implement chat transcript rendering, composer submission, and retry UX in src/Boxcars/Components/GameBoard/AdvisorSidebar.razor"
Task: "Refresh authoritative game and player context per submitted question in src/Boxcars/Services/GameBoardAdviceService.cs"
```

## Parallel Example: User Story 3

```text
Task: "Enrich advisor context with controlled-seat, engine, destination, fee-pressure, and railroad summaries in src/Boxcars/Services/GameBoardAdviceService.cs"
Task: "Add strategy-oriented prompt construction and recent-conversation inclusion in src/Boxcars/Services/GameBoardAdviceService.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational.
3. Complete Phase 3: User Story 1.
4. Validate the sidebar entry point, greeting, and close behavior on the live board.

### Incremental Delivery

1. Deliver US1 to establish the lower-right advisor experience.
2. Add US2 to enable authoritative board-aware question/answer flow.
3. Add US3 to improve relevance with controlled-seat strategy context.
4. Finish with quickstart and build validation.

### Suggested MVP Scope

Implement through **User Story 1** for the first shippable increment, then layer live advisory responses and strategy tailoring in subsequent increments.

---

## Notes

- `[P]` tasks indicate parallelizable work across different files or isolated concerns.
- User story labels map every implementation task back to a single independently testable story.
- The task list preserves the feature decision to keep advice advisory-only, session-scoped, and derived from authoritative state at send time.