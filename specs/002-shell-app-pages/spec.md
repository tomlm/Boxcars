# Feature Specification: BoxCars Shell Application Pages

**Feature Branch**: `002-shell-app-pages`  
**Created**: 2026-02-27  
**Status**: Draft  
**Input**: User description: "Rewrite the default Blazor template pages into the BoxCars shell application: public landing page with email sign-in, authenticated player dashboard with stats and game availability, game entry routing, and profile menu with settings and sign out"

## Starting Point

The application currently contains the default Blazor Server template with ASP.NET Core Identity scaffolded:

- **Home page** (`/`): "Hello, world!" placeholder
- **Counter page** (`/counter`): Click-counter demo
- **Weather page** (`/weather`): Random weather forecast demo
- **Auth page** (`/auth`): Simple "You are authenticated" test page
- **Error page** (`/Error`): Default error display
- **Sidebar navigation**: Links to Home, Counter, Weather, Auth Required, plus Account links (Register/Login/Logout/Manage)
- **Identity pages**: Full ASP.NET Core Identity scaffold (Login, Register, ForgotPassword, ConfirmEmail, Manage, etc.)
- **ApplicationUser**: Empty `IdentityUser` subclass with no custom properties
- **Layout**: Sidebar-based `MainLayout` with top-row "About" link

This feature replaces the demo pages with the BoxCars application shell while preserving and building upon the existing Identity infrastructure.

## Clarifications

### Session 2026-02-27

- Q: How does the player provide or select their thumbnail image? → A: URL field — the player pastes a URL to an external image.
- Q: Should stats infrastructure be built now or stubbed since no gameplay exists yet? → A: Stub the stats section with placeholder UI; implement real aggregation when gameplay ships.
- Q: What does the destination look like when a player creates or joins a game (no gameplay yet)? → A: Placeholder game page showing game ID and "gameplay coming soon" with a link back to dashboard.
- Q: What layout pattern should replace the current sidebar-based layout? → A: Top navigation bar with BoxCars brand on the left and profile menu on the right; sidebar removed.
- Q: What storage technology should be used for all application data? → A: Azure Table Storage only, for everything including Identity user accounts. Emulated locally via Azurite. No SQL Server, no EF Core. Custom Identity stores (`IUserStore<T>`, etc.) backed by Azure Table Storage.
- Q: Must player display nicknames be globally unique? → A: Yes — nicknames must be globally unique, enforced via an index table (same pattern as email/username indexes).
- Q: What is the maximum number of players per game? → A: Configurable per game, 2–6 players (Rail Baron standard range).
- Q: Should the thumbnail URL be validated for format or reachability? → A: No validation — accept any string. Validation deferred to a future hardening pass.
- Q: What triggers a real-time dashboard refresh via SignalR? → A: Push on any game-state change globally — all connected dashboard clients receive updates when any game is created, joined, filled, or closed.
- Q: What UI component library should be used? → A: Microsoft Fluent UI Blazor components (`Microsoft.FluentUI.AspNetCore.Components` v4.x). Replaces Bootstrap. Provides `FluentLayout`, `FluentHeader`, `FluentProfileMenu`, `FluentCard`, `FluentButton`, `FluentTextField`, and full icon set.
- Q: When a player has no active game, should the dashboard show joinable games from other players or just "Create Game"? → A: Show joinable games from other players (ACTIVE games with open slots). Players can join one OR create a new one.
- Q: When a player already has an active game, what does the dashboard show? → A: A "Resume Game" button linking to their active game. Join/Create options are not shown.
- Q: When a player creates a game, are they automatically the first player in it? → A: Yes — the creator is auto-joined as player 1. `CurrentPlayerCount` starts at 1, creator added to `GamePlayersTable` and `PlayerActiveGameIndexTable`.
- Q: When should the player profile (nickname, thumbnail) be created — at registration or first sign-in? → A: At registration, when `UserManager.CreateAsync` is called (before email confirmation).
- Q: When auto-provisioning a nickname from the email prefix and it collides with an existing nickname, what happens? → A: Leave nickname blank and force the user to set it on their first dashboard visit.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Sign In from Landing Page (Priority: P1)

