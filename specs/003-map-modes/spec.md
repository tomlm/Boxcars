# Feature Specification: Map Interaction Modes

**Feature Branch**: `[003-map-modes]`  
**Created**: 2026-03-02  
**Status**: Draft  
**Input**: User description: "Add modes for map. Rail mode highlights railroad on segment selection; Route mode builds and edits a player route from clicked nodes; add toggle button; start from Chicago for now."

## Clarifications

### Session 2026-03-02

- Q: For “exactly like RBP,” how should Route mode handle node clicks? → A: Auto-path (click any reachable node and system auto-selects intermediate segments).

## User Scenarios & Testing *(mandatory)*

<!--
  IMPORTANT: User stories should be PRIORITIZED as user journeys ordered by importance.
  Each user story/journey must be INDEPENDENTLY TESTABLE - meaning if you implement just ONE of them,
  you should still have a viable MVP (Minimum Viable Product) that delivers value.
  
  Assign priorities (P1, P2, P3, etc.) to each story, where P1 is the most critical.
  Think of each story as a standalone slice of functionality that can be:
  - Developed independently
  - Tested independently
  - Deployed independently
  - Demonstrated to users independently
-->

### User Story 1 - Build a route from Chicago (Priority: P1)

As a player planning movement, I can switch to Route mode and click map position nodes to build a connected route that starts at Chicago.

**Why this priority**: Route planning is the core gameplay interaction this feature introduces.

**Independent Test**: Can be tested by entering Route mode and clicking nodes; each click updates the displayed route from Chicago to the most recently clicked node.

**Acceptance Scenarios**:

1. **Given** the map is in Route mode and no route has been selected, **When** the map is shown, **Then** the current position is Chicago.
2. **Given** Route mode and current position Chicago, **When** a user clicks any reachable route node, **Then** the system auto-selects intermediate contiguous segments and highlights the resulting path from Chicago to that node as a solid black line.
3. **Given** Route mode and a current endpoint already selected, **When** the user clicks another reachable node, **Then** the system auto-selects intermediate contiguous segments from the current route endpoint to the clicked node and updates the displayed solid black route.

---

### User Story 2 - Inspect railroads (Priority: P2)

As a player evaluating options, I can switch to Rail mode and click any segment to highlight that segment's railroad.

**Why this priority**: Railroad inspection supports decision-making but is secondary to route building.

**Independent Test**: Can be tested by entering Rail mode and clicking segments on different railroads; only the selected railroad is highlighted each time.

**Acceptance Scenarios**:

1. **Given** Rail mode, **When** a user clicks any segment, **Then** all segments belonging to that railroad are highlighted.
2. **Given** Rail mode with one railroad highlighted, **When** the user clicks a segment on a different railroad, **Then** highlight moves to the newly selected railroad.

---

### User Story 3 - Undo route by clicking prior node (Priority: P3)

As a player adjusting movement, I can click a node already included in the selected route to undo all segments beyond that node.

**Why this priority**: Route correction is important for usability after base route selection is available.

**Independent Test**: Can be tested by selecting a multi-segment route, then clicking an earlier node on the same route and confirming trailing segments are removed.

**Acceptance Scenarios**:

1. **Given** Route mode and a selected route with at least three nodes, **When** the user clicks a node that is already part of that route, **Then** the route is truncated at that node and later segments are removed.
2. **Given** Route mode and a selected route, **When** the user clicks the current endpoint node again, **Then** the route remains unchanged.

---

### Edge Cases

- Clicking on map geometry that is not a position node in Route mode does not alter the route.
- Clicking an unreachable node in Route mode does not alter the route and keeps the current endpoint.
- Switching from Route mode to Rail mode preserves the current route selection state but changes visible emphasis to railroad highlight behavior.
- Switching from Rail mode to Route mode clears railroad inspection highlight and restores route visualization.
- If Chicago is not present in loaded map data, the map enters a safe non-interactive route state and surfaces a recoverable error message.

## Requirements *(mandatory)*

<!--
  ACTION REQUIRED: The content in this section represents placeholders.
  Fill them out with the right functional requirements.
-->

### Functional Requirements

- **FR-001**: System MUST provide a visible mode toggle control with exactly two modes: Rail mode and Route mode.
- **FR-002**: System MUST default Route mode starting position to Chicago for route calculations when gameplay/player state is unavailable.
- **FR-003**: In Rail mode, selecting any segment MUST highlight all segments belonging to the same railroad.
- **FR-004**: In Route mode, selecting any reachable position node MUST update the selected route using auto-path segment selection from the current route endpoint to the clicked node.
- **FR-005**: The selected route in Route mode MUST be rendered as a solid black line across all selected segments.
- **FR-006**: In Route mode, selecting a node already present in the selected route MUST remove all route segments after that node.
- **FR-007**: In Route mode, selecting the current endpoint node MUST NOT modify route selection.
- **FR-008**: Mode switching MUST update interaction behavior immediately without requiring page refresh.
- **FR-009**: Rail mode highlight state and Route mode selected-route state MUST be managed independently to avoid cross-mode corruption.
- **FR-010**: Route mode auto-path selection MUST use contiguous route segments only and MUST reject clicks on unreachable nodes without altering the current route.

### Key Entities *(include if feature involves data)*

- **MapInteractionMode**: UI state value indicating `Rail` or `Route` mode.
- **PositionNode**: A route-selectable map position with unique id and display name (including Chicago).
- **RailSegment**: A traversable map segment between two position nodes with railroad affiliation.
- **SelectedRoute**: Ordered path of position nodes/segments from Chicago to current route endpoint.
- **RailroadHighlight**: Set of segments currently emphasized for a selected railroad in Rail mode.

## Success Criteria *(mandatory)*

<!--
  ACTION REQUIRED: Define measurable success criteria.
  These must be technology-agnostic and measurable.
-->

### Measurable Outcomes

- **SC-001**: 100% of clicks on a segment in Rail mode produce a corresponding railroad highlight update in the same interaction cycle.
- **SC-002**: 100% of clicks on valid position nodes in Route mode produce a visible solid black route from Chicago to the selected endpoint.
- **SC-003**: 100% of clicks on previously selected route nodes correctly truncate the route beyond the clicked node.
- **SC-004**: Mode toggling completes without page reload and with interaction semantics changed within one user action.
