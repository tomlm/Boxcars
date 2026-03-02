# Implementation Plan: RBP Map Board Rendering

**Branch**: `001-render-rbp-map` | **Date**: 2026-03-02 | **Spec**: `/specs/001-render-rbp-map/spec.md`
**Input**: Feature specification from `/specs/001-render-rbp-map/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Load supported RBP/RB3 map files and render a complete game board in the existing Blazor Server UI using Fluent controls, including background image, city rectangles + labels, train-position dots, and map overlays. Implement deterministic layered rendering with validated parsing, explicit error handling (no misleading partial render), and zoom interactions defined as 25%–300%, default fit-to-board, cursor-centered wheel zoom, and viewport-centered scroll-bar zoom.

## Technical Context

<!--
  ACTION REQUIRED: Replace the content in this section with the technical details
  for the project. The structure here is presented in advisory capacity to guide
  the iteration process.
-->

**Language/Version**: C# / .NET 8 (`net8.0`)  
**Primary Dependencies**: ASP.NET Core Blazor Server, Microsoft Fluent UI Blazor components, SignalR, Azure.Data.Tables  
**Storage**: Azure Table Storage (existing app storage), local file input for map content  
**Testing**: Existing `dotnet test` for solution docs project plus manual scenario validation from `quickstart.md`; add focused automated tests only for non-trivial parser/transform logic if introduced  
**Target Platform**: Modern desktop/web browsers via Blazor Server
**Project Type**: Web application (single server-rendered app)  
**Performance Goals**: Meet SC-003 and SC-006 from spec (first render ≤2s for 95% valid loads; visible zoom response ≤200ms)  
**Constraints**: Maintain layer alignment across zoom (FR-015), clamp zoom to 25%–300% (FR-016), no misleading partial render on invalid input (FR-008), keep implementation simple per constitution  
**Scale/Scope**: One game board rendering surface in existing app; support sample USA-scale map set and same-session map reloads

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Research Gate Review

- **Gameplay Fidelity (Principle I)**: PASS — Board visuals represent canonical map artifacts and coordinates without altering game rules.
- **Real-Time Multiplayer First (Principle II)**: PASS — Feature is read/render oriented and does not move authority to client; no rule/state authority changes introduced.
- **Simplicity & Ship Fast (Principle III)**: PASS — Uses existing Blazor Server + Fluent stack and incremental services/components.
- **Tech Stack Constraints**: PASS — Remains in C#/.NET, Blazor Server, existing project patterns.

### Post-Design Gate Review (after Phase 1 artifacts)

- **Gameplay Fidelity (Principle I)**: PASS — Data model and contract preserve source naming and coordinate fidelity.
- **Real-Time Multiplayer First (Principle II)**: PASS — Contracts and quickstart do not introduce client-authoritative game outcomes.
- **Simplicity & Ship Fast (Principle III)**: PASS — Design avoids new projects/frameworks and focuses on minimal render pipeline.
- **Violations**: None.

## Project Structure

### Documentation (this feature)

```text
specs/001-render-rbp-map/
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
    │   ├── Layout/
    │   └── Pages/
    │       ├── Game.razor
    │       └── (new/updated map board components)
    ├── Services/
    │   ├── GameService.cs
    │   ├── (new map parsing/render state services)
    ├── Data/
    │   └── (optional map DTOs/view models)
    ├── Hubs/
    └── wwwroot/
        └── (optional static assets for sample backgrounds)

specs/
└── 001-render-rbp-map/
    ├── spec.md
    ├── plan.md
    ├── research.md
    ├── data-model.md
    ├── quickstart.md
    └── contracts/
```

**Structure Decision**: Keep a single existing Blazor Server project and add focused map parsing/rendering pieces inside current `src/Boxcars` folders. Do not create extra application projects.

## Phase 0 Output

- `research.md` completed with decisions for parser tolerance, layered rendering, zoom model, asset resolution, and validation strategy.

## Phase 1 Output

- `data-model.md` completed with map, city, dot, viewport, and load-result entities plus validation/state transitions.
- `contracts/map-board-ui-contract.md` completed for map load + zoom interaction behavior and error contracts.
- `quickstart.md` completed with validation flows for P1/P2/P3 scenarios and regressions.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| None | N/A | N/A |
