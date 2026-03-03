# Implementation Plan: Map Interaction Modes

**Branch**: `003-map-modes` | **Date**: 2026-03-02 | **Spec**: `/specs/003-map-modes/spec.md`
**Input**: Feature specification from `/specs/003-map-modes/spec.md`

## Summary

Add explicit map interaction modes to the game board: **Rail mode** for railroad inspection and **Route mode** for movement planning. Rail mode keeps current segment-click behavior (highlight selected railroad). Route mode introduces node-driven path selection from a temporary starting node of **Chicago**, renders the active route as a **solid black line**, and supports in-route undo by clicking an already selected node.

## Technical Context

**Language/Version**: C# on .NET 8 (`net8.0`)  
**Primary Dependencies**: Blazor Server, Microsoft Fluent UI Blazor, existing map parser/projection services (`IMapParserService`, `BoardProjectionService`, `BoardViewportService`)  
**Storage**: N/A for this phase (UI/runtime state only)  
**Testing**: `dotnet build` baseline + targeted unit tests for route graph/path logic (new service/helper)  
**Target Platform**: Modern desktop/mobile browsers via server-hosted Blazor  
**Project Type**: Single-project web application  
**Performance Goals**: Mode toggle and click feedback in same render cycle; no visible lag while panning/zooming on default map  
**Constraints**: Preserve existing pan/zoom behavior; no player/game-turn persistence yet; start node hardcoded to Chicago until player mechanics exist  
**Scale/Scope**: `MapBoard` interactions and supporting map routing helpers only

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Phase 0 Gate Review

- **I. Gameplay Fidelity**: PASS — Route display behavior aligns with Rail Baron movement planning semantics; no rule mutations beyond UI interaction.
- **II. Real-Time Multiplayer First**: PASS — No server-authoritative gameplay state added yet; this is local board interaction scaffolding for future multiplayer turn logic.
- **III. Simplicity & Ship Fast**: PASS — Extend existing `MapBoard` and map services without introducing additional projects or unnecessary abstractions.
- **Coding Conventions**: PASS — Any new I/O remains async with `CancellationToken`; routing calculations are in-memory and synchronous by design.

### Post-Phase 1 Design Re-check

- **I. Gameplay Fidelity**: PASS — Rail mode highlights full railroad; Route mode pathing from Chicago + undo-on-prior-node matches feature spec.
- **II. Real-Time Multiplayer First**: PASS — Local-only route state is explicitly temporary and isolated from authoritative game state.
- **III. Simplicity & Ship Fast**: PASS — One UI component plus one focused routing helper/service for graph traversal.

## Project Structure

### Documentation (this feature)

```text
specs/003-map-modes/
├── spec.md
├── plan.md              # This file
├── research.md          # Optional follow-up if design tradeoffs need recording
├── data-model.md        # Optional follow-up for route graph model details
├── quickstart.md        # Optional follow-up for validation scenarios
├── contracts/           # Optional follow-up (none required yet)
└── tasks.md             # Phase 2 output (/speckit.tasks)
```

### Source Code (repository root)

```text
src/Boxcars/
├── Components/
│   └── Pages/
│       ├── MapBoard.razor            # MODIFY: add mode toggle and route-mode click handling
│       └── MapBoard.razor.css        # MODIFY: style mode toggle, route path, route nodes
├── Data/
│   └── Maps/
│       ├── BoardElements.cs          # MODIFY: add render metadata if needed for route overlays
│       └── MapDefinition.cs          # KEEP (route segments already available)
├── Services/
│   └── Maps/
│       ├── BoardProjectionService.cs # MODIFY: expose node/segment projections usable by route mode
│       ├── MapRouteService.cs        # NEW: graph building + shortest path + route truncation helper
│       └── RbpMapParserService.cs    # KEEP (already parses route segments)
└── wwwroot/
    └── js/
        └── mapBoard.js               # KEEP unless extra pointer helpers are required
```

**Structure Decision**: Implement in the existing single Blazor project. Keep all map interaction behavior in `MapBoard` while moving non-trivial route traversal logic into a small dedicated map service to preserve readability and testability.

## Phase Plan

### Phase 0: Clarify interaction semantics

- Define exact mode state model (`Rail` / `Route`) and default mode.
- Define click targets in Route mode (city nodes vs all train dots) and freeze this in `tasks.md`.
- Confirm fallback behavior when Chicago cannot be resolved from map data.

### Phase 1: Design routing model

- Build adjacency graph from `MapDefinition.RailroadRouteSegments` and `TrainDots`.
- Define deterministic path selection strategy (fewest segments; stable tie-break).
- Define overlay model for route-selected segments and route-selectable node hitboxes.

### Phase 2: Implement UI + logicl

- Add mode toggle control to `MapBoard` controls card.
- Preserve existing railroad highlighting for Rail mode.
- Add Route mode node click handling, path expansion from Chicago, and truncation on prior-node click.
- Render route-selected segments as solid black line independent of railroad color.

### Phase 3: Validate

- Verify scenarios from `spec.md` manually on `/game/{id}`.
- Add focused tests for route service path/truncation behavior.
- Run `dotnet build Boxcars.slnx` and targeted tests.

## Risks & Mitigations

- **Ambiguous path ties**: Multiple equal-length paths may exist between nodes.
  - **Mitigation**: deterministic tie-break order using stable segment ordering from parsed map data.
- **Dense node hitboxes**: Route node clicking may conflict with pan interactions.
  - **Mitigation**: explicit hitbox radius + stop-propagation only on node click targets.
- **Chicago lookup mismatch**: Name mismatches across map files could break default start.
  - **Mitigation**: case-insensitive city-name lookup with clear warning and safe no-op route mode when unresolved.

## Complexity Tracking

No constitution violations identified at planning time.
