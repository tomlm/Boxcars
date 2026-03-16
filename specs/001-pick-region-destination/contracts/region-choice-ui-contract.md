# Contract: Same-Region Destination Region Choice

## Purpose

Define the authoritative state and UI interaction contract for resolving same-region destination draws through an explicit replacement-region choice.

## Surface

- **Consumer**: `GameBoard` turn-decision surface and related board view models
- **Provider**: Authoritative engine/game-state pipeline plus `GameBoardStateMapper`

## Authoritative State Contract

### Pending Region Choice State

- **Trigger**: A destination region draw matches the region of the active player's current city.
- **Persisted Fields**:
  - `playerIndex`
  - `currentCityName`
  - `currentRegionCode`
  - `eligibleRegionCodes[]`
  - `eligibleCityCountsByRegion{}`
- **Invariants**:
  - `eligibleRegionCodes[]` excludes `currentRegionCode`
  - every eligible region has at least one valid weighted city candidate
  - final destination city remains unset until region confirmation completes

## UI Input Contract

### Region Choice Request

- **Trigger**: Player selects one region from the region-choice decision surface
- **Authorized Actor**: Active player's controlling participant only
- **Payload**:
  - `gameId` (string)
  - `actorUserId` (string)
  - `playerIndex` (int)
  - `selectedRegionCode` (string)

## UI Output Contract

### Region Choice Presentation Model

- `playerName` (string)
- `currentCityName` (string)
- `currentRegionName` (string)
- `options[]`
  - `regionCode` (string)
  - `regionName` (string)
  - `regionProbabilityPercent` (decimal)
  - `accessibleDestinationPercent` (decimal)
  - `monopolyDestinationPercent` (decimal)
  - `eligibleCityCount` (int)
- `selectedRegionCode` (string, optional)
- `canConfirm` (bool)

## Resolution Contract

- On valid confirmation, the provider performs the weighted city draw using the selected region's existing city probability table.
- The finalized destination city must belong to the chosen region.
- The pending region-choice state is cleared after the final destination assignment is persisted.
- The updated game state is broadcast to all connected clients.

## Error Contract

- If a non-controlling participant submits a region choice, the action is rejected with the same authorization semantics used for other active-turn decisions.
- If `selectedRegionCode` is not one of the eligible replacement regions, the action is rejected and the pending state remains unchanged.
- If the pending region-choice state is no longer current when a choice arrives, the action is rejected as stale.