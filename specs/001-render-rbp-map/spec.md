# Feature Specification: RBP Map Board Rendering

**Feature Branch**: `001-render-rbp-map`  
**Created**: 2026-03-02  
**Status**: Draft  
**Input**: User description: "Loading a RBP map file and rendering it in the UI, including background image, train-piece dot positions, and labeled city rectangles as the game board"

## Clarifications

### Session 2026-03-02

- Q: How should users adjust board magnification when viewing the map? → A: Map supports zoom in/out via mouse wheel and an in-app zoom scrollbar/slider control.
- Q: What zoom range and default zoom behavior should the board use? → A: Fixed range 25%–300%, default fit-to-board.
- Q: What zoom focal behavior should each zoom input use? → A: Mouse wheel zoom is cursor-centered; in-app zoom scrollbar/slider control is viewport-centered.

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

### User Story 1 - Load and View a Complete Board (Priority: P1)

As a player, I can open a valid RBP map file and immediately see a complete game board with the matching background image, map geometry, city markers, city labels, and train-position dots in the correct locations.

**Why this priority**: This is the core value of the feature; without an accurate board render, no gameplay map interaction is possible.

**Independent Test**: Can be fully tested by loading the provided USA map package and confirming that board elements appear together and are visually aligned on first render.

**Acceptance Scenarios**:

1. **Given** a valid RBP map file and required referenced assets are available, **When** the user opens the map, **Then** the board renders the background and map elements as one coherent board view.
2. **Given** a valid map with city and dot coordinates, **When** the board is rendered, **Then** city rectangles, city labels, and train-position dots are drawn at their defined coordinates.

---

### User Story 2 - Inspect Board Readability (Priority: P2)

As a player, I can clearly distinguish labeled cities and train-position dots from the board background so I can identify destinations and piece placement points.

**Why this priority**: Visual readability is required to use the board effectively for route planning and movement.

**Independent Test**: Can be tested by loading a map and verifying labels remain readable and markers remain visible across the full board bounds.

**Acceptance Scenarios**:

1. **Given** a successfully loaded map, **When** the board is displayed, **Then** city labels are legible and each city marker is visually associated with its label.
2. **Given** a successfully loaded map, **When** the board is displayed, **Then** train-position dots are visible and distinct from city rectangles.
3. **Given** a successfully loaded map, **When** the user zooms with the mouse wheel or the in-app zoom scrollbar/slider control, **Then** the board zoom level updates and markers/labels remain spatially aligned.
4. **Given** a successfully loaded map, **When** first render occurs, **Then** the board starts at fit-to-board zoom within an enforced zoom range of 25% to 300%.
5. **Given** a successfully loaded map, **When** zoom is changed using the mouse wheel, **Then** zoom is centered on the current cursor position.
6. **Given** a successfully loaded map, **When** zoom is changed using the in-app zoom scrollbar/slider control, **Then** zoom is centered on the viewport center.

---

### User Story 3 - Handle Invalid or Incomplete Map Inputs (Priority: P3)

As a player, I receive a clear failure message when a map file is invalid, unsupported, or missing required data, so I understand why the board cannot be shown.

**Why this priority**: Prevents silent failures and reduces confusion when map content is malformed or incomplete.

**Independent Test**: Can be tested by opening malformed files and files with missing required sections, then verifying clear error feedback and no partial misleading board render.

**Acceptance Scenarios**:

1. **Given** a file that cannot be parsed as a supported map format, **When** the user attempts to open it, **Then** the system rejects it and presents a clear, user-facing error.
2. **Given** a map that parses but lacks required board data, **When** rendering is attempted, **Then** the system does not show a misleading board and presents a clear reason.

---

### Edge Cases

<!--
  ACTION REQUIRED: The content in this section represents placeholders.
  Fill them out with the right edge cases.
-->

- Map references a background image that is missing or unreadable.
- Map contains coordinates outside declared board bounds.
- Map includes unknown sections or extra metadata.
- City label text exceeds expected display width.
- Duplicate city names or duplicate coordinate points appear in source data.
- Board file is valid but contains zero cities or zero train-position dots.
- User zooms continuously to minimum or maximum allowed level.

## Requirements *(mandatory)*

<!--
  ACTION REQUIRED: The content in this section represents placeholders.
  Fill them out with the right functional requirements.
-->

### Functional Requirements

