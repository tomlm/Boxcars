# Full Requirements Review Checklist: BoxCars Shell Application Pages

**Purpose**: Comprehensive validation of requirements quality across all domains — completeness, clarity, consistency, measurability, and coverage for the shell application pages feature.  
**Created**: 2026-02-27  
**Feature**: `/specs/002-shell-app-pages/spec.md`  
**Focus**: Full requirements review (UX, data/storage, security/auth, real-time, integration)  
**Depth**: Standard  
**Audience**: Reviewer (PR / pre-implementation gate)

## Requirement Completeness

- [ ] CHK001 - Are requirements defined for the landing page's visual content and messaging beyond "clear sign-in action"? [Completeness, Spec §FR-001, FR-002]
- [ ] CHK002 - Is the dashboard page route (`/dashboard`) explicitly specified, or only implied by redirection requirements? [Completeness, Gap]
- [ ] CHK003 - Are requirements defined for the placeholder game page route (e.g., `/game/{id}`)? [Completeness, Spec §FR-016]
- [ ] CHK004 - Are requirements defined for the profile settings page route (e.g., `/settings`)? [Completeness, Spec §FR-021]
- [ ] CHK005 - Is the default thumbnail placeholder URL specified or delegated to implementation? [Completeness, Spec §FR-009]
- [ ] CHK006 - Are requirements for the `FluentProfileMenu` content items explicitly listed (Settings, Sign Out, and any others like "View Account")? [Completeness, Spec §FR-020]
- [ ] CHK007 - Are requirements for creating tables on first startup defined (auto-creation behavior, error handling if creation fails)? [Completeness, Gap]
- [ ] CHK008 - Are requirements for the game creation UI defined (how does the player select 2–6 max players)? [Completeness, Spec §FR-017]
- [ ] CHK009 - Is the "first-game welcome message" content or structure specified, or only referenced as a requirement? [Completeness, Spec §FR-014]
- [x] CHK010 - Are requirements defined for what the dashboard displays when the player already has an active game (e.g., "Resume Game" vs "Join Game")? [Completeness, Gap] — RESOLVED: "Resume Game" button; Join/Create hidden.
- [ ] CHK011 - Are the Identity scaffold pages that need modification (Register, Login) explicitly listed with their required changes? [Completeness, Spec §FR-004, FR-009]

## Requirement Clarity

- [ ] CHK012 - Is "clear sign-in action" (FR-002) quantified with specific UI behavior — a button, a link, or a redirect? [Clarity, Spec §FR-002]
- [ ] CHK013 - Is "clear, non-technical message" (FR-005) defined with example wording or content guidelines? [Clarity, Spec §FR-005]
- [ ] CHK014 - Is "sensible defaults" (FR-009) fully enumerated — email prefix for nickname and what specific default thumbnail? [Clarity, Spec §FR-009]
- [ ] CHK015 - Is "immediately reflected" (FR-022) defined — does it mean without page reload, within the same Blazor circuit, or across all open tabs? [Clarity, Spec §FR-022]
- [ ] CHK016 - Is "clear conflict message" (FR-018) defined with specific messaging or structure? [Clarity, Spec §FR-018]
- [ ] CHK017 - Is "friendly error state with a retry action" (edge case) specified with UI behavior (inline message, toast, full-page error)? [Clarity, Edge Cases]
- [ ] CHK018 - Is "configurable player count" (FR-017) defined in terms of UI — dropdown, number input, radio buttons? [Clarity, Spec §FR-017]
- [ ] CHK019 - Is the scope of "game-state change" that triggers `DashboardStateRefreshed` exhaustively defined? The clarification lists "created, joined, filled, or closed" — does "left" or "cancelled" also trigger it? [Clarity, Spec §FR-015]
- [ ] CHK020 - Is "preserve in-game state for reconnection" (edge case: sign-out during game) specified with expected preservation scope and duration? [Clarity, Edge Cases]

## Requirement Consistency

