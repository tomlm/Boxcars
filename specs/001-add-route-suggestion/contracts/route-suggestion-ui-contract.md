# Contract: Route Suggestion UI + Calculation

## Purpose

Define interaction and response contract for selecting a destination city and rendering the cheapest suggested route on the map board.

## Surface

- **Consumer**: `MapBoard` page interaction layer
- **Provider**: Route suggestion service logic (`MapRouteService` + ownership/profile context integration)

## Input Contract

### Destination Selection Input

- **Trigger**: Right-click on city target in Route mode
- **Action**: `Set as destination`
- **Payload**:
  - `cityName` (string)
  - `nodeId` (string)
  - `playerId` (string)

### Route Suggestion Request

- **Trigger**: Destination selection changed or explicit recompute
- **Payload**:
  - `playerId` (string)
  - `startNodeId` (string)
  - `destinationNodeId` (string)
  - `movementType` (`TwoDie` | `ThreeDie`)
  - `ownershipLookup` (railroad -> owner category)

## Output Contract

### Success

```json
{
  "status": "success",
  "route": {
    "startNodeId": "string",
    "destinationNodeId": "string",
    "nodeIds": ["string"],
    "totalTurns": 0,
    "totalCost": 0,
    "segments": [
      {
        "fromNodeId": "string",
        "toNodeId": "string",
        "railroadIndex": 0,
        "ownershipCategory": "Unowned|OwnedByPlayer|OwnedByOtherPlayer",
        "turns": 1,
        "costPerTurn": 1000,
        "totalCost": 1000
      }
    ]
  },
  "highlights": [
    {
      "nodeId": "string",
      "x": 0,
      "y": 0,
      "color": "string",
      "radius": 0
    }
  ]
}
```

### No Route Available

```json
{
  "status": "no_route",
  "message": "No valid route available to destination.",
  "route": null,
  "highlights": []
}
```

### Error

```json
{
  "status": "error",
  "message": "string"
}
```

## Behavioral Guarantees

- Cost model:
  - Unowned railroad turn: `$1000`
  - Player-owned railroad turn: `$1000`
  - Other-player-owned railroad turn: `$5000`
- Best route is always the minimum `totalCost` valid route.
- Equal-cost routes resolve deterministically.
- Highlight output contains one point-circle per route node in active player color.
- New suggestion replaces previous highlight set atomically.

## Compatibility Guarantees

- Existing route-node revisit behavior remains unchanged (backtrack before railroad toggle on revisits).
- Route-node railroad menu behavior remains append-first from current endpoint when available.
