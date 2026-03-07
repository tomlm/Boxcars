# Quickstart: Redo Table Storage

## Prerequisites

- .NET 8 SDK installed.
- Azure Table Storage connection configured for the app.
- Feature branch checked out: `002-redo-table-storage`.

## 1) Build and run

- Build: `dotnet build Boxcars.slnx`
- Run app (from `src/Boxcars`): `dotnet run`

## 2) Validate user-table bootstrap and auth profile path (P3)

1. Ensure table initialization runs for current environment.
2. Confirm `UsersTable` contains Beatles mock users:
   - `paul@beatles.com`
   - `ringo@beatles.com`
   - `george@beatles.com`
   - `john@beatles.com`
3. Authenticate as any user and verify profile create/lookup uses `PartitionKey=USER`, `RowKey={Email}`.

Expected result:
- User profile is retrievable for selection in Create Game.

## 3) Validate create-game flow and immutable setup persistence (P1)

1. From Dashboard, navigate to Create Game page.
2. For each required slot, select one player and one color.
3. Trigger validation checks:
   - duplicate user assignment blocked
   - duplicate color assignment blocked
   - incomplete slots blocked
4. Click Create Game.

Expected result:
- A `GameEntity` is written to `GamesTable` with `PartitionKey={GameId}`, `RowKey=GAME`.
- App navigates to Game page and Start Game action is available.

## 4) Validate event persistence and reconnect restore (P2)

1. Start game and perform several actions that produce events.
2. For each action, verify corresponding `GameEventEntity` with:
   - sortable `RowKey=Event_{UtcSortableTick}`
   - event kind/data
   - serialized mutable game snapshot
3. Simulate reconnect (refresh/new session).

Expected result:
- Game resumes from latest persisted snapshot.
- Action history panel displays events in chronological order.

SC-003 measurement protocol:
- Run 20 reconnect trials after mixed gameplay actions.
- Count successful resumes where latest persisted state is restored with no manual repair.
- Pass threshold: at least 19/20 successful resumes (95%).

## 5) Regression checks for storage scope

1. Inspect active runtime writes for user/game flows.
2. Verify no removed/de-scoped legacy tables are still required for this feature path.

Expected result:
- Only `UsersTable` and `GamesTable` participate in the specified flows.

## 6) First-attempt create-game usability sample (SC-004)

1. Use 10 independent create-game attempts with seeded users.
2. For each attempt, perform full flow once: Dashboard -> Create Game -> assign players/colors -> Create Game.
3. Record whether the user succeeds without retrying slot/color assignments.

Expected result:
- At least 9/10 first-attempt successes (90%).
