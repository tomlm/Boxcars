# Data Model: Redo Table Storage

## Entity: UserEntity (`UsersTable`)

- Purpose: Represents an authenticated user profile used by game creation and player presentation.
- Keys:
  - PartitionKey: `USER`
  - RowKey: `{Email}`
- Core fields:
  - `Email` (required, unique per user)
  - `Name` (required)
  - `Nickname` (optional)
  - `Thumbnail` (optional)
  - Additional profile properties as needed by authentication/profile UX
- Validation rules:
  - Email is required and normalized before keying.
  - RowKey must equal normalized email.
  - Missing optional profile fields do not block user selection.
- State transitions:
  - `Absent -> Present` on first authentication or seeded initialization.
  - `Present -> Updated` on profile refresh.

## Entity: GameEntity (`GamesTable`)

- Purpose: Immutable game setup/configuration created at game creation.
- Keys:
  - PartitionKey: `{GameId}`
  - RowKey: `GAME`
- Core fields:
  - `GameId` (required)
  - `Settings` (required, immutable after create)
  - `Players` (required ordered list of `{UserId/Email, Color}`)
  - `CreatedBy` (required)
  - `CreatedUtc` (required)
- Validation rules:
  - Exactly one `GameEntity` per `GameId` partition.
  - Player assignments must satisfy slot completeness and uniqueness constraints.
  - Stored player order is authoritative for turn-order initialization.
- State transitions:
  - `Absent -> Created` when creator confirms Create Game.
  - No in-place mutation of immutable setup fields.

## Entity: GameEventEntity (`GamesTable`)

- Purpose: Represents one chronological game action plus a serialized mutable state snapshot for resume/history.
- Keys:
  - PartitionKey: `{GameId}`
  - RowKey: `Event_{UtcSortableTick}`
- Core fields:
  - `GameId` (required)
  - `EventKey`/`RowKey` (required, sortable)
  - `EventKind` (required; e.g., `StartGame`, `DestinationAssigned`, `Move`, `DestinationReached`)
  - `EventData` (required payload for event details)
  - `SerializedGameState` (required snapshot of mutable engine state)
  - `OccurredUtc` (required)
  - `CreatedBy` (required actor identity)
- Validation rules:
  - Event key must be unique within game partition.
  - Event order is determined by sortable row key.
  - Snapshot payload must deserialize to a valid engine state.
- State transitions:
  - `None -> Persisted` when engine processes a UI action.
  - `Persisted -> Broadcast` only after persistence succeeds.

## Relationships

- One `UserEntity` can appear in zero or many `GameEntity.Players` entries.
- One `GameEntity` has zero or many `GameEventEntity` records in the same `{GameId}` partition.
- Reconnect load path reads `GameEntity` + latest `GameEventEntity.SerializedGameState` + ordered event history for timeline UI.

## Invariants

- Only `UsersTable` and `GamesTable` are active in this feature scope.
- UI-triggered game mutations must flow through engine persistence path.
- Action history and reconnect state must be derivable from `GamesTable` partition data alone.
