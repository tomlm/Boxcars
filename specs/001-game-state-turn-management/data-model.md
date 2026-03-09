# Data Model: Game State and Turn Management Cleanup

**Feature**: 001-game-state-turn-management  
**Date**: 2026-03-07

## Entity Relationship Overview

```text
GameEventEntity
├── EventData
├── SerializedGameState
└── OccurredUtc

GameState
├── ActivePlayerIndex
├── Turn : TurnState
└── Players : PlayerState[]
    ├── ActiveRoute : RouteState?
    ├── UsedSegments : string[]
    ├── CurrentNodeId
    └── RouteProgressIndex

BoardTurnViewState
├── ActivePlayer
├── IsCurrentUserActivePlayer
├── MovementAllowance
├── MovementRemaining
├── SelectedRoutePreview
├── TraveledTripHistory
└── ArrivalResolution?

PlayerControlBinding
├── UserId
├── PlayerIndex
└── DisplayName
```

## Entities

### Game Event Record

Represents one persisted game mutation in `GamesTable` and is the reload source for the board.

| Field | Type | Source | Description |
|------|------|--------|-------------|
| `GameId` | `string` | Existing | Game partition identifier |
| `EventKind` | `string` | Existing | Mutation category such as `ChooseRoute`, `Move`, `EndTurn` |
| `EventData` | `string` | Existing | Action payload serialized for audit and targeted UI restore |
| `SerializedGameState` | `string` | Existing | Full post-action snapshot used for reconnect/reload restore |
| `OccurredUtc` | `DateTimeOffset` | Existing | Event ordering timestamp |
| `CreatedBy` | `string` | Existing | Acting participant identifier |

**Validation rules**:
- Row ordering remains append-only and sortable by row key.
- Every committed action that changes game state must persist both `EventData` and `SerializedGameState` before broadcast.

### Board Turn View State

Represents the UI-ready turn state derived from the latest snapshot plus current player binding.

| Field | Type | Description |
|------|------|-------------|
| `ActivePlayerIndex` | `int` | Current turn owner from restored snapshot |
| `ActivePlayerName` | `string` | Display label for board and status components |
| `IsCurrentUserActivePlayer` | `bool` | Whether the current authenticated participant can act |
| `TurnPhase` | `string` | Current phase shown on the board |
| `MovementAllowance` | `int` | Total movement granted by the current roll |
| `MovementRemaining` | `int` | Remaining movement after committed and/or previewed selections |
| `PreviewFee` | `int` | Estimated route fee for current selected path |
| `SelectedRouteNodeIds` | `IReadOnlyList<string>` | Current unfinished turn selection shown on the board |
| `SelectedRouteSegmentKeys` | `IReadOnlyList<string>` | Visual segment selection for map restore |
| `TraveledSegmentKeys` | `IReadOnlyList<string>` | Completed current-trip segment history for X markers |
| `CanEndTurn` | `bool` | Derived flag that becomes true only when the turn is legally completable |

**State transitions**:
- `Reloaded` → derived from latest persisted event.
- `PreviewingMove` → active player changes route selection locally.
- `ReadyToCommit` → no movement remains and server preconditions are satisfied.
- `ArrivalPendingPrompt` → destination reached and arrival UI prompt is active.
- `WaitingForNextPlayer` → turn ended and next player becomes active.

### Player Control Binding

Represents the stable relationship between the authenticated user and the player slot they control.

| Field | Type | Description |
|------|------|-------------|
| `UserId` | `string` | Authenticated participant identifier |
| `PlayerIndex` | `int` | Assigned player slot in turn order |
| `DisplayName` | `string` | User-facing label only |
| `Color` | `string` | Player color used for UI rendering |

**Validation rules**:
- Each `UserId` maps to at most one player slot per game.
- Only the binding for `GameState.ActivePlayerIndex` may issue mutating turn actions.

### Turn Movement Preview

Represents the active player's in-progress path selection before the move is committed.

| Field | Type | Description |
|------|------|-------------|
| `NodeIds` | `IReadOnlyList<string>` | Ordered preview route nodes |
| `SegmentKeys` | `IReadOnlyList<string>` | Canonical segment identifiers for visual restore |
| `MoveCount` | `int` | Number of previewed steps |
| `FeeEstimate` | `int` | Client-visible fee estimate for the selected path |
| `ExhaustsMovement` | `bool` | Whether the current preview uses the full legal movement allowance |

**Validation rules**:
- Preview cannot exceed movement remaining.
- Preview cannot include illegal non-connected or non-owned transitions beyond engine/map rules.
- Invalid preview attempts leave the prior legal preview unchanged.

### Arrival Resolution

Represents the UI-facing summary of an engine-resolved destination arrival.

| Field | Type | Description |
|------|------|-------------|
| `PlayerIndex` | `int` | Arriving player |
| `DestinationCityName` | `string` | City reached |
| `PayoutAmount` | `int` | Cash awarded |
| `CashAfterPayout` | `int` | Updated player cash after arrival |
| `PurchaseOpportunityAvailable` | `bool` | Whether the next prompt should offer a buy decision |
| `Message` | `string` | User-facing arrival summary |

## Relationships

- `GameEventEntity.SerializedGameState` restores one `GameState`.
- `GameState.ActivePlayerIndex` joins with `PlayerControlBinding.PlayerIndex` to determine action authority.
- `GameState.Players[].ActiveRoute` and `GameState.Players[].UsedSegments` project into `BoardTurnViewState.SelectedRouteSegmentKeys` and `BoardTurnViewState.TraveledSegmentKeys`.
- `ArrivalResolution` is derived from engine state transitions and displayed by the board shell.