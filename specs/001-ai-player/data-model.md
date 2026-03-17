# Data Model: AI-Controlled Player Turns

## Entity Overview

```text
BotStrategyDefinition (global, BotsTable)
        │
        └── referenced by ──> BotAssignment (per game / per seat)
                                   │
                                   ├── combined with ──> SeatControllerState (per seat / current control mode)
                                   ├── produces ───────> BotDecisionContext (transient)
                                   ├── produces ───────> BotDecisionResolution (transient)
                                   └── records ────────> RecordedBotAction (durable game history)
```

## BotStrategyDefinition

**Purpose**: Global reusable bot definition managed from the dashboard.

| Field | Type | Description |
|---|---|---|
| `BotDefinitionId` | `string` | Stable unique identifier for the bot definition. |
| `Name` | `string` | User-facing bot name shown in dashboard and assignment UI. |
| `StrategyText` | `string` | Freeform strategy prompt text used to shape AI decisions. |
| `CreatedByUserId` | `string` | User ID of the creator for audit/debugging. |
| `CreatedUtc` | `DateTimeOffset` | Creation timestamp. |
| `ModifiedByUserId` | `string` | User ID of the last editor. |
| `ModifiedUtc` | `DateTimeOffset` | Last modification timestamp. |
| `ETag` | `ETag` | Azure Table optimistic concurrency token. |

**Storage mapping**:

| Property | Value |
|---|---|
| Table | `BotsTable` |
| PartitionKey | `BOT` |
| RowKey | `BotDefinitionId` |

## SeatControllerState

**Purpose**: Current control ownership for a player seat, resolved outside the engine.

| Field | Type | Description |
|---|---|---|
| `GameId` | `string` | Owning game identifier. |
| `PlayerUserId` | `string` | Seat identity bound to the player selection. |
| `ControllerMode` | `string` | `HumanDirect`, `HumanDelegated`, `AiBotSeat`, or `AiGhost`. |
| `DelegatedControllerUserId` | `string?` | Human user currently controlling the seat when in delegated-human mode. |
| `OwningHumanUserId` | `string?` | Original human seat owner for reconnect/ghost stop semantics. |
| `IsConnected` | `bool` | Current live connection status for the seat owner. |
| `BotDefinitionId` | `string?` | Current bot reference when the controller mode is AI-backed. |

**Validation rules**:

- `AiBotSeat` does not require `DelegatedControllerUserId`.
- `AiGhost` requires an owning human seat and is valid only while that owner is disconnected and ghost mode remains enabled.
- `HumanDelegated` requires `DelegatedControllerUserId`.

## BotAssignment

**Purpose**: Durable per-game record of AI ownership for a seat.

| Field | Type | Description |
|---|---|---|
| `GameId` | `string` | Owning game identifier. |
| `PlayerUserId` | `string` | Seat receiving AI-authored actions. |
| `ControllerMode` | `string` | `AiBotSeat` or `AiGhost`. |
| `ControllerUserId` | `string?` | Human who enabled ghost mode or performed the assignment, if applicable. Optional for dedicated bot seats. |
| `BotDefinitionId` | `string` | Live reference to the global bot definition. |
| `AssignedUtc` | `DateTimeOffset` | Assignment timestamp. |
| `ClearedUtc` | `DateTimeOffset?` | End timestamp when the assignment is released, invalidated, or superseded. |
| `Status` | `string` | `Active`, `Cleared`, `MissingDefinition`, or `DisconnectedController`. |
| `ClearReason` | `string?` | Reason for clearing, such as reconnect, release, deletion, or reassignment. |

**Validation rules**:

- Only one active AI assignment may exist for a given `(GameId, PlayerUserId)` pair.
- Dedicated bot seats may be active without `ControllerUserId`.
- Ghost assignments stop when the original human reconnects or ghost mode is disabled/released.

## BotDecisionContext

**Purpose**: Sanitized per-phase authoritative snapshot sent to the bot decision service.

| Field | Type | Description |
|---|---|---|
| `GameId` | `string` | Current game identifier. |
| `PlayerUserId` | `string` | Represented seat. |
| `Phase` | `string` | `PickRegion`, `Purchase`, or `Auction`. |
| `TurnNumber` | `int` | Current authoritative turn index. |
| `BotName` | `string` | Current display name from the resolved bot definition. |
| `StrategyText` | `string` | Current strategy text from the resolved bot definition. |
| `GameStatePayload` | `string` | Serialized phase-specific authoritative state summary. |
| `LegalOptions` | `IReadOnlyList<BotLegalOption>` | Explicit legal choices available right now. |
| `TimeoutUtc` | `DateTimeOffset` | Latest time the external decision is considered valid. |

## BotDecisionResolution

**Purpose**: Result of bot decision processing before commit.

| Field | Type | Description |
|---|---|---|
| `GameId` | `string` | Current game identifier. |
| `PlayerUserId` | `string` | Represented seat. |
| `Phase` | `string` | Phase being resolved. |
| `SelectedOptionId` | `string` | Option chosen for commit. |
| `Source` | `string` | `OpenAI`, `OnlyLegalChoice`, `Fallback`, or deterministic phase-specific source. |
| `FallbackReason` | `string?` | Timeout, parse failure, stale state, or invalid choice. |
| `ResolvedUtc` | `DateTimeOffset` | Resolution timestamp. |

## RecordedBotAction

**Purpose**: Durable game-history entry for actions committed on behalf of an AI-controlled seat.

| Field | Type | Description |
|---|---|---|
| `GameId` | `string` | Owning game. |
| `PlayerUserId` | `string` | Seat on whose behalf the action was taken. |
| `ActionType` | `string` | Game action category. |
| `ActionPayload` | `string` | Persisted action details. |
| `BotDefinitionId` | `string?` | Resolved bot definition used for the action, if applicable. |
| `DecisionSource` | `string` | `OpenAI`, `OnlyLegalChoice`, `Fallback`, or deterministic sell/auction source. |
| `RecordedUtc` | `DateTimeOffset` | Commit timestamp. |
| `ActorUserId` | `string` | Server-owned AI actor identity, not a delegated human user. |

## State Transitions

### Seat control lifecycle

```text
HumanDirect
  -> HumanDelegated
  -> AiGhost
HumanDelegated
  -> HumanDirect
  -> AiGhost
AiGhost
  -> HumanDirect        (reconnect)
  -> HumanDelegated     (ghost disabled, delegated control retained)
AiBotSeat
  -> AiBotSeat          (normal steady state)
  -> Cleared / Reassigned
```

### Assignment lifecycle

```text
Unassigned
  -> AssignedActive(AiBotSeat)
  -> AssignedActive(AiGhost)
AssignedActive
  -> ClearedByRelease
  -> ClearedByReconnect
  -> ClearedByReassignment
  -> MissingDefinition
```