As a player, I can open the BoxCars landing page and sign in with my email account so I can access game features.

**Why this priority**: Authentication is the gate to all other player experiences. Without sign-in, no other feature is reachable. The existing Identity scaffold provides registration and login, but the default Home page does not direct users to it — the landing page must replace Home and orient visitors toward signing in.

**Independent Test**: From a signed-out state, navigate to the root URL, complete email sign-in, and confirm the user reaches the dashboard.

**Acceptance Scenarios**:

1. **Given** I am not signed in, **When** I navigate to the application root, **Then** I see the BoxCars landing page with a clear sign-in action.
2. **Given** I am not signed in, **When** I complete email authentication successfully, **Then** I am signed in and redirected to the dashboard.
3. **Given** I am not signed in, **When** authentication fails or is canceled, **Then** I remain on the landing page and see a clear, non-technical failure message.
4. **Given** I am already signed in, **When** I navigate to the application root, **Then** I am redirected to the dashboard automatically.

---

### User Story 2 - View Player Dashboard (Priority: P2)

As an authenticated player, I can view a dashboard that shows my player profile, stats summary, and current game availability so I can decide what to do next.

**Why this priority**: The dashboard is the primary post-login destination and replaces the default Counter/Weather demo pages. It gives immediate value by orienting the player and showing their current state.

**Independent Test**: Sign in with a test account and verify the dashboard renders the player's nickname, thumbnail, conditional stats section, and a join-or-create game action.

**Acceptance Scenarios**:

1. **Given** I am signed in and have completed at least one game, **When** I open the dashboard, **Then** I see my nickname, thumbnail, and player stats summary.
2. **Given** I am signed in and have zero completed games, **When** I open the dashboard, **Then** I see my nickname and thumbnail but the stats section is hidden and a first-game welcome message is displayed instead.
3. **Given** I am signed in and there is an active game available to me, **When** I open the dashboard, **Then** I see a Join Game action.
4. **Given** I am signed in and there is no active game available, **When** I open the dashboard, **Then** I see a Create Game action.

---

### User Story 3 - Start or Join Game from Dashboard (Priority: P3)

As an authenticated player, I can choose to join an available game or create a new one from the dashboard so I can begin gameplay setup.

**Why this priority**: This connects the shell application to gameplay initiation. It establishes the game entry pattern while keeping actual game rules and gameplay for later features.

**Independent Test**: From the dashboard, trigger join or create actions and verify the system transitions to the appropriate game context entry point.

**Acceptance Scenarios**:

1. **Given** an active game is available and I am eligible to join, **When** I select Join Game, **Then** I am routed into that game context.
2. **Given** no active game is available, **When** I select Create Game, **Then** a new game is created and I am routed into it.
3. **Given** I select Join Game but the game state has changed since the dashboard loaded (filled, started, or closed), **When** the join attempt fails, **Then** I remain on the dashboard, the game availability refreshes, and a clear conflict message is displayed.

---

### User Story 4 - Manage Profile and Sign Out (Priority: P3)

As an authenticated player, I can access a profile menu to update my display settings and sign out, so I can manage my identity and securely exit.

**Why this priority**: Profile management and sign-out are baseline shell capabilities. The existing Identity "Manage" pages handle email/password but not BoxCars-specific profile fields (nickname, thumbnail). This story replaces the sidebar account links with a cleaner profile menu.

**Independent Test**: While signed in, open the profile menu, navigate to settings, save a nickname change, and verify it appears in the UI. Then sign out and verify return to the landing page.

**Acceptance Scenarios**:

1. **Given** I am signed in, **When** I look at the application chrome, **Then** I see a profile menu showing my nickname and thumbnail with Settings and Sign Out actions.
2. **Given** I am signed in, **When** I open Settings and update my nickname, **Then** the updated nickname is immediately reflected in the application UI without re-signing in.
3. **Given** I am signed in, **When** I open Settings and update my thumbnail, **Then** the updated thumbnail is immediately reflected in the application UI without re-signing in.
4. **Given** I am signed in, **When** I choose Sign Out, **Then** my session is terminated and I am returned to the landing page.

