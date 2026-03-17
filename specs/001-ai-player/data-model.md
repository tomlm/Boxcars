# Data Model: AI-Controlled Player Turns

## Entity Overview

The feature introduces one durable global entity for reusable bot definitions, one durable per-game assignment entity embedded in authoritative game state, and transient decision artifacts used only during turn resolution.

```text
BotStrategyDefinition (global, BotsTable)
        │
        └── referenced by ──> BotAssignment (per game / per player)
                                   │
                                   ├── produces ──> BotDecisionContext (transient)
                                   ├── produces ──> BotDecisionResolution (transient)
                                   └── records ──> RecordedBotAction (durable game history)
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

**Validation rules**:

- `Name` is required, trimmed, and must be unique enough for users to distinguish choices in the dashboard.
- `StrategyText` may be blank.
- Create, update, and delete operations require an authenticated user.
- Updates must include the latest ETag or fail with a concurrency conflict.

## BotAssignment

**Purpose**: Active or recently cleared assignment connecting a disconnected controlled player seat to a global bot definition.

| Field | Type | Description |
|---|---|---|
| `GameId` | `string` | Owning game identifier. |
| `PlayerUserId` | `string` | Disconnected player seat receiving bot actions. |
| `ControllerUserId` | `string` | User who currently holds delegated control and performed the assignment. |
| `BotDefinitionId` | `string` | Live reference to the global bot definition. |
| `AssignedUtc` | `DateTimeOffset` | Assignment timestamp. |
| `ClearedUtc` | `DateTimeOffset?` | End timestamp when the assignment is released, invalidated, or superseded. |
| `Status` | `string` | `Active`, `Cleared`, `MissingDefinition`, or `DisconnectedController`. |
| `ClearReason` | `string?` | Reason for clearing, such as reconnect, release, deletion, or reassignment. |

**Storage mapping**:

- Persisted inside the authoritative game record/snapshot in `GamesTable`.
- Only one active assignment may exist for a given `(GameId, PlayerUserId)` pair.

**Validation rules**:

- Assignment is allowed only when the player is disconnected and currently under delegated control.
- `ControllerUserId` must match the current delegated controller at the time of assignment.
- `BotDefinitionId` must resolve to an existing bot definition.
- Assignment is cleared immediately when the original player reconnects or delegated control is released.

## BotDecisionContext

**Purpose**: Sanitized per-phase authoritative snapshot sent to the bot decision service.

| Field | Type | Description |
|---|---|---|
| `GameId` | `string` | Current game identifier. |
| `PlayerUserId` | `string` | Represented player. |
| `Phase` | `string` | `PickRegion`, `Purchase`, or `Auction`. |
| `TurnNumber` | `int` | Current authoritative turn index. |
| `BotName` | `string` | Current display name from the resolved bot definition. |
| `StrategyText` | `string` | Current strategy text from the resolved bot definition. |
| `GameStatePayload` | `string` | Serialized phase-specific authoritative state summary. |
| `LegalOptions` | `IReadOnlyList<BotLegalOption>` | Explicit legal choices available right now. |
| `TimeoutUtc` | `DateTimeOffset` | Latest time the external decision is considered valid. |

**Validation rules**:

- `LegalOptions` cannot be empty for AI-backed phases.
- If exactly one legal option exists, external AI is skipped and the sole option is committed directly.
- `GameStatePayload` must omit secrets and redundant noise not needed for legal decision making.

## BotLegalOption

**Purpose**: Canonical legal action representation used for prompting and validation.

| Field | Type | Description |
|---|---|---|
| `OptionId` | `string` | Stable identifier used in prompts and parsing. |
| `OptionType` | `string` | Domain category such as `Region`, `PurchaseRailroad`, `NoPurchase`, `Bid`, or `Pass`. |
| `DisplayText` | `string` | Human-readable summary for debugging and prompt clarity. |
| `Payload` | `string` | Compact serialized action payload required for server commit. |

## BotDecisionResolution

**Purpose**: Result of bot decision processing before commit.

| Field | Type | Description |
|---|---|---|
| `GameId` | `string` | Current game identifier. |
| `PlayerUserId` | `string` | Represented player. |
| `Phase` | `string` | Phase being resolved. |
| `SelectedOptionId` | `string` | Option chosen for commit. |
| `Source` | `string` | `OpenAI`, `OnlyLegalChoice`, `Fallback`, or `DeterministicSell`. |
| `FallbackReason` | `string?` | Timeout, parse failure, stale state, missing bot definition, or invalid choice. |
| `ResolvedUtc` | `DateTimeOffset` | Resolution timestamp. |

## SellImpactEvaluation

**Purpose**: Deterministic scoring record for forced railroad sales.

| Field | Type | Description |
|---|---|---|
| `RailroadId` | `string` | Candidate railroad to sell. |
| `AccessDeltaScore` | `int` | Relative negative effect on network access. Lower magnitude is better. |
| `MonopolyDeltaScore` | `int` | Relative negative effect on monopoly position. Lower magnitude is better. |
| `TieBreakerKey` | `string` | Stable secondary ordering key, such as railroad identifier. |
| `CompositeRank` | `string` | Deterministic rank representation used to select the winner. |

**Validation rules**:

- Ranking must be deterministic for the same authoritative state.
- Equal-impact candidates must resolve through a stable tie-breaker.

## RecordedBotAction

**Purpose**: Durable game-history entry for actions committed on behalf of the disconnected player.

| Field | Type | Description |
|---|---|---|
| `GameId` | `string` | Owning game. |
| `PlayerUserId` | `string` | Player on whose behalf the action was taken. |
| `ActionType` | `string` | Game action category. |
| `ActionPayload` | `string` | Persisted action details. |
| `BotDefinitionId` | `string?` | Resolved bot definition used for the action, if applicable. |
| `DecisionSource` | `string` | `OpenAI`, `OnlyLegalChoice`, `Fallback`, or `DeterministicSell`. |
| `RecordedUtc` | `DateTimeOffset` | Commit timestamp. |

**Relationship notes**:

- `RecordedBotAction` is part of the normal authoritative game history rather than a separate audit table.
- History entries are broadcast exactly as other game actions are broadcast.

## State Transitions

### Bot assignment lifecycle

```text
Unassigned
  -> AssignedActive
AssignedActive
  -> ClearedByRelease
  -> ClearedByReconnect
  -> ClearedByReassignment
  -> MissingDefinition
MissingDefinition
  -> AssignedActive
  -> ClearedByRelease
```

### Decision lifecycle

```text
PhaseReached
  -> SoleLegalChoiceCommitted
  -> AwaitingOpenAI
AwaitingOpenAI
  -> OpenAIChoiceValidated
  -> FallbackSelected
OpenAIChoiceValidated
  -> RecordedAndBroadcast
FallbackSelected
  -> RecordedAndBroadcast
```