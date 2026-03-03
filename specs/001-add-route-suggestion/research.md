# Research: Route Suggestion

**Feature**: `001-add-route-suggestion`  
**Date**: 2026-03-03  
**Purpose**: Resolve route-calculation and map-interaction design choices before task planning.

---

## R1: Cheapest Route Algorithm for Ownership-Based Costs

**Decision**: Use weighted shortest-path search (Dijkstra-style) over existing map graph edges, where each traversed segment contributes turn-cost based on railroad ownership category.

**Rationale**: Existing `MapRouteService` currently optimizes by segment count with railroad preference bias. This does not guarantee minimum monetary travel cost when ownership changes edge weights. Weighted shortest path directly matches FR-002 and FR-003.

**Alternatives considered**:
- Keep BFS + preference rank: simple but incorrect when shortest segment-count path is more expensive.
- Enumerate all paths and pick cheapest: computationally expensive and unnecessary for this graph.

---

## R2: Turn Cost Model for 2-Die vs 3-Die Players

**Decision**: Compute total route cost as the sum of per-turn railroad costs over traversal turns; movement profile (2-die/3-die) controls how segment traversal maps to turns using existing game movement rules.

**Rationale**: Spec requires ownership cost per turn and requires support for both movement profiles. Keeping turn-calculation policy separate from graph search weight function maintains clarity and fidelity.

**Alternatives considered**:
- Cost per segment only: ignores turn semantics and violates FR-004.
- Duplicate separate route solvers for 2-die and 3-die: unnecessary duplication.

---

## R3: Deterministic Tie-Breaking for Equal-Cost Routes

**Decision**: When multiple routes have equal total cost, select deterministically by (1) lower total turns, then (2) fewer railroad switches, then (3) lexical node-path order.

**Rationale**: Edge-case requirement demands stable deterministic behavior for equal-cost solutions; deterministic output prevents UI churn and test flakiness.

**Alternatives considered**:
- First-found route in adjacency order: unstable if input ordering changes.
- Random tie-breaker: non-deterministic and unsuitable for repeatable tests.

---

## R4: Ownership Cost Source for Route Evaluation

**Decision**: Resolve railroad ownership from current game/player state already used by map rendering (`IsOwned` semantics and game ownership state), with default classification fallback to "other-player-owned" only when ownership is known to differ from active player.

**Rationale**: Cost rules depend on ownership category. The route solver must consume authoritative ownership state rather than UI-only color hints.

**Alternatives considered**:
- Infer ownership solely from map stroke color: presentation detail, not authoritative.
- Assume all railroads unowned during suggestion: violates pricing requirements.

---

## R5: Destination Selection Interaction Pattern

**Decision**: Add destination selection to existing right-click map menu pattern on city targets in `MapBoard`, as a mock-helper action (`Set as destination`) without adding a new global selector UI.

**Rationale**: Reuses established interaction model already present for route-node railroad selection and satisfies MVP scope with minimal UI surface.

**Alternatives considered**:
- Dedicated destination panel/dropdown: adds non-required UI complexity.
- Left-click destination assignment: conflicts with existing route/mode interactions.

---

## R6: Suggested Route Visualization

**Decision**: Render suggested route points as dedicated circle overlays in the active user's color, replacing prior suggestion markers on recomputation.

**Rationale**: Matches FR-007/FR-008 and keeps suggested-path semantics visually distinct from static train dots and city markers.

**Alternatives considered**:
- Highlight only connecting lines: does not meet explicit point-circle requirement.
- Keep cumulative circles from prior suggestions: violates replacement behavior.

---

## R7: Existing Route-Node Behavior Compatibility

**Decision**: Preserve current route-node revisit and railroad-menu behavior constraints while adding destination-driven suggestion mode output.

**Rationale**: Repository instructions require backtrack to run before railroad toggle for revisits, and route-node menu railroad choice must prefer appending from current endpoint where possible.

**Alternatives considered**:
- Rewrite interaction sequencing wholesale: higher regression risk and unnecessary for this feature.

---

## R8: Rulebook Citation Requirement for Implementation

**Decision**: Route suggestion implementation must include an explicit citation to the official Rail Baron rulebook guidance for movement and user-fee cost behavior in the implementation PR notes.

**Rationale**: The project constitution requires gameplay-rule accuracy to be verified against the official rulebook and interpretation choices to be documented consistently.

**Implementation note**:
- Before merge, record the exact rulebook section/page reference used for route cost and movement interpretation.
- If edition differences create ambiguity, document the selected interpretation and apply it deterministically.
