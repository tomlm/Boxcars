# Data Model: Route Suggestion

**Feature**: `001-add-route-suggestion`  
**Date**: 2026-03-03  
**Source**: `/specs/001-add-route-suggestion/spec.md`, `/specs/001-add-route-suggestion/research.md`

---

## Entity: PlayerTravelProfile

- **Purpose**: Active player context for route-cost computation and route highlight style.
- **Fields**:
  - `PlayerId` (string, required)
  - `CurrentNodeId` (string, required)
  - `MovementType` (enum, required: `TwoDie`, `ThreeDie`)
  - `Color` (string, required; display color token/value for highlight)
- **Validation**:
  - `CurrentNodeId` must exist in route graph.
  - `MovementType` must be one of defined enum values.

## Entity: DestinationSelection

- **Purpose**: Mock-helper selected destination city for route suggestion.
- **Fields**:
  - `CityName` (string, required)
  - `NodeId` (string, required)
  - `SelectedAtUtc` (DateTime, required)
- **Validation**:
  - `NodeId` must resolve to a valid city/node on loaded map.

## Entity: TraversalSegmentCost

- **Purpose**: Per-segment cost breakdown used during weighted search.
- **Fields**:
  - `FromNodeId` (string, required)
  - `ToNodeId` (string, required)
  - `RailroadIndex` (int, required)
  - `OwnershipCategory` (enum, required: `Unowned`, `OwnedByPlayer`, `OwnedByOtherPlayer`)
  - `Turns` (int, required, >= 1)
  - `CostPerTurn` (int, required; allowed: 1000 or 5000)
  - `TotalCost` (int, required; `Turns * CostPerTurn`)
- **Validation**:
  - `CostPerTurn = 1000` when ownership is `Unowned` or `OwnedByPlayer`.
  - `CostPerTurn = 5000` when ownership is `OwnedByOtherPlayer`.

## Entity: RouteSuggestion

- **Purpose**: Cheapest computed route result from player start to destination.
- **Fields**:
  - `StartNodeId` (string, required)
  - `DestinationNodeId` (string, required)
  - `NodeIds` (ordered collection of string, required; first=start, last=destination)
  - `Segments` (ordered collection of `TraversalSegmentCost`, required)
  - `TotalTurns` (int, required)
  - `TotalCost` (int, required)
  - `ComputedAtUtc` (DateTime, required)
- **Validation**:
  - `NodeIds.Count >= 1`
  - `TotalCost` equals sum of segment totals.
  - If `StartNodeId == DestinationNodeId`, `Segments` is empty and `TotalCost = 0`.

## Entity: RouteSuggestionHighlight

- **Purpose**: UI overlay model for displaying suggested route points.
- **Fields**:
  - `NodeId` (string, required)
  - `X` (double, required)
  - `Y` (double, required)
  - `Color` (string, required)
  - `Radius` (double, required)
- **Validation**:
  - One highlight per route node in the active suggestion.
  - Highlight set is fully replaced when destination or suggestion changes.

---

## State Transitions

### Destination + Suggestion Flow

1. `NoDestination` → user right-clicks city and selects destination
2. `DestinationSelected` → route calculation triggered
3. `SuggestionComputed` → cheapest route stored/rendered
4. `SuggestionRendered` → route-point highlights displayed
5. Destination changes → previous highlights cleared → back to `SuggestionComputed`
6. No valid path found → transition to `NoSuggestionAvailable` (no highlight overlay)

### Route Tie Handling

- Equal `TotalCost` candidates are resolved deterministically using tie-break criteria from research.

---

## Relationships

- `PlayerTravelProfile` 1→1 active `DestinationSelection`
- `RouteSuggestion` references one `PlayerTravelProfile` and one `DestinationSelection`
- `RouteSuggestion` 1→many `TraversalSegmentCost`
- `RouteSuggestion` 1→many `RouteSuggestionHighlight`