- [ ] CHK021 - Are the profile fields consistent between spec (FR-007, FR-008: nickname + thumbnail) and data model (`UsersTable`: Nickname, NormalizedNickname, ThumbnailUrl)? The spec mentions no `NormalizedNickname` — is this a plan-level addition or a spec gap? [Consistency, Spec §FR-007 vs data-model.md]
- [ ] CHK022 - Is the "Join Game" action consistent between the dashboard (FR-015: "active game available") and the data model (PlayerActiveGameIndexTable checks "player has active game")? The dashboard shows "Join" for available games, but the index tracks the player's own active game — are both directions covered? [Consistency, Spec §FR-015 vs data-model.md]
- [ ] CHK023 - Are the error/conflict display patterns consistent — FR-018 says "clear conflict message," edge cases reference "friendly error state with retry," and the contract uses `JoinConflict` event. Are these the same UI or different? [Consistency, Spec §FR-018 vs Edge Cases vs Contracts]
- [x] CHK024 - Is the term "active game" used consistently? FR-010 says "one active game per player," FR-015 says "active game available for the signed-in player," and FR-017 says "Create Game." Does "active" mean the player's own game or any joinable game? [Consistency, Spec §FR-010, FR-015, FR-017] — RESOLVED: FR-015 updated to distinguish player's own active game (Resume) from joinable games (Join).
- [ ] CHK025 - Do the SignalR contract events align with the spec? The spec requires `DashboardStateRefreshed` globally and `JoinConflict` per-player. Does the contract document match FR-015 and FR-018 exactly? [Consistency, Spec §FR-015, FR-018 vs contracts/realtime-events.md]

## Acceptance Criteria Quality

- [ ] CHK026 - Can SC-001 ("95% complete sign-in within 30 seconds") be objectively measured in the shell application without production analytics infrastructure? [Measurability, Spec §SC-001]
- [ ] CHK027 - Can SC-004 ("90% of usability test participants determine join vs create within 10 seconds") be tested without formal usability test infrastructure? [Measurability, Spec §SC-004]
- [ ] CHK028 - Is SC-002 ("100% display Join or Create — never ambiguous or empty") testable for all possible dashboard states (no active games, one joinable game, player already in game, games loading, load error)? [Measurability, Spec §SC-002]
- [ ] CHK029 - Are acceptance scenarios for User Story 2 testable when "completed games" are impossible in this feature (scenarios 1 and 2 reference completed game state)? [Measurability, Spec §US-2]
- [ ] CHK030 - Are the acceptance scenarios for User Story 3 testable without full gameplay — specifically, what constitutes a "game context" routing target? [Measurability, Spec §US-3]

## Scenario Coverage

- [ ] CHK031 - Are requirements defined for the scenario where a player navigates directly to `/dashboard` while unauthenticated? [Coverage, Spec §FR-006]
- [ ] CHK032 - Are requirements defined for the scenario where a player navigates directly to `/game/{id}` for a game they are not part of? [Coverage, Gap]
- [ ] CHK033 - Are requirements defined for the scenario where a player navigates to `/game/{id}` with an invalid or non-existent game ID? [Coverage, Gap]
- [ ] CHK034 - Are requirements defined for concurrent nickname update attempts (two players trying to claim the same nickname simultaneously)? [Coverage, Gap]
- [ ] CHK035 - Are requirements defined for what happens when a player creates a game but is already in an active game (FR-010 constraint violation)? [Coverage, Spec §FR-010]
- [ ] CHK036 - Are requirements defined for SignalR connection failure/reconnection on the dashboard? [Coverage, Gap]
- [ ] CHK037 - Are requirements defined for the landing page appearance for returning users who have a remembered session/cookie? [Coverage, Spec §FR-003]

## Edge Case Coverage

