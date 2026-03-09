# Contract: Turn Management UI and Action Flow

## Purpose

Define the user-facing and server-facing contract for restoring board state, previewing movement, committing turn actions, and enforcing per-player turn ownership.

## Surface

- **Consumers**: `GameBoard` page, turn-status/action components, map interaction layer
- **Providers**: `GameEngineService`, `IGameEngine`, persisted `GameEventEntity` snapshots, SignalR game updates

## Input Contract

### Board Load / Reload Request

- **Trigger**: Navigate to a game board, reconnect to an active game, or receive a state refresh after SignalR reconnect.
- **Required context**:
  - `gameId` (string)
  - authenticated `userId` (string)

### Move Preview Input

- **Trigger**: Active player selects or deselects route segments in move mode.
- **Payload**:
  - `gameId` (string)
  - `playerIndex` (int)
  - `selectedNodeIds` (ordered string array)
  - `selectedSegmentKeys` (ordered string array)

Behavior:
- Preview updates immediately in the UI.
- Preview does not become committed travel history until the move action is accepted by the server.

### Commit Turn Actions

- **Supported actions**:
  - `ChooseRoute`
  - `Move`
  - `DeclinePurchase` or purchase-related arrival follow-up
  - `EndTurn`
- **Required payload**:
  - `gameId` (string)
  - `userId` (string)
  - `playerIndex` (int)
  - action-specific route or movement payload

Authorization rule:
- Server accepts mutating turn actions only when `userId` is bound to `playerIndex` and `playerIndex` matches the active player.

## Output Contract

### Restored Board State

```json
{
  "status": "ok",
  "activePlayerIndex": 1,
  "turnPhase": "Move",
  "movementRemaining": 3,
  "selectedRoute": {
    "nodeIds": ["12:4", "12:5", "12:6"],
    "segmentKeys": ["12:4-12:5", "12:5-12:6"],
    "feeEstimate": 5000
  },
  "traveledTripHistory": {
    "segmentKeys": ["11:9-12:0", "12:0-12:1"]
  },
  "isCurrentUserActivePlayer": true,
  "canEndTurn": false
}
```

### Action Rejected

```json
{
  "status": "rejected",
  "reason": "Only the controlling participant for the active player may perform this action.",
  "restoredBoardState": {
    "movementRemaining": 2,
    "selectedRoute": {
      "segmentKeys": ["12:4-12:5"]
    }
  }
}
```

### Arrival Resolved

```json
{
  "status": "arrival_resolved",
  "playerIndex": 1,
  "destinationCityName": "Denver",
  "payoutAmount": 18000,
  "cashAfterPayout": 72500,
  "purchaseOpportunityAvailable": true,
  "nextPhase": "Purchase"
}
```

### State Update Broadcast

- **Event**: `StateUpdated`
- **Target**: all clients joined to the game group
- **Payload**: latest serialized game snapshot mapped to the board state described above

## Behavioral Guarantees

- Board reload always reflects the latest persisted event snapshot rather than a locally reconstructed guess.
- Moves left and fee estimate refresh in the same interaction cycle as a legal segment selection change.
- Attempting to add segments after movement is exhausted leaves the prior legal route preview unchanged.
- `EndTurn` is disabled and server-rejected until the move phase is legally complete.
- Non-active players can observe current partial-turn state but cannot mutate it.
- Arrival messaging is explicit and occurs after payout has been applied by the engine.