- **FR-001**: System MUST allow a user to select and open a supported RBP map file for board rendering.
- **FR-002**: System MUST parse required map data sections needed to render the board background, city markers, city labels, and train-position dots.
- **FR-003**: System MUST render map elements using the coordinate system defined by the loaded map data.
- **FR-004**: System MUST render city markers as rectangular indicators and show each city’s label adjacent to or associated with its marker.
- **FR-005**: System MUST render train-position locations as dot markers at the positions defined by map data.
- **FR-006**: System MUST render map elements aligned to the intended board extent so that relative geographic placement is preserved.
- **FR-007**: System MUST show a clear user-facing error when map parsing fails, required sections are missing, or required assets cannot be loaded.
- **FR-008**: System MUST prevent partial renders that could misrepresent map state when required board data is unavailable.
- **FR-009**: System MUST ignore non-required unknown map sections without failing board render, as long as required board data remains valid.
- **FR-010**: System MUST support reloading a different map file in the same session and replace the prior board view with the newly loaded board.
- **FR-011**: System MUST preserve original source names for displayed city labels.
- **FR-012**: System MUST provide deterministic rendering so repeated loads of the same file produce the same visual board output.
- **FR-013**: System MUST allow users to zoom the board in and out using the mouse wheel.
- **FR-014**: System MUST provide a dedicated in-app zoom scrollbar/slider control that can adjust zoom level in and out.
- **FR-015**: System MUST preserve relative alignment of all board layers (background, city rectangles, labels, and train-position dots) across zoom changes.
- **FR-016**: System MUST constrain zoom level to a minimum of 25% and a maximum of 300%.
- **FR-017**: System MUST initialize board view to fit-to-board zoom when a map is first rendered.
- **FR-018**: System MUST center mouse-wheel zoom operations on the cursor position at the time of zoom input.
- **FR-019**: System MUST center in-app zoom scrollbar/slider zoom operations on the viewport center.

### Key Entities *(include if feature involves data)*

- **Map Definition**: A board source containing metadata, region definitions, city definitions, marker coordinates, and drawing instructions.
- **Board Asset Reference**: A pointer to required visual assets (for example, the board background image) used in final render composition.
- **City**: A destination node with name, region, and one or more board coordinates used for marker and label placement.
- **Train Position Dot**: A board coordinate denoting a valid position for train pieces.
- **Board Element Layer**: A logical visual grouping (background, cities, labels, dots, and other map overlays) used to compose the final board.
- **Board Viewport**: The current visual frame and zoom level used to display the board.

### Assumptions

- Users provide map files compatible with the existing Rail Baron map content format used by the project.
- Required assets referenced by a map file are expected to be available in accessible project or map-relative locations.
- Initial scope is loading and rendering a static board view; gameplay movement or interaction behavior is out of scope for this feature.
- Board readability is evaluated at normal application viewing scale with standard desktop display settings.

## Success Criteria *(mandatory)*

<!--
  ACTION REQUIRED: Define measurable success criteria.
  These must be technology-agnostic and measurable.
-->

### Measurable Outcomes

- **SC-001**: In validation with reference sample maps, 100% of required board elements (background, city rectangles, city labels, and train-position dots) are displayed.
- **SC-002**: In validation with reference sample maps, at least 98% of rendered city and dot markers appear within a tolerance of ±2 CSS pixels from expected reference positions.
- **SC-003**: At least 95% of valid map loads complete first visible board render within 2 seconds on the baseline validation profile (Windows 11, 4 logical CPU cores, 16 GB RAM, Chromium-based browser current stable release).
- **SC-004**: At least 90% of test users can correctly identify any requested city label and at least one nearby train-position dot within 10 seconds.
- **SC-005**: 100% of invalid-map test cases return a clear user-facing error and do not display a misleading partial board.
- **SC-006**: In usability validation, 100% of zoom actions from mouse wheel and in-app zoom scrollbar/slider controls result in visible zoom level changes within 200 ms.
- **SC-007**: During zoom validation, at least 99% of city markers, labels, and train-position dots remain aligned to their expected relative positions at each tested zoom level.
- **SC-008**: In zoom-boundary tests, 100% of attempted zoom actions beyond 25% or 300% are clamped to those limits.
- **SC-009**: In first-render tests, 100% of valid map loads start in fit-to-board zoom state.
- **SC-010**: In interaction tests, 100% of mouse-wheel zoom events keep the cursor target within ±10 px of its expected post-zoom screen location.
- **SC-011**: In interaction tests, 100% of in-app zoom scrollbar/slider events preserve viewport-center anchoring within ±10 px.
