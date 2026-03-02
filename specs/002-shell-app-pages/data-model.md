# Data Model: BoxCars Shell Application Pages

**Feature**: `002-shell-app-pages`  
**Date**: 2026-02-27  
**Source**: `/specs/002-shell-app-pages/spec.md`, `/specs/002-shell-app-pages/research.md`  
**Storage**: Azure Table Storage (Azurite locally)

---

## Tables

### UsersTable (mono table)

Stores Identity user accounts and player profile data in a single entity.

**Partition strategy**: All users in a single partition `"USER"` (low volume — player count, not request count).

| Property | Type | Role | Notes |
|----------|------|------|-------|
| PartitionKey | string | Partition | Always `"USER"` |
| RowKey | string | Row key | User ID (GUID string) |
| Timestamp | DateTimeOffset? | System | Auto-managed |
| ETag | ETag | System | Optimistic concurrency |
| Email | string | Identity | User's email address |
| NormalizedEmail | string | Identity | Uppercased email for lookups |
| UserName | string | Identity | Same as Email |
| NormalizedUserName | string | Identity | Uppercased username for lookups |
| PasswordHash | string | Identity | Hashed password |
| SecurityStamp | string | Identity | Changes on credential update |
| EmailConfirmed | bool | Identity | Email verification status |
| LockoutEnd | DateTimeOffset? | Identity | Lockout expiry (nullable) |
| LockoutEnabled | bool | Identity | Whether lockout is enabled |
| AccessFailedCount | int | Identity | Failed login attempts |
| Nickname | string | Profile | Display name; default = email prefix |
| NormalizedNickname | string | Profile | Uppercased nickname for uniqueness checks |
| ThumbnailUrl | string | Profile | External image URL; default = placeholder |

**C# entity**: `ApplicationUser : ITableEntity`

---

### UserEmailIndexTable (mono table)

Secondary index for looking up users by normalized email (table storage has no secondary indexes).

| Property | Type | Role | Notes |
|----------|------|------|-------|
| PartitionKey | string | Partition | Always `"EMAIL_INDEX"` |
| RowKey | string | Row key | Normalized email |
| UserId | string | Data | Points to RowKey in `UsersTable` |

**Purpose**: `FindByEmailAsync` needs O(1) lookup. Without this, every email lookup scans the entire `UsersTable`.

**C# entity**: `IndexEntity : ITableEntity`

---

### UserNameIndexTable (mono table)

Secondary index for looking up users by normalized username.

| Property | Type | Role | Notes |
|----------|------|------|-------|
| PartitionKey | string | Partition | Always `"USERNAME_INDEX"` |
| RowKey | string | Row key | Normalized username |
| UserId | string | Data | Points to RowKey in `UsersTable` |

**Purpose**: `FindByNameAsync` needs O(1) lookup.

**C# entity**: `IndexEntity : ITableEntity` (shared with email index)

---

### NicknameIndexTable (mono table)

Secondary index for enforcing globally unique nicknames.

| Property | Type | Role | Notes |
|----------|------|------|-------|
| PartitionKey | string | Partition | Always `"NICKNAME_INDEX"` |
| RowKey | string | Row key | Normalized nickname (uppercased) |
| UserId | string | Data | Points to RowKey in `UsersTable` |

**Purpose**: FR-007 requires globally unique nicknames. Insert-or-fail pattern: if `AddEntityAsync` returns 409 Conflict, the nickname is taken. On nickname change, delete old index entry then insert new one.

**C# entity**: `IndexEntity : ITableEntity` (shared with other indexes)

---

### GamesTable (mono table)

Stores game instances.

**Partition strategy**: PartitionKey = game status string. When status changes, the row is deleted and re-inserted with the new PartitionKey (table storage cannot update partition keys in place).

| Property | Type | Role | Notes |
|----------|------|------|-------|
| PartitionKey | string | Partition | `"ACTIVE"`, `"COMPLETED"`, or `"CANCELLED"` |
| RowKey | string | Row key | Game ID (GUID string) |
| Timestamp | DateTimeOffset? | System | Auto-managed |
| ETag | ETag | System | Optimistic concurrency |
| CreatorId | string | Data | User ID of the player who created the game |
| MaxPlayers | int | Data | Configured max players (2–6, Rail Baron standard) |
| CurrentPlayerCount | int | Data | Current number of joined players |
| CreatedAt | DateTime | Data | UTC creation time |

