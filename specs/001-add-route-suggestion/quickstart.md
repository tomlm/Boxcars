# Quickstart: Route Suggestion (Feature 001)

**Purpose**: Validate route suggestion behavior using existing Boxcars tooling and map UI.

## Prerequisites

- .NET 8 SDK
- Existing local setup for Boxcars (including Azurite if required by your environment)
- Map file available (default `U21MAP.RB3`)

## Run

1. Start the app:
   - `cd src/Boxcars`
   - `dotnet run`
2. Open the app in a browser and navigate to the page hosting `MapBoard`.
3. Ensure map renders and Route mode is available.

## Validation Scenarios

### 1) Destination Selection via Right-Click (P2)

1. Enter Route mode.
2. Right-click a city.
3. Select `Set as destination` from the mock helper menu.
4. Confirm destination updates and triggers route suggestion.

**Expected**: Destination city is set without page refresh; previous suggestion (if any) is replaced.

### 2) Cheapest Route by Ownership Cost (P1)

1. Use a start point with at least two valid destination paths.
2. Ensure one candidate path contains more turns on other-player-owned railroads.
3. Trigger route suggestion.

**Expected**: Suggested route is the one with the lowest computed total cost (using $1000/$5000 per-turn ownership rules), even if it is not the fewest-segment path.

### 3) 2-Die and 3-Die Profile Coverage (P1)

1. Run suggestion as a 2-die player profile.
2. Repeat with a 3-die profile for the same destination.

**Expected**: Both profiles produce valid cheapest-route output; turn totals/costs reflect the movement profile policy.

### 4) Suggested Route Point Highlighting (P3)

1. Compute a suggestion.
2. Verify each point along the suggested route has a circle highlight in the active user's color.
3. Change destination and recompute.

**Expected**: Old circles are cleared and replaced with circles for the new route only.

### 5) SignalR Propagation (FR-011)

1. Open two authenticated browser sessions on the same game page.
2. In session A, right-click a city and set destination.
3. Observe that route suggestion update event is published to connected clients.

**Expected**: Route suggestion updates are broadcast through `RouteSuggestionUpdated` SignalR event.

### 6) No-Route Handling (Edge Case)

1. Select a destination known to be unreachable from current node (or simulate disconnected node).
2. Trigger suggestion.

**Expected**: No crash; no stale highlight remains; clear no-suggestion state is shown.

## Suggested Verification Commands

From repository root:

- `dotnet build Boxcars.slnx`
- `dotnet test` (if test projects are present for this feature scope)

## Notes

- Keep existing route-node interaction behavior intact:
  - Backtrack logic must run before railroad toggle when revisiting previously selected non-endpoint nodes.
  - Route-node railroad menu should prefer appending from current endpoint when possible.