### Edge Cases

- What happens when a first-time user completes registration but has no player profile yet? The system creates a profile during registration with email-derived nickname (if unique) or blank nickname (if collision). If nickname is blank, the dashboard prompts the player to choose one before other actions.
- What happens if the dashboard cannot load stats or game data temporarily? The dashboard displays a friendly error state with a retry action instead of crashing.
- What happens if game availability changes between the dashboard loading and the player clicking Join? The system keeps the player on the dashboard, refreshes game state, and shows a conflict message (no silent failure).
- What happens if a profile settings save fails due to a transient error? The settings page shows a clear save-failed message and retains unsaved changes so the user can retry.
- What happens if sign-out is triggered while the player is in an active game context? The session ends, the player is returned to the landing page, and their in-game state is preserved for reconnection later.
- What happens if a signed-in user navigates directly to the landing page URL? They are redirected to the dashboard.
- How does the system handle a player who has a profile from a previous session but whose Identity account was deleted? The system treats them as a new user through normal registration flow.
- What happens if a player tries to save a nickname that another player already has? The settings page shows a clear "nickname taken" validation error and retains the unsaved input so the player can choose a different nickname.

## Pages to Remove

The following default template pages serve no purpose in the BoxCars application and must be removed:

- **Counter** (`/counter`): Demo click-counter — replaced by Dashboard functionality.
- **Weather** (`/weather`): Demo forecast table — replaced by Dashboard functionality.
- **Auth** (`/auth`): Simple authorize-test page — replaced by Dashboard (which inherently requires auth).

## Pages to Transform

- **Home** (`/`): "Hello, world!" replaced by the BoxCars landing page with sign-in direction for unauthenticated users, auto-redirect for authenticated users.
- **MainLayout**: Sidebar-based layout replaced by a top navigation bar layout with BoxCars brand and profile menu for authenticated users.
- **NavMenu**: Sidebar nav component replaced by a top bar component — demo page links removed, brand link and profile menu added.

## Requirements *(mandatory)*

### Functional Requirements

#### Landing & Authentication

- **FR-001**: The application root URL MUST display a BoxCars landing page for unauthenticated visitors, replacing the default "Hello, world!" Home page.
- **FR-002**: The landing page MUST provide a clear path to email sign-in using the existing Identity infrastructure.
- **FR-003**: Authenticated users who navigate to the landing page MUST be redirected to the dashboard automatically.
- **FR-004**: The system MUST redirect users to the dashboard after successful sign-in.
- **FR-005**: Authentication failures MUST display a clear, non-technical message on the landing page without exposing sensitive details.
- **FR-006**: The system MUST prevent unauthenticated users from accessing the dashboard or any authenticated pages.

#### Player Profile

- **FR-007**: The player profile MUST include a nickname (display name) distinct from the Identity email/username. Nicknames MUST be globally unique across all players, enforced via an index table.
- **FR-008**: The player profile MUST include a thumbnail as an external image URL for visual identification throughout the application. No URL validation is performed in this feature; any string is accepted.
- **FR-009**: At registration (during `CreateAsync`), the system MUST automatically create a player profile with defaults: email-derived nickname (if unique; blank if collision detected) and default thumbnail URL. If the nickname is blank after registration, the dashboard MUST prompt the player to set a nickname before other actions.
- **FR-010**: The system MUST enforce at most one active game per player.

#### Dashboard

- **FR-011**: The application MUST provide an authenticated dashboard page that replaces the Counter and Weather demo pages as the primary post-login destination.
- **FR-012**: The dashboard MUST display the player's nickname and thumbnail.
- **FR-013**: The dashboard MUST reserve a stats summary area that displays placeholder content until real gameplay stats are available in a later feature.
- **FR-014**: For all players in this feature (no completed games possible yet), the dashboard MUST show a first-game welcome message in place of stats.
- **FR-015**: The dashboard MUST check if the player has an active game. If yes, display a "Resume Game" action (no Join/Create shown). If no, query all ACTIVE-status games with open slots and display either a Join Game action (if joinable games exist) or a Create Game action (if none). The dashboard MUST update in real time via SignalR when any game-state change occurs globally (game created, player joined/left, game filled/closed).

