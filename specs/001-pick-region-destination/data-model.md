# Data Model: Pick Region for Same-Region Destinations

## Entity: PendingRegionChoiceState

- **Purpose**: Represents an unresolved destination-selection branch where the active player must choose a replacement region before the final destination city can be assigned.
- **Fields**:
  - `PlayerIndex` (int, required)
  - `CurrentCityName` (string, required)
  - `CurrentRegionCode` (string, required)
  - `TriggeredByInitialRegionCode` (string, required)
  - `EligibleRegionCodes` (collection of string, required)
  - `EligibleCityCountsByRegion` (dictionary keyed by region code, required)
- **Validation**:
  - `PlayerIndex` must match the active player while this state is pending.
  - `CurrentRegionCode` must not appear in `EligibleRegionCodes`.
  - Each eligible region must have at least one valid city candidate for weighted selection.

## Entity: DestinationRegionOption

- **Purpose**: One player-selectable replacement region in the region-choice panel.
- **Fields**:
  - `RegionCode` (string, required)
  - `RegionName` (string, required)
  - `RegionProbabilityPercent` (decimal, required)
  - `AccessibleDestinationPercent` (decimal, required)
  - `MonopolyDestinationPercent` (decimal, required)
  - `EligibleCityCount` (int, required)
- **Validation**:
  - `RegionCode` must resolve to a real map region.
  - `EligibleCityCount` must be greater than 0.

## Entity: RegionChoicePhaseModel

- **Purpose**: View-model representation of the pending region-choice branch used by the board UI.
- **Fields**:
  - `PlayerIndex` (int, required)
  - `PlayerName` (string, required)
  - `CurrentCityName` (string, required)
  - `CurrentRegionName` (string, required)
  - `Options` (collection of `DestinationRegionOption`, required)
  - `SelectedRegionCode` (string, optional)
  - `CanConfirm` (bool, required)
- **Validation**:
  - `Options` must be non-empty whenever the model is visible.
  - `SelectedRegionCode`, when present, must match one of the option region codes.

## Entity: FinalDestinationAssignment

- **Purpose**: The completed destination result after a replacement region is chosen and a city is drawn from that region.
- **Fields**:
  - `PlayerIndex` (int, required)
  - `RegionCode` (string, required)
  - `RegionName` (string, required)
  - `CityName` (string, required)
  - `CityNodeId` (string, optional in UI model)
- **Validation**:
  - `CityName` must belong to `RegionCode` in the loaded map definition.

## Entity: TurnState Extension

- **Purpose**: Adds authoritative persistence support for unresolved region-choice decisions.
- **Fields**:
  - `PendingRegionChoice` (`PendingRegionChoiceState`, optional)
- **Validation**:
  - Must be null outside the destination-selection branch.
  - Must be cleared immediately after final destination assignment.

## State Transitions

1. `DrawDestination` -> initial region draw resolves to different region -> `Roll` or `Move` with final destination assigned.
2. `DrawDestination` -> initial region draw matches current region -> `PendingRegionChoice` persisted and broadcast.
3. `PendingRegionChoice` -> controlling participant selects replacement region -> server performs weighted city draw within that region.
4. `PendingRegionChoice` -> final city assigned -> `Roll` or `Move` with pending choice cleared.
5. `PendingRegionChoice` -> reconnect/reload -> state restored unchanged until a valid region selection completes.

## Relationships

- `PendingRegionChoiceState` belongs to exactly one active turn.
- `RegionChoicePhaseModel` is derived from one `PendingRegionChoiceState` plus current map coverage/probability data.
- `FinalDestinationAssignment` resolves one `PendingRegionChoiceState`.