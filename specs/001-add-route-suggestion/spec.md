# Feature Specification: Route Suggestion

**Feature Branch**: `[001-add-route-suggestion]`  
**Created**: 2026-03-03  
**Status**: Draft  
**Input**: User description: "Add route suggestion with cheapest-route calculation, destination city mock selection via right-click, and route point highlighting in the user's color."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Calculate cheapest route to destination city (Priority: P1)

As a player, I can request a suggested route from my current map point to a destination city, and the game returns the cheapest valid route.

**Why this priority**: Cost-aware route guidance is the core gameplay value of this feature.

**Independent Test**: Can be fully tested by setting a current point and destination, then verifying the returned route has the lowest total travel cost among available valid routes.

**Acceptance Scenarios**:

1. **Given** a player has a current map point and a destination city is selected, **When** route suggestion runs, **Then** the system returns a connected route that reaches the destination city.
2. **Given** two or more valid routes to the destination, **When** route suggestion runs, **Then** the system selects the route with the lowest total travel cost.
3. **Given** route cost includes railroad ownership rules, **When** a route uses an unowned railroad turn or a railroad owned by the player, **Then** that turn contributes $1000 to total route cost.
4. **Given** route cost includes railroad ownership rules, **When** a route uses a railroad owned by a different player, **Then** that turn contributes $5000 to total route cost.

---

### User Story 2 - Set destination via mock helper menu (Priority: P2)

As a tester or developer, I can right-click a city and set it as destination from a mock helper menu so route suggestion can be exercised quickly.

**Why this priority**: Destination selection is required to drive and verify route suggestion behavior during development and testing.

**Independent Test**: Can be tested by right-clicking any city and selecting the destination menu item, then confirming the selected destination updates.

**Acceptance Scenarios**:

1. **Given** a city is visible on the map, **When** the user opens the right-click context menu on that city, **Then** the menu includes an action to set that city as destination.
2. **Given** the user chooses the destination action from the city context menu, **When** the action is applied, **Then** that city becomes the active destination used by route suggestion.

---

### User Story 3 - Visualize suggested route points (Priority: P3)

As a player, I can see each point on the suggested route highlighted with circles in my color so I can quickly follow the recommendation on the map.

**Why this priority**: Visualization improves usability and confirms exactly which path is being suggested.

**Independent Test**: Can be tested by generating a route and verifying every point on that route is highlighted with player-color circles.

**Acceptance Scenarios**:

1. **Given** a route suggestion has been calculated, **When** the map is rendered, **Then** each point in the suggested route is highlighted with a circle in the active user's color.
2. **Given** destination changes and a new suggestion is calculated, **When** the new route is displayed, **Then** highlight circles update to reflect only the new route points.

---

### Edge Cases

- If no valid connected path exists between current point and destination city, the system shows no suggested route and provides a clear failure state.
- If multiple routes tie for cheapest total cost, the system returns one deterministic route using a stable tie-break rule.
- If the user right-clicks outside a city target, no destination-selection action is shown.
- If destination is set to the same city as the player's current city, the suggested route is zero-length and no intermediate travel cost is applied.
- If a player profile does not indicate whether they are on 2-die or 3-die movement, route suggestion uses a defined default movement profile.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST allow a player to calculate a suggested route from the player's current map point to a selected destination city.
- **FR-002**: System MUST compute route total cost using railroad-turn ownership pricing rules: $1000 per turn for unowned railroads, $1000 per turn for railroads owned by the player, and $5000 per turn for railroads owned by other players.
- **FR-003**: System MUST select the best route as the valid destination-reaching route with the lowest total computed travel cost.
- **FR-004**: System MUST account for the player's movement profile (2-die or 3-die) when determining turn-based traversal cost for candidate routes.
- **FR-005**: System MUST expose a right-click context menu action on city targets to set that city as active destination for route suggestion.
- **FR-006**: System MUST recalculate suggested route whenever active destination changes.
- **FR-007**: System MUST visualize the suggested route by highlighting each route point with a circle in the active user's color.
- **FR-008**: System MUST clear or replace prior suggestion highlights when a new suggestion is produced.
- **FR-009**: System MUST handle no-route situations without crashing and must present a clear no-suggestion outcome.
- **FR-010**: System MUST apply route suggestion and route highlighting consistently for both 2-die and 3-die players.
- **FR-011**: System MUST propagate destination selection and route suggestion state updates to connected clients in real time via SignalR.

### Key Entities *(include if feature involves data)*

- **Player Travel Profile**: Represents the active player context used for route suggestion, including current location, movement type (2-die or 3-die), and player color.
- **Destination City Selection**: Represents the currently chosen city target for route suggestion.
- **Railroad Traversal Segment**: Represents one traversable portion of route travel with ownership status and per-turn cost category.
- **Route Suggestion**: Represents the ordered set of points from start to destination and its computed total cost.
- **Route Point Highlight**: Represents map-visual markers applied to suggested route points in the active user's color.

## Assumptions & Dependencies

- Route suggestion starts from the player's current map point already maintained by game state.
- Existing map data provides enough connectivity and ownership information to classify traversal segment cost correctly.
- Turn calculation for 2-die and 3-die players follows established game movement rules already defined elsewhere in the product.
- The mock helper city context menu is intended for testing/development support and can be visible in environments where map interaction testing occurs.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In controlled route test cases, 100% of suggestions reach the selected destination city when at least one valid path exists.
- **SC-002**: In controlled route test cases with known alternatives, 100% of suggestions match the lowest expected total travel cost according to defined pricing rules.
- **SC-003**: For both 2-die and 3-die player profiles, users can set a destination via right-click city menu and see updated route highlights within one interaction cycle.
- **SC-004**: In usability checks, at least 90% of test users correctly identify all suggested route points from the on-map circle highlights without additional guidance.

## Rulebook Reference

- Route-cost and movement-profile behavior MUST be implemented against the official Rail Baron rulebook sections governing movement and user fees.
- If interpretation choices are needed for digital adaptation, the chosen interpretation MUST be documented in implementation notes and applied consistently.