- [x] CHK038 - Is the behavior specified when the email prefix used for nickname derivation (FR-009) collides with an existing nickname? [Edge Case, Spec §FR-009] — RESOLVED: Leave nickname blank, force manual entry on first dashboard visit.
- [ ] CHK039 - Is the fallback behavior specified when the external thumbnail URL returns a broken image? [Edge Case, Spec §FR-008]
- [ ] CHK040 - Is behavior defined for extremely long nicknames or nicknames with special characters? Are length/format constraints specified? [Edge Case, Spec §FR-007, Gap]
- [x] CHK041 - Is behavior defined when a player tries to join their own created game (are they already in it, or do they need to join separately)? [Edge Case, Spec §FR-017] — RESOLVED: Creator is auto-joined as player 1.
- [ ] CHK042 - Is behavior defined when all 7 tables cannot be created on startup (partial table creation failure)? [Edge Case, Gap]
- [ ] CHK043 - Is the behavior specified when Azure Table Storage (or Azurite) is unreachable after initial connection? [Edge Case, Gap]
- [ ] CHK044 - Is behavior defined for games that have been created but never reach maximum players (stuck in ACTIVE state indefinitely)? [Edge Case, Gap]

## Non-Functional Requirements

- [ ] CHK045 - Are accessibility requirements specified for keyboard navigation across the landing page, dashboard, and profile settings? [Accessibility, Gap]
- [ ] CHK046 - Are accessibility requirements specified for screen reader compatibility with Fluent UI components? [Accessibility, Gap]
- [ ] CHK047 - Are responsive/mobile layout requirements specified for the FluentHeader top navigation bar? [Responsiveness, Spec §FR-019, Gap]
- [ ] CHK048 - Are performance requirements specified for dashboard initial load time (beyond the 30-second auth-to-dashboard SC-001)? [Performance, Gap]
- [ ] CHK049 - Are performance requirements specified for SignalR global broadcast under concurrent user load? [Performance, Gap]
- [ ] CHK050 - Are data retention/cleanup requirements specified for Azure Table Storage (e.g., old cancelled/completed games)? [Data Management, Gap]
- [ ] CHK051 - Are logging/observability requirements specified for the custom Identity stores (auth failures, store errors)? [Observability, Gap]

## Dependencies & Assumptions

- [ ] CHK052 - Is the assumption that "Identity pages are kept as-is" validated against the required changes (Register needs profile provisioning, Login needs redirect change)? [Assumption, Spec §Assumptions]
- [ ] CHK053 - Is the Azurite version requirement specified or is any version acceptable? [Dependency, Gap]
- [ ] CHK054 - Is the dependency on `Microsoft.FluentUI.AspNetCore.Components` v4.x pinned to a specific minor version or floating? [Dependency, Spec §Clarifications]
- [ ] CHK055 - Is the assumption that `UseDevelopmentStorage=true` works identically to Azure Table Storage validated for all used operations (AddEntity, DeleteEntity, batch)? [Assumption, Gap]
- [ ] CHK056 - Is the assumption that `Clients.User(playerId)` works with cookie-based auth documented, or does it need configuration? [Assumption, contracts/realtime-events.md]

## Ambiguities & Conflicts

- [x] CHK057 - Does the spec define "game availability" precisely? FR-015 says "active game available for the signed-in player" — does this mean any ACTIVE game with open slots, or a specific game the player was invited to? [Ambiguity, Spec §FR-015] — RESOLVED: Any ACTIVE game with `CurrentPlayerCount < MaxPlayers`.
- [x] CHK058 - Is there a conflict between FR-009 (auto-create profile on "first successful sign-in") and the edge case (auto-create on "registration")? These may be different events depending on email confirmation settings. [Conflict, Spec §FR-009 vs Edge Cases] — RESOLVED: Profile created at registration (CreateAsync), before email confirmation.
- [ ] CHK059 - Is the relationship between the Key Entity "Player Profile" (one-to-one with Identity user) and the data model (profile fields stored directly on `ApplicationUser`) consistent, or does the spec imply a separate entity? [Ambiguity, Spec §Key Entities vs data-model.md]
- [ ] CHK060 - Is "the system MUST prevent unauthenticated users from accessing the dashboard" (FR-006) achieved via route-level `[Authorize]` attribute, middleware, or another mechanism? The spec states the requirement but not the enforcement pattern. [Ambiguity, Spec §FR-006]

## Notes

- Check items off as completed: `[x]`
- Items referencing `[Gap]` indicate requirements that may need to be added to the spec
- Items referencing `[Ambiguity]` or `[Conflict]` should be resolved before implementation begins
- Items referencing specific spec sections `[Spec §XX-NNN]` can be verified against the source document
