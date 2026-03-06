# Contract: Storage and Game Flow

## Scope

Defines externally observable behavior for authentication profile sync, create-game persistence, event persistence, and reconnect restore for the `Redo Table Storage` feature.

## Storage Contracts

### UsersTable / UserEntity

- Table name: `UsersTable`
- Entity type: `UserEntity`
- Key contract:
  - `PartitionKey = USER`
  - `RowKey = {Email}`
- Required fields: `Email`, `Name`
- Optional fields: `Nickname`, `Thumbnail`, additional profile properties

Behavior:
- On successful authentication resolution, system creates missing user record or returns existing record for email key.

### GamesTable / GameEntity

- Table name: `GamesTable`
- Entity type: `GameEntity`
- Key contract:
  - `PartitionKey = {GameId}`
  - `RowKey = GAME`
- Required fields: `GameId`, immutable `Settings`, ordered `Players` list (`User + Color`)

Behavior:
- Created exactly once when creator confirms Create Game.
- Represents immutable setup for that game.

### GamesTable / GameEventEntity

- Table name: `GamesTable`
- Entity type: `GameEventEntity`
- Key contract:
  - `PartitionKey = {GameId}`
  - `RowKey = Event_{UtcSortableTick}`
- Required fields: `EventKind`, `EventData`, `SerializedGameState`, `OccurredUtc`, actor metadata

Behavior:
- A new event entity is persisted for every UI-triggered game action processed by the game engine.
- Event ordering is determined by row key sort order.

## Interaction Contracts

### Create Game flow

1. User navigates `Dashboard -> Create Game`.
2. Creator assigns user + color per slot.
3. Validation must reject duplicate users, duplicate colors, or incomplete required slots.
4. On success, `GameEntity` is persisted.
5. User is navigated to Game page where Start Game action is present.

### Action processing flow

For each game action initiated from UI:
1. Game engine validates action server-side.
2. Game engine persists corresponding `GameEventEntity`.
3. Only after persistence success does game engine publish updates to connected clients.

Failure contract:
- If persistence fails, action is not broadcast as committed state.

### Reconnect flow

1. Load game partition from `GamesTable`.
2. Read immutable setup from `GameEntity`.
3. Read latest snapshot from highest-order `GameEventEntity`.
4. Restore engine state from snapshot and present ordered event timeline.

## Non-scope / De-scoped contract

- Storage flows in this feature do not require additional active tables beyond `UsersTable` and `GamesTable`.
- Legacy helper/index tables may be removed or de-scoped from active read/write paths for this feature.
