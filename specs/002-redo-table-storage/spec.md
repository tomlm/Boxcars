# Feature Specification: Redo Table Storage

**Feature Branch**: `002-redo-table-storage`  
**Created**: 2026-03-06  
**Status**: Draft  
**Input**: User description: "Redo table storage with UsersTable + GamesTable only, user bootstrap data, game creation player/color assignment, and removal of unused storage artifacts"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Create and Start a Game with Explicit Players (Priority: P1)

As a game creator, I can choose which authenticated user occupies each player slot and assign a color per slot before creating the game, so the game starts with the intended roster and turn order.

**Why this priority**: This is the main gameplay entry path and blocks the ability to begin a session with correct participants.

**Independent Test**: Can be fully tested by selecting players and colors on Create Game, creating the game, navigating to the game page, and confirming Start Game is available with the selected roster.

**Acceptance Scenarios**:

1. **Given** a user is on Dashboard, **When** they open Create Game, select users and colors for each slot, and click Create Game, **Then** a game settings record is created and the user is taken to the game page with a Start Game action.
2. **Given** a game was created with selected users in slot order, **When** the game page loads, **Then** the displayed players and colors match the selected slot assignments.

---

### User Story 2 - Persist and Replay Game Timeline (Priority: P2)

As a participant reconnecting to an in-progress game, I can load the latest mutable game state and prior events so gameplay can continue and history is visible.

**Why this priority**: Reliable reconnection and timeline visibility are core to real-time multiplayer continuity.

**Independent Test**: Can be tested by creating a game, producing several events, reconnecting, and confirming current state and action history are restored from stored game records.

**Acceptance Scenarios**:

1. **Given** a game has recorded events, **When** a player reconnects, **Then** the game loads from stored game-event state data and resumes without data loss.
2. **Given** events have been recorded for gameplay actions, **When** the game page shows action history, **Then** events appear in chronological order with their event details.

---

### User Story 3 - Authenticate and Reuse User Profiles (Priority: P3)

As an authenticated user, I have a persistent user profile that is created or looked up by authentication so I can be selected for game slots.

**Why this priority**: User identity consistency is required to build valid player rosters.

**Independent Test**: Can be tested by authenticating as a known user and confirming the user profile exists and can be selected during game creation.

**Acceptance Scenarios**:

1. **Given** a user authenticates, **When** identity is resolved, **Then** a user profile record is created or retrieved using the authenticated email.
2. **Given** initial system setup, **When** user data is initialized, **Then** mock Beatles user profiles are available for selection.

### Edge Cases

- A game creator attempts to assign the same user to multiple slots in one game.
- A game creator leaves one or more required slots unassigned and attempts to create the game.
- A game creator chooses duplicate colors for multiple player slots.
- A reconnecting player joins when no events exist yet beyond the game settings record.
- Event records arrive with out-of-order timestamps; displayed history still preserves intended chronological sequence.
- An authenticated user is missing optional profile attributes (for example, nickname or thumbnail).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST use exactly two storage tables for this feature scope: `UsersTable` and `GamesTable`.
- **FR-002**: The system MUST store authenticated-user profile data in `UsersTable` under entity type `UserEntity` keyed by user email.
- **FR-003**: Authentication flows MUST create or retrieve `UserEntity` records and keep profile fields available for gameplay selection.
- **FR-004**: Initial environment setup MUST prepopulate `UsersTable` with four mock user profiles: Paul, Ringo, George, and John at `@beatles.com` addresses.
- **FR-005**: The system MUST store immutable game configuration in `GamesTable` under entity type `GameEntity`, including game identifier, settings, and ordered players with selected colors.
- **FR-006**: The system MUST store gameplay events in `GamesTable` under entity type `GameEventEntity`, including event kind, event payload, and serialized mutable game-state snapshot.
- **FR-007**: Game events MUST be persisted with sortable event keys that preserve chronological ordering for history and resume operations.
- **FR-008**: The game engine MUST be the authoritative path for all UI-triggered game operations and MUST persist event records before broadcasting updates to connected users.
- **FR-009**: The system MUST support resuming an interrupted game session by loading game state from persisted game-event snapshot data.
- **FR-010**: The Create Game flow MUST be `Dashboard -> Create Game page -> Game page`, where Create Game commits the game settings record and gameplay starts on the game page.
- **FR-011**: The Create Game page MUST allow the game creator to select a user and a color for each player slot before creation.
- **FR-012**: The game page MUST expose a Start Game action after successful game creation.
- **FR-013**: Storage tables, entities, and code paths outside this specified model MUST be removed or de-scoped from active use.

### Key Entities *(include if feature involves data)*

- **UserEntity**: Authenticated user profile record keyed by email, containing identity and display attributes used for player selection.
- **GameEntity**: Immutable game setup record containing game identifier, game settings, and ordered player/color assignments.
- **GameEventEntity**: Ordered gameplay event record containing event metadata, event payload, and a serialized mutable game snapshot for recovery.

### Assumptions

- Authentication remains the source of truth for user identity and triggers user-profile create/lookup behavior.
- The four Beatles mock users are non-production seed data and are expected only where initialization is enabled.
- Game slot count and valid color set are governed by existing game rules and current UI constraints.

### Dependencies

- Existing authentication integration capable of resolving authenticated email identity.
- Existing game engine integration that applies rule validation and event publication to connected clients.
- Existing game page UI that can consume persisted state and timeline data.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of new game creations persist one immutable game setup record before user navigation reaches the game page.
- **SC-002**: 100% of UI-triggered gameplay actions produce a persisted game-event record and appear in action history in chronological order.
- **SC-003**: In reconnection testing, at least 95% of interrupted sessions resume to the latest persisted game state without manual repair.
- **SC-004**: In usability testing with seeded users, game creators successfully assign players and colors and complete game creation on first attempt in at least 90% of trials.
