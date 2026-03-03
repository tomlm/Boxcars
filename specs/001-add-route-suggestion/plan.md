# Implementation Plan: Route Suggestion

**Branch**: `001-add-route-suggestion` | **Date**: 2026-03-03 | **Spec**: `/specs/001-add-route-suggestion/spec.md`
**Input**: Feature specification from `/specs/001-add-route-suggestion/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Add cost-based route suggestion from current player position to a destination city on the map board. Reuse the existing right-click map interaction pattern to add a mock city destination selector, calculate the cheapest valid route using ownership-based railroad turn costs (own/unowned = $1000, other player's = $5000), account for 2-die vs 3-die travel profile, and render suggested route points as circles in the active player's color.

## Technical Context

**Language/Version**: C# / .NET 8 (`net8.0`)  
**Primary Dependencies**: ASP.NET Core Blazor Server, Microsoft Fluent UI Blazor components, ASP.NET Core SignalR, existing map stack (`MapBoard`, `MapRouteService`, board projection/viewport services)  
**Storage**: Azure Table Storage for player/game ownership state; in-memory map graph context for route calculation  
**Testing**: Existing `dotnet build` + focused unit tests for route-cost algorithm (recommended for non-trivial game logic per constitution) and manual map UI validation  
**Target Platform**: Modern desktop/mobile browsers via server-rendered Blazor
**Project Type**: Single-project web application (`src/Boxcars`)  
**Performance Goals**: Route suggestion recalculates within one interaction cycle for normal map sizes; map update remains visually responsive (<200ms perceived on interaction)  
**Constraints**: Preserve existing route-node revisit behavior (backtrack before railroad toggle), preserve route-node railroad context-menu append preference, keep server-authoritative rule/state decisions, no speculative architecture  
**Scale/Scope**: One map feature slice: destination selection + route computation + route-point highlighting for current player

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Phase 0 Gate Review

- **I. Gameplay Fidelity**: PASS — This feature directly models route cost behavior from game rules and does not introduce house-rule changes; tie-break behavior will be deterministic and documented.
- **II. Real-Time Multiplayer First**: PASS — Route cost and ownership-derived decisions remain server-side/authoritative service logic; UI displays computed outcome.
- **III. Simplicity & Ship Fast**: PASS — Extend existing `MapRouteService` and `MapBoard` flows; no new project/layer/framework.
- **Naming Conventions**: PASS — No new storage tables required by this feature plan.
- **Coding Conventions**: PASS — Planned I/O integration points stay async with cancellation where applicable.

### Post-Phase 1 Design Re-check

- **I. Gameplay Fidelity**: PASS — Research/design artifacts define exact ownership costs and deterministic cheapest-route selection.
- **II. Real-Time Multiplayer First**: PASS — Contracts keep calculation and ownership evaluation in application services; UI only triggers and renders.
- **III. Simplicity & Ship Fast**: PASS — Reuses existing map mode and route menu patterns; adds only minimal new entities/contracts.
- **Violations**: None.

## Project Structure

### Documentation (this feature)

```text
specs/001-add-route-suggestion/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/
└── Boxcars/
  ├── Components/
  │   └── Pages/
  │       ├── MapBoard.razor                    # MODIFY: destination menu + suggested point highlights
  ├── Services/
  │   └── Maps/
  │       └── MapRouteService.cs                # MODIFY: weighted cheapest-route calculation
  ├── Data/
  │   └── Maps/
  │       ├── BoardElements.cs                  # MODIFY: route suggestion render DTO integration
  │       └── RouteSuggestionModels.cs          # NEW: route suggestion model types
  ├── Hubs/
  │   └── BoxCarsHub.cs                         # MODIFY: route suggestion events for client propagation
  └── wwwroot/
    └── js/
      └── mapBoard.js                       # KEEP unless interaction hook updates are required

specs/
└── 001-add-route-suggestion/
  ├── spec.md
  ├── plan.md
  ├── research.md
  ├── data-model.md
  ├── quickstart.md
  └── contracts/
```

**Structure Decision**: Extend the existing single Blazor Server project in place. Keep route calculation in `MapRouteService` and map interaction/rendering in `MapBoard`. No extra projects or cross-cutting abstractions are introduced.

## Phase 0 Output

- `research.md` documents route-cost algorithm choice, die-profile handling strategy, ownership-cost sourcing, and destination context-menu integration decisions.

## Phase 1 Output

- `data-model.md` defines route suggestion, traversal-cost breakdown, destination selection, and route-point highlight entities and transitions.
- `contracts/route-suggestion-ui-contract.md` defines UI interaction contract for right-click destination selection and suggested-route rendering.
- `quickstart.md` defines setup and validation flows for 2-die/3-die and ownership-cost scenarios.

## Complexity Tracking

No constitution violations currently require complexity exceptions.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| None | N/A | N/A |
