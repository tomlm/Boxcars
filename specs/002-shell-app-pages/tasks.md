# Tasks: BoxCars Shell Application Pages

**Input**: Design documents from `/specs/002-shell-app-pages/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/realtime-events.md, quickstart.md

**Tests**: Not requested for this feature. Test tasks omitted per constitution Principle III (Simplicity & Ship Fast).

**Organization**: Tasks grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3, US4)
- Include exact file paths in descriptions

## Phase 1: Setup

**Purpose**: Remove EF Core / SQL Server artifacts, swap NuGet packages, delete demo pages and Bootstrap

- [x] T001 Update NuGet packages in src/Boxcars/Boxcars.csproj — remove Microsoft.AspNetCore.Identity.EntityFrameworkCore, Microsoft.EntityFrameworkCore.SqlServer, Microsoft.EntityFrameworkCore.Tools, Microsoft.AspNetCore.Diagnostics.EntityFrameworkCore; add Azure.Data.Tables, Microsoft.AspNetCore.SignalR.Client, Microsoft.FluentUI.AspNetCore.Components (v4.\*), Microsoft.FluentUI.AspNetCore.Components.Icons (v4.\*)
- [x] T002 [P] Delete EF Core artifacts: src/Boxcars/Data/ApplicationDbContext.cs and entire src/Boxcars/Data/Migrations/ directory
- [x] T003 [P] Delete demo pages: src/Boxcars/Components/Pages/Counter.razor, src/Boxcars/Components/Pages/Weather.razor, src/Boxcars/Components/Pages/Auth.razor
- [x] T004 [P] Delete sidebar nav files: src/Boxcars/Components/Layout/NavMenu.razor and src/Boxcars/Components/Layout/NavMenu.razor.css
- [x] T005 [P] Delete Bootstrap directory: src/Boxcars/wwwroot/bootstrap/

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story — table entities, custom Identity store, storage + SignalR + Fluent UI registration, layout transformation, and shared services

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [x] T006 [P] Create table name constants in src/Boxcars/Identity/TableNames.cs — static class with string constants for all 7 tables: UsersTable, UserEmailIndexTable, UserNameIndexTable, NicknameIndexTable, GamesTable, GamePlayersTable, PlayerActiveGameIndexTable
- [x] T007 [P] Rewrite ApplicationUser as ITableEntity in src/Boxcars/Data/ApplicationUser.cs — remove IdentityUser inheritance; implement Azure.Data.Tables.ITableEntity with PartitionKey="USER", RowKey=userId (GUID string); add computed Id property (=> RowKey); include all Identity properties (Email, NormalizedEmail, UserName, NormalizedUserName, PasswordHash, SecurityStamp, EmailConfirmed, LockoutEnd, LockoutEnabled, AccessFailedCount); add profile properties (Nickname, NormalizedNickname, ThumbnailUrl); add scaffold-compatibility properties (ConcurrencyStamp, PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled) per data-model.md UsersTable schema
- [x] T008 [P] Create shared index entity in src/Boxcars/Data/IndexEntity.cs — ITableEntity POCO with PartitionKey, RowKey, UserId (string), and GameId (string, nullable) for reuse across UserEmailIndexTable, UserNameIndexTable, NicknameIndexTable, and PlayerActiveGameIndexTable per data-model.md
- [x] T009 [P] Create game entity in src/Boxcars/Data/GameEntity.cs — ITableEntity with PartitionKey (status string: "ACTIVE", "COMPLETED", "CANCELLED"), RowKey (game ID GUID string), CreatorId (string), MaxPlayers (int, default 6), CurrentPlayerCount (int), CreatedAt (DateTime UTC) per data-model.md GamesTable schema
- [x] T010 [P] Create game player entity in src/Boxcars/Data/GamePlayerEntity.cs — ITableEntity with PartitionKey (game ID), RowKey (player user ID), JoinedAt (DateTime UTC) per data-model.md GamePlayersTable schema
- [x] T011 Implement custom Identity store in src/Boxcars/Identity/TableStorageUserStore.cs — implement IUserStore\<ApplicationUser\>, IUserEmailStore\<ApplicationUser\>, IUserPasswordStore\<ApplicationUser\>, IUserSecurityStampStore\<ApplicationUser\>, IUserLockoutStore\<ApplicationUser\> backed by Azure.Data.Tables TableClient; inject TableServiceClient and resolve TableClients for UsersTable, UserEmailIndexTable, UserNameIndexTable, NicknameIndexTable; CreateAsync: generate GUID RowKey, derive Nickname from email prefix (check NicknameIndexTable — if 409 Conflict leave Nickname blank), set default ThumbnailUrl, insert into UsersTable + all 3 index tables; FindByIdAsync: point read PK="USER" RK=userId; FindByEmailAsync: lookup UserEmailIndexTable then point read UsersTable; FindByNameAsync: lookup UserNameIndexTable then point read UsersTable; UpdateAsync: use ETag optimistic concurrency on UsersTable; DeleteAsync: remove user + all index entries; implement all interface methods per research.md R1–R3
- [x] T012 [P] Create SignalR hub in src/Boxcars/Hubs/BoxCarsHub.cs — empty \[Authorize\] hub class inheriting Hub; events dispatched from services via IHubContext\<BoxCarsHub\> not from hub methods; per contracts/realtime-events.md
- [x] T013 Update src/Boxcars/appsettings.json — remove "ConnectionStrings" section entirely; add "AzureTableStorage" section with empty "ConnectionString" property per quickstart.md
- [x] T014 [P] Update src/Boxcars/appsettings.Development.json — add "AzureTableStorage" section with "ConnectionString": "UseDevelopmentStorage=true" for Azurite local emulator per quickstart.md
- [x] T015 Rewrite src/Boxcars/Program.cs — remove all EF Core registrations (AddDbContext, AddDatabaseDeveloperPageExceptionFilter, UseMigrationsEndPoint, AddEntityFrameworkStores); register TableServiceClient singleton from AzureTableStorage:ConnectionString config; register AddHttpClient() then AddFluentUIComponents(); configure authentication with AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies(); register AddIdentityCore\<ApplicationUser\>(options => options.SignIn.RequireConfirmedAccount = true).AddSignInManager().AddDefaultTokenProviders(); register AddScoped\<IUserStore\<ApplicationUser\>, TableStorageUserStore\>(); register AddCascadingAuthenticationState(); register PlayerProfileService and GameService as scoped; map SignalR hub endpoint at /hubs/boxcars; add startup table auto-creation (CreateTableIfNotExistsAsync for all 7 tables via TableServiceClient); keep existing Identity endpoint mapping and antiforgery; per research.md R1, R9, R10, R11 and quickstart.md
- [x] T016 [P] Update src/Boxcars/Components/_Imports.razor — add @using Microsoft.FluentUI.AspNetCore.Components and @using Icons = Microsoft.FluentUI.AspNetCore.Components.Icons per research.md R11
- [x] T017 Update src/Boxcars/Components/App.razor — replace Bootstrap CSS link (bootstrap/bootstrap.min.css) with Fluent UI reboot.css link (\_content/Microsoft.FluentUI.AspNetCore.Components/css/reboot.css); add \<FluentDesignTheme StorageName="theme" /\> component for auto-persisted light/dark mode; keep app.css link; per research.md R11
- [x] T018 Transform src/Boxcars/Components/Layout/MainLayout.razor — replace sidebar layout with FluentLayout containing: FluentHeader with FluentStack (Orientation.Horizontal, SpaceBetween) holding BoxCars brand text (left) and FluentProfileMenu (right) showing authenticated user's Nickname and ThumbnailUrl loaded via cascading AuthenticationState + PlayerProfileService, with popover actions for Settings (NavigateTo /profile-settings) and Sign Out (form POST to Identity logout endpoint); FluentBodyContent wrapping @Body; add Fluent provider components at end: FluentToastProvider, FluentDialogProvider, FluentTooltipProvider, FluentMessageBarProvider; per research.md R11
- [x] T019 [P] Rewrite src/Boxcars/Components/Layout/MainLayout.razor.css — remove all sidebar layout styles; add minimal Fluent UI layout styles for top nav bar, header spacing, and body content area
- [x] T020 Update src/Boxcars/wwwroot/app.css — remove all Bootstrap body styles and Bootstrap-specific class references; replace with Fluent CSS variable-based body styles using --neutral-foreground-rest for color and --neutral-fill-layer-rest for background; per research.md R11
- [x] T021 Create player profile service in src/Boxcars/Services/PlayerProfileService.cs — inject TableServiceClient, resolve TableClients for UsersTable and NicknameIndexTable; GetProfileAsync(string userId, CancellationToken): load ApplicationUser from UsersTable by PK="USER" RK=userId; UpdateNicknameAsync(string userId, string newNickname, CancellationToken): normalize nickname (uppercase), try insert new NicknameIndexTable entry (if 409 return conflict result), update Nickname+NormalizedNickname on UsersTable entity with ETag, delete old NicknameIndexTable entry, rollback new index on UsersTable update failure; UpdateThumbnailUrlAsync(string userId, string newUrl, CancellationToken): update ThumbnailUrl on UsersTable entity with ETag; all methods async with CancellationToken per data-model.md nickname update flow and constitution conventions
- [x] T022 Create game service in src/Boxcars/Services/GameService.cs — inject TableServiceClient (resolve TableClients for GamesTable, GamePlayersTable, PlayerActiveGameIndexTable) and IHubContext\<BoxCarsHub\>; GetDashboardStateAsync(string playerId, CancellationToken): query PlayerActiveGameIndexTable PK="ACTIVE\_GAME" RK=playerId — if found return active game ID (resume state), else query GamesTable PK="ACTIVE" and filter for CurrentPlayerCount \< MaxPlayers returning list of joinable games; CreateGameAsync(string creatorId, int maxPlayers, CancellationToken): generate game ID GUID, insert GameEntity PK="ACTIVE" with CurrentPlayerCount=1, insert GamePlayerEntity PK=gameId RK=creatorId, insert PlayerActiveGameIndexTable PK="ACTIVE\_GAME" RK=creatorId with GameId, broadcast DashboardStateRefreshed via Clients.All; JoinGameAsync(string playerId, string gameId, CancellationToken): check PlayerActiveGameIndexTable for existing active game (reject if exists), read GameEntity and validate CurrentPlayerCount \< MaxPlayers, insert GamePlayerEntity + PlayerActiveGameIndexTable entry, increment CurrentPlayerCount with ETag concurrency (on conflict: delete inserted rows, send JoinConflict to Clients.User(playerId) with reason, return failure), broadcast DashboardStateRefreshed on success; per data-model.md join flow, dashboard decision logic, and contracts/realtime-events.md
- [x] T023 Audit and update Account infrastructure files for non-EF-Core compatibility — review src/Boxcars/Components/Account/IdentityComponentsEndpointRouteBuilderExtensions.cs, IdentityRevalidatingAuthenticationStateProvider.cs, IdentityUserAccessor.cs, IdentityNoOpEmailSender.cs, and Account/Pages/\_Imports.razor; remove any Microsoft.EntityFrameworkCore using statements, DbContext references, or AddEntityFrameworkStores calls; ensure all files compile cleanly with the ITableEntity-based ApplicationUser and custom TableStorageUserStore; verify ExternalLogin.razor and Register.razor create ApplicationUser correctly via Activator.CreateInstance\<ApplicationUser\>()

**Checkpoint**: Foundation ready — all 7 table entities, custom Identity store, PlayerProfileService, GameService, SignalR hub, Fluent UI layout, and Program.cs wiring in place. User story implementation can now begin.

---

## Phase 3: User Story 1 — Sign In from Landing Page (Priority: P1) 🎯 MVP

**Goal**: Unauthenticated visitors see a BoxCars landing page with a clear sign-in action; authenticated users auto-redirect to the dashboard

**Independent Test**: From a signed-out state, navigate to the root URL, see the BoxCars landing page, complete email sign-in, and confirm redirect to /dashboard. Navigate to root while signed in, confirm auto-redirect to /dashboard.

### Implementation for User Story 1

- [x] T024 [US1] Transform Home.razor into landing page in src/Boxcars/Components/Pages/Home.razor — set \[AllowAnonymous\] attribute; inject NavigationManager; add CascadingParameter for Task\<AuthenticationState\>; in OnInitializedAsync check if user is authenticated and redirect to /dashboard if so; render BoxCars branding with FluentCard containing game title, tagline, and a "Play Now" FluentButton that navigates to /Account/Login?ReturnUrl=%2Fdashboard; remove all default "Hello world" template content; per research.md R7 and spec.md FR-001 through FR-003
- [x] T025 [US1] Update default post-login redirect to /dashboard in src/Boxcars/Components/Account/Pages/Login.razor — change the ReturnUrl query parameter default value from "/" to "/dashboard" so successful login redirects to the dashboard when no explicit ReturnUrl is specified; per research.md R5 and spec.md FR-004

**Checkpoint**: Users can visit the landing page, sign in via email Identity, and reach /dashboard. Authenticated users auto-redirect from landing to dashboard. MVP gate passed.

---

## Phase 4: User Story 2 — View Player Dashboard (Priority: P2)

**Goal**: Authenticated players see their profile (nickname + thumbnail), stubbed stats with a welcome message, and current game availability on the dashboard with real-time SignalR updates

**Independent Test**: Sign in with a test account, navigate to /dashboard, verify nickname and thumbnail display, stats stub with first-game welcome message, game availability section showing either "Resume Game" (if active game), joinable games list with Join buttons, or "Create Game" button. Open a second browser and create a game — verify first browser's dashboard updates in real time via SignalR.

### Implementation for User Story 2

- [x] T026 [US2] Create dashboard page in src/Boxcars/Components/Pages/Dashboard.razor — \[Authorize\] page at route /dashboard; inject PlayerProfileService, GameService, NavigationManager, IMessageService; add CascadingParameter for AuthenticationState to get current user ID; in OnInitializedAsync load player profile via PlayerProfileService.GetProfileAsync and dashboard state via GameService.GetDashboardStateAsync; render player identity section with FluentPersona showing nickname and thumbnail; render FluentCard stats area with first-game welcome message as placeholder (FR-014 — no completed games possible yet); render game availability section: if player has active game show "Resume Game" FluentButton linking to /game/{gameId} (hide Join/Create), else if joinable games exist show FluentCard list of available games each with game info and "Join" FluentButton, else show "Create Game" FluentButton with FluentSelect for player count (2–6); establish HubConnection to /hubs/boxcars in OnAfterRenderAsync (firstRender), register DashboardStateRefreshed handler to re-query GetDashboardStateAsync and call StateHasChanged, register JoinConflict handler to show error via IMessageService FluentMessageBar and re-query state; implement IAsyncDisposable to dispose HubConnection; add try-catch around data loading with friendly error FluentMessageBar and retry FluentButton on failure; per spec.md FR-011 through FR-015 and contracts/realtime-events.md
- [x] T027 [US2] Add nickname-required prompt on dashboard in src/Boxcars/Components/Pages/Dashboard.razor — after loading profile, check if Nickname is null or empty (collision during registration left it blank per FR-009); if blank, show a prominent FluentCard or FluentDialog with FluentTextField prompting user to choose a nickname; validate uniqueness by calling PlayerProfileService.UpdateNicknameAsync; on success reload profile and show full dashboard; on "nickname taken" error show FluentMessageBar validation message and retain input; disable all other dashboard actions (Resume/Join/Create) until nickname is set; per spec.md FR-009 edge case

**Checkpoint**: Dashboard fully functional — profile display, stats stub, game availability with real-time SignalR updates, and nickname prompt for users with blank nicknames.

---

## Phase 5: User Story 3 — Start or Join Game from Dashboard (Priority: P3)

**Goal**: Players can create a new game (with configurable player count 2–6) or join an available game from the dashboard, arriving at a placeholder game page

**Independent Test**: From the dashboard, create a game with max players 4, verify routing to /game/{id} showing placeholder content and back-to-dashboard link. Open a second browser, sign in as a different user, verify the new game appears in the joinable games list, join it, verify routing to /game/{id}. Try joining a full game from a third browser — verify conflict message and dashboard refresh.

### Implementation for User Story 3

- [x] T028 [P] [US3] Create placeholder game page in src/Boxcars/Components/Pages/Game.razor — \[Authorize\] page at route /game/{GameId}; accept GameId as a string route parameter; render FluentCard containing the game ID, a "Gameplay coming soon" message, and a FluentButton linking back to /dashboard; per spec.md FR-016
- [x] T029 [US3] Wire Create Game and Join Game actions on dashboard in src/Boxcars/Components/Pages/Dashboard.razor — Create Game button onClick: call GameService.CreateGameAsync(currentUserId, selectedMaxPlayers) then NavigateTo /game/{newGameId}; Join Game button onClick: call GameService.JoinGameAsync(currentUserId, gameId) — on success NavigateTo /game/{gameId}, on failure show FluentMessageBar conflict message via IMessageService and re-query dashboard state to refresh game list; per spec.md FR-017, FR-018, and edge case for game availability change

**Checkpoint**: Full game creation and joining flow works end-to-end — dashboard → create/join action → placeholder game page → back to dashboard.

---

## Phase 6: User Story 4 — Manage Profile and Sign Out (Priority: P3)

**Goal**: Players can update their nickname and thumbnail URL via a settings page accessible from the profile menu; changes reflect immediately in the UI; sign out returns to landing page

**Independent Test**: While signed in, open profile menu in top nav, navigate to Settings (/profile-settings), change nickname to a new unique value, save, verify the profile menu in the header immediately reflects the updated nickname. Change thumbnail URL, save, verify update. Try saving a nickname that another player already has — verify "nickname taken" error with input retained. Sign out via profile menu — verify return to landing page.

### Implementation for User Story 4

- [x] T030 [US4] Create profile settings page in src/Boxcars/Components/Pages/ProfileSettings.razor — \[Authorize\] page at route /profile-settings; inject PlayerProfileService, NavigationManager, IMessageService; load current profile via PlayerProfileService.GetProfileAsync in OnInitializedAsync; render FluentCard with FluentTextField for Nickname (bound to form model) and FluentTextField for Thumbnail URL (bound to form model); "Save" FluentButton calls PlayerProfileService.UpdateNicknameAsync (if nickname changed) and UpdateThumbnailUrlAsync (if thumbnail changed); on success show FluentMessageBar confirmation and trigger MainLayout profile menu refresh (via shared state service, event callback, or NavigationManager.Refresh); on nickname conflict (409) show "Nickname already taken" FluentMessageBar error and retain unsaved input for retry; on transient save failure show "Save failed, please try again" FluentMessageBar and retain unsaved input; per spec.md FR-021, FR-022, edge cases for save failure and nickname collision

**Checkpoint**: Profile management fully functional — nickname and thumbnail editable with immediate UI reflection. Sign out via profile menu (wired in Phase 2 MainLayout) returns to landing page per FR-023.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Validation, cleanup, and final verification across all stories

- [x] T031 Verify no residual EF Core references — search entire src/Boxcars/ directory for Microsoft.EntityFrameworkCore, ApplicationDbContext, UseMigrationsEndPoint, AddEntityFrameworkStores, AddDbContext, and SQL Server ConnectionStrings; remove any remaining references found
- [x] T032 Verify all navigation links updated — confirm no dead links to removed Counter, Weather, or Auth pages remain in any component, layout, or configuration file; confirm routes /dashboard, /game/{GameId}, /profile-settings all resolve correctly; confirm no references to deleted NavMenu component remain
- [x] T033 Run quickstart.md end-to-end validation — start Azurite, run the application (dotnet run from src/Boxcars/), verify all 7 tables are auto-created, register a new user account, confirm landing page renders with Fluent UI styling and "Play Now" button, complete sign-in and verify redirect to /dashboard, verify nickname/thumbnail display and stats welcome message, create a game and verify routing to placeholder game page, open second browser and join the game, update profile settings and verify immediate reflection, sign out and verify return to landing page; per quickstart.md verification steps

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 completion (NuGet packages must be swapped before building)
- **User Story 1 (Phase 3)**: Depends on Phase 2 completion
- **User Story 2 (Phase 4)**: Depends on Phase 2 completion
- **User Story 3 (Phase 5)**: Depends on Phase 2 + Phase 4 (Dashboard must exist to wire Create/Join actions)
- **User Story 4 (Phase 6)**: Depends on Phase 2 completion (independent of US1, US2, US3)
- **Polish (Phase 7)**: Depends on all prior phases

### User Story Dependencies

- **US1 (P1)**: Independent after Foundational — can start immediately after Phase 2
- **US2 (P2)**: Independent after Foundational — can run in parallel with US1
- **US3 (P3)**: Depends on US2 — Dashboard page must exist before wiring Create/Join actions
- **US4 (P3)**: Independent after Foundational — can run in parallel with US1, US2

### Within Each User Story

- Core page implementation before integration refinements
- Service calls before UI feedback logic
- Commit after each task or logical group

### Parallel Opportunities

Within Phase 1:
- T002, T003, T004, T005 can all run in parallel (independent delete operations on different files)

Within Phase 2:
- T006, T007, T008, T009, T010, T012, T016 can all run in parallel (independent new/modified files)
- T013, T014 can run in parallel (different appsettings files)
- T017, T019, T020 can run in parallel (different UI/CSS files)
- T011 depends on T006, T007, T008 (store uses TableNames, ApplicationUser, IndexEntity)
- T015 depends on T006–T012 (Program.cs registers all entities, store, hub)
- T018 depends on T015, T016, T017 (layout uses Fluent UI + DI services)
- T021, T022 depend on T006–T010, T012 (services use entities and hub context)
- T023 depends on T007 (must know new ApplicationUser shape)

Across User Stories (after Phase 2 complete):
- US1 (T024–T025) and US2 (T026–T027) can run in parallel
- US1 (T024–T025) and US4 (T030) can run in parallel
- US2 (T026–T027) and US4 (T030) can run in parallel
- US3 (T028–T029) must wait for US2 (T026) to complete

---

## Parallel Example: Phase 2 Foundational

```
Batch 1 (parallel — independent new files, no cross-dependencies):
  T006: Create TableNames.cs
  T007: Rewrite ApplicationUser.cs
  T008: Create IndexEntity.cs
  T009: Create GameEntity.cs
  T010: Create GamePlayerEntity.cs
  T012: Create BoxCarsHub.cs
  T016: Update _Imports.razor