**C# entity**: `GameEntity : ITableEntity`

---

### GamePlayersTable (mono table)

Tracks which players are in which games.

**Partition strategy**: PartitionKey = game ID. All players for a game are in the same partition for efficient per-game queries.

| Property | Type | Role | Notes |
|----------|------|------|-------|
| PartitionKey | string | Partition | Game ID (GUID string) |
| RowKey | string | Row key | Player ID (User ID) |
| Timestamp | DateTimeOffset? | System | Auto-managed |
| ETag | ETag | System | Optimistic concurrency |
| JoinedAt | DateTime | Data | UTC join time |

**C# entity**: `GamePlayerEntity : ITableEntity`

---

### PlayerActiveGameIndexTable (mono table)

Index for quickly checking if a player has an active game (enforces one-active-game constraint per FR-010).

| Property | Type | Role | Notes |
|----------|------|------|-------|
| PartitionKey | string | Partition | Always `"ACTIVE_GAME"` |
| RowKey | string | Row key | Player ID (User ID) |
| GameId | string | Data | Active game ID |

**Purpose**: Dashboard needs O(1) lookup: "Does this player have an active game?" Also enforces the one-active-game-per-player constraint — if a row exists, the player already has an active game.

**C# entity**: `IndexEntity : ITableEntity` (shared with other indexes — has UserId property, here repurposed: RowKey=playerId, GameId stored instead of UserId)

> **Note**: This index reuses the `IndexEntity` shape but the `UserId` property stores the GameId. Alternatively, create a dedicated `ActiveGameIndexEntity` for clarity. The implementation decision is deferred to task execution.

---

## State Transitions

### Game Lifecycle

```
[Created] → ACTIVE → COMPLETED
                   → CANCELLED
```

- Game created with PartitionKey = `"ACTIVE"` in `GamesTable`, `CurrentPlayerCount = 1`
- Creator added to `GamePlayersTable` (PartitionKey=gameId, RowKey=creatorId)
- Creator added to `PlayerActiveGameIndexTable` (RowKey=creatorId, GameId=gameId)
- On status change: delete from `GamesTable` with old PK, insert with new PK; delete all player entries from `PlayerActiveGameIndexTable`
- Status transitions are one-way (no reactivation)

### Player Joins Game

1. Check `PlayerActiveGameIndexTable` — if row exists for player, reject (already in a game)
2. Read game entity from `GamesTable` — check `CurrentPlayerCount < MaxPlayers`
3. Insert row in `GamePlayersTable` (PartitionKey=gameId, RowKey=playerId)
4. Insert row in `PlayerActiveGameIndexTable` (RowKey=playerId, GameId=gameId)
5. Increment `CurrentPlayerCount` on game entity (use ETag for optimistic concurrency)
6. Broadcast `DashboardStateRefreshed` to all clients via SignalR
7. If ETag conflict on game update → delete inserted rows, return join conflict

### Player Profile Provisioning

```
[Registration] → Set Nickname = email prefix, ThumbnailUrl = default
              → Insert user in UsersTable
              → Insert email index, username index, nickname index
```

### Nickname Update

1. Normalize new nickname (uppercase)
2. Try insert new `NicknameIndexTable` entry — if 409, return "nickname taken"
3. Update `Nickname` and `NormalizedNickname` on `UsersTable` entity (ETag)
4. Delete old `NicknameIndexTable` entry
5. If step 3 fails, delete the new index entry (rollback)

---

## Dashboard Decision Logic

1. Query `PlayerActiveGameIndexTable` for PartitionKey=`"ACTIVE_GAME"`, RowKey=currentPlayerId
2. If row exists → player has an active game → Show "Resume Game" with GameId
3. If no row → Query `GamesTable` where PartitionKey=`"ACTIVE"` for games with `CurrentPlayerCount < MaxPlayers`
4. If joinable game found → Show "Join Game"
5. If none → Show "Create Game"

---

## Concurrency & Consistency

- **Optimistic concurrency**: All table operations use ETags. Join conflicts detected via ETag mismatch on game entity.
- **Index consistency**: Index tables and main tables are updated together. If a write to an index fails after the main entity succeeds, a cleanup path handles removal (best-effort rollback).
- **No transactions across tables**: Azure Table Storage only supports batch operations within a single partition of a single table. Cross-table consistency is eventual — acceptable for this shell feature where conflicts are rare and resolved via dashboard refresh + SignalR notifications.