#### Game Entry

- **FR-016**: The Join Game action MUST route the player to a placeholder game page that displays the game identifier, a "gameplay coming soon" message, and a link back to the dashboard.
- **FR-017**: The Create Game action MUST create a new game with a configurable player count (2–6, Rail Baron standard). The creator is automatically joined as the first player (`CurrentPlayerCount = 1`). The system then routes the player to the placeholder game page with the new game's identifier.
- **FR-018**: If a Join Game attempt fails because game state changed after the dashboard loaded, the system MUST keep the player on the dashboard, refresh game availability, and display a clear conflict message.

#### Profile Menu & Navigation

- **FR-019**: The application layout MUST replace the sidebar with a top navigation bar containing the BoxCars brand on the left and an authenticated profile menu (displaying the player's nickname and thumbnail) on the right.
- **FR-020**: The profile menu MUST include Settings and Sign Out actions.
- **FR-021**: The Settings page MUST allow the player to update their nickname and thumbnail URL.
- **FR-022**: On successful settings save, updated nickname and thumbnail MUST be reflected immediately in the application UI without requiring re-authentication.
- **FR-023**: The Sign Out action MUST terminate the authenticated session and return the user to the landing page.

#### Cleanup

- **FR-024**: The Counter, Weather, and Auth demo pages MUST be removed from the application.
- **FR-025**: Navigation links to removed demo pages MUST be removed from the layout.

### Key Entities

- **Player Profile**: Application-level player record linked to the Identity user; includes nickname, thumbnail image reference, aggregate game statistics, and creation timestamp. One-to-one relationship with the Identity user account.
- **Player Statistics Summary**: Placeholder UI area on the dashboard reserved for future game statistics (e.g., games played, wins, losses). Stubbed in this feature since no gameplay exists yet; real aggregation deferred to the gameplay feature.
- **Game Summary**: Lightweight view of a game used by the dashboard to decide join-or-create state. Includes game status (active/inactive), participant relationship, and join eligibility. The game destination in this feature is a placeholder page; full game entity details and gameplay mechanics are deferred to a later feature.

## Assumptions

- The existing ASP.NET Core Identity scaffold (registration, login, email confirmation, password reset, account management) is preserved for email authentication UX. The Identity storage backend is replaced: EF Core + SQL Server removed, replaced with custom Identity stores backed by Azure Table Storage (Azurite locally). Identity pages themselves are kept as-is.
- The player profile is a BoxCars-specific extension of the Identity user, not a replacement. Identity handles authentication; the profile holds game-related display data.
- If nickname is not supplied during profile creation, the system derives one from the user's email address (portion before @). If thumbnail URL is not supplied, a default placeholder image URL is used.
- Game creation and join actions in this feature establish routing entry points only. Actual game setup, rules, and gameplay mechanics are deferred to later features.
- A player can have at most one active game at a time for this initial shell.
- Each game supports 2–6 players (configurable at creation). A game is "full" and no longer joinable when its player count reaches the configured maximum.
- Performance targets assume standard web application expectations under normal network conditions — no extraordinary scale or latency requirements at this stage.
- The Error page is retained as-is since it serves a functional purpose.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: At least 95% of users who start email sign-in complete authentication and reach the dashboard within 30 seconds under normal network conditions.
- **SC-002**: 100% of authenticated dashboard visits display either Join Game or Create Game as a clear next action — never an ambiguous or empty state.
- **SC-003**: At least 95% of first-time sign-ins automatically receive a usable player profile (with nickname and thumbnail) without manual intervention.
- **SC-004**: At least 90% of usability test participants can correctly determine whether they should join an existing game or create a new one within 10 seconds of dashboard load.
- **SC-005**: All default template demo pages (Counter, Weather, Auth) are fully removed with no residual navigation links or routes.