Batch 2 (parallel — depends on Batch 1 entities being defined):
  T011: Implement TableStorageUserStore (needs T006, T007, T008)
  T013: Update appsettings.json
  T014: Update appsettings.Development.json
  T017: Update App.razor
  T019: Update MainLayout.razor.css
  T020: Update app.css

Batch 3 (sequential — integrates all prior work):
  T015: Rewrite Program.cs (needs T006–T012)

Batch 4 (parallel — depends on Program.cs + entity definitions):
  T018: Transform MainLayout.razor (needs T015, T016)
  T021: Create PlayerProfileService (needs T006–T010)
  T022: Create GameService (needs T006–T010, T012)

Batch 5 (sequential — needs new ApplicationUser shape finalized):
  T023: Audit Account infrastructure (needs T007)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (remove EF Core, add new packages)
2. Complete Phase 2: Foundational (entities, store, services, layout, hub)
3. Complete Phase 3: User Story 1 (landing page + login redirect)
4. **STOP and VALIDATE**: Register a new account → login → see /dashboard route works
5. Deploy/demo if ready — authentication flow is the MVP gate

### Incremental Delivery

1. Setup + Foundational → Infrastructure ready
2. Add US1 → Landing page + auth flow → **MVP!**
3. Add US2 → Dashboard with profile, stats, game availability → Primary experience
4. Add US3 → Create/join game flow → Game entry point established
5. Add US4 → Profile settings page → Shell complete
6. Polish → Validation, cleanup, quickstart verification

### Sequential Execution (Single Agent)

Phase 1 → Phase 2 → Phase 3 (US1) → Phase 4 (US2) → Phase 5 (US3) → Phase 6 (US4) → Phase 7

---

## Notes

- No test tasks generated — testing not explicitly requested for this feature
- Azure Table Storage entities use ETag optimistic concurrency throughout
- All service methods are async with CancellationToken propagation per constitution
- LINQ extension-method syntax per constitution coding conventions
- Table auto-creation in Program.cs startup ensures idempotent first run via CreateTableIfNotExistsAsync
- Bootstrap fully removed; Fluent UI reboot.css is the only CSS reset
- Identity scaffold pages (Account/) are preserved — only infrastructure files updated for compatibility
- Mono table naming convention: all tables use `<PluralObjectName>Table` per constitution
