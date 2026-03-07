# Implementation Plan: UI Component Library Migration

**Branch**: `001-port-ui-mudblazor` | **Date**: 2026-03-06 | **Spec**: `specs/001-port-ui-mudblazor/spec.md`
**Input**: Feature specification from `specs/001-port-ui-mudblazor/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Migrate the `src/Boxcars` Blazor Server UI from Fluent UI to MudBlazor while preserving user-visible behavior across landing, authentication, dashboard, and game board flows. Execute migration in phased slices (infrastructure, shared shell/layout, page/component conversions, validation sweep), remove all Fluent dependencies/usages, and standardize on MudBlazor layout/theming to minimize custom CSS and custom HTML.

## Technical Context

<!--
  ACTION REQUIRED: Replace the content in this section with the technical details
  for the project. The structure here is presented in advisory capacity to guide
  the iteration process.
-->

**Language/Version**: C# 12 on .NET 8 (`net8.0`)  
**Primary Dependencies**: ASP.NET Core Blazor Server, MudBlazor, ASP.NET Core SignalR, ASP.NET Core Identity (custom store), Azure.Data.Tables  
**Storage**: Azure Table Storage  
**Testing**: `dotnet build Boxcars.slnx`, existing xUnit test projects (`tests/Boxcars.Engine.Tests`, `tests/Boxcars.GameEngine.Tests`), focused manual UI acceptance for migrated pages  
**Target Platform**: Modern desktop/mobile browsers via Blazor Server
**Project Type**: Web application (single server project with shared engine libraries)  
**Performance Goals**: No user-perceived regression in interaction responsiveness on migrated pages; no increase in runtime UI errors for primary journeys  
**Constraints**: Must remove Fluent dependencies, use MudBlazor components/layout primitives, minimize custom CSS and raw HTML, preserve route/interaction behavior, maintain SignalR-driven real-time UX  
**Scale/Scope**: `src/Boxcars` UI surfaces currently using Fluent controls (broad usage across shell and map/game components); migration includes shared app shell, layout, imports, and all impacted page/component files

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Research Gate Review

- **I. Gameplay Fidelity**: PASS — UI technology swap only; no rule logic changes planned.
- **II. Real-Time Multiplayer First**: PASS — SignalR architecture and server-authoritative model remain unchanged.
- **III. Simplicity & Ship Fast**: PASS — in-place migration in existing project; no new solution projects/layers.
- **Blazor UI Conventions**: PASS (planned) — target is full MudBlazor usage with minimal raw HTML and minimized CSS.

### Post-Design Gate Review

- **I. Gameplay Fidelity**: PASS — design artifacts explicitly exclude gameplay/rule modifications.
- **II. Real-Time Multiplayer First**: PASS — migration contract includes no-regression checks for real-time updates during interaction.
- **III. Simplicity & Ship Fast**: PASS — phased migration plus shared mapping patterns avoids speculative abstractions.
- **Blazor UI Conventions**: PASS — design centers MudBlazor components/layout/theme and disallows mixed legacy/target control sets.

## Project Structure

### Documentation (this feature)

```text
specs/001-port-ui-mudblazor/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── ui-migration-contract.md
└── tasks.md             # Created later by /speckit.tasks
```

### Source Code (repository root)
<!--
  ACTION REQUIRED: Replace the placeholder tree below with the concrete layout
  for this feature. Delete unused options and expand the chosen structure with
  real paths (e.g., apps/admin, packages/something). The delivered plan must
  not include Option labels.
-->

```text
src/
└── Boxcars/
  ├── Boxcars.csproj
  ├── Program.cs
  ├── Components/
  │   ├── App.razor
  │   ├── _Imports.razor
  │   ├── Layout/
  │   ├── Pages/
  │   └── Map/
  ├── Services/
  └── wwwroot/

tests/
├── Boxcars.Engine.Tests/
└── Boxcars.GameEngine.Tests/
```

**Structure Decision**: Keep the existing single Blazor Server host project and migrate UI in place. No new projects or architectural layers are introduced. Scope is concentrated in `src/Boxcars/Components`, root composition files (`Program.cs`, `App.razor`, `_Imports.razor`), and package references in `Boxcars.csproj`.

## Phase 0: Research Plan

1. Confirm migration strategy and risk controls for replacing control libraries in Blazor Server.
2. Define component mapping patterns for high-frequency controls and shared layout primitives.
3. Define acceptance/no-regression contract criteria for behavior parity, responsive checks, and real-time interaction stability.

## Phase 1: Design Plan

1. Model migration-tracking entities (`UI Surface`, `Control Mapping`, `Style Rule Set`) and their state transitions.
2. Define UI migration contract under `contracts/` for parity and validation evidence.
3. Produce quickstart execution sequence for implementing and validating migration slices.
4. Update agent context to include MudBlazor-based tech direction.

## Phase 2: Task Planning Preview

Implementation tasking (`tasks.md`) should be produced in sequential slices:

1. **Infrastructure Slice**: package + DI + root shell assets/providers.
2. **Shared Shell Slice**: imports, main layout, nav/profile structures.
3. **Page/Component Slice A**: account/landing/dashboard pages.
4. **Page/Component Slice B**: map/game board and child components.
5. **Validation/Hardening Slice**: Fluent removal verification, responsive checks, and runtime smoke validation.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| None | N/A | N/A |
