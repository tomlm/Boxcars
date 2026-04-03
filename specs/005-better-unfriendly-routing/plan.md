# Implementation Plan: Better Routing for Unfriendly Destinations

**Branch**: `005-better-unfriendly-routing` | **Date**: 2026-04-02 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/005-better-unfriendly-routing/spec.md`

## Summary

Enhance advisory route planning for unfriendly destinations so the suggested route minimizes not only immediate arrival cost but the combined fee outlook for arrival, next-turn exit pressure, and locomotive bonus-out opportunities. Keep the calculation server-authoritative in rules and advisory-only in effect by extending the existing route-suggestion path (`GameEngine` → `MapRouteService` → map/UI projections), reusing configured fee settings, grandfathered-fee rules, and authoritative locomotive behavior. Add deterministic strategic tie breaks for least-cash owner, weakest network, and payment spreading, with focused regression tests around route ranking and fee semantics.

## Technical Context

**Language/Version**: C# / .NET 10 in the active Boxcars feature path, with the wider workspace also containing .NET 8 projects  
**Primary Dependencies**: Blazor Server, MudBlazor, ASP.NET Core SignalR, xUnit, existing Boxcars engine and map services  
**Storage**: No new persistence; advisory routing reads existing game state, map definitions, and configured game settings  
**Testing**: xUnit in `tests/Boxcars.Engine.Tests` with focused regression coverage for engine, route service, and projection behavior  
**Target Platform**: Server-authoritative Blazor web application for modern desktop and mobile browsers  
**Project Type**: Blazor Server web app with a shared game-engine library and service layer  
**Performance Goals**: Keep route recalculation interactive for map updates and destination changes; route ranking must remain deterministic and fast enough for repeated in-turn recomputation  
**Constraints**: Must preserve gameplay fidelity, must use settings-based fees rather than hard-coded amounts, must remain advisory-only, must not introduce a client-side rules fork, must support grandfathered fees and locomotive bonus rules, must remain deterministic under ties  
**Scale/Scope**: One feature spanning the engine route-suggestion seam, map routing service, route suggestion models/projections, and route-specific regression tests

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

**Pre-research gate**

- **Gameplay Fidelity**: Pass. The feature explicitly deepens fee-aware routing based on official fee behavior, locomotive bonus rules, and grandfathered-fee handling rather than introducing a house-rule shortcut.
- **Real-Time Multiplayer First**: Pass. Route planning remains advisory and derived from authoritative state; no client authority or multiplayer state ownership changes are introduced.
- **Simplicity & Ship Fast**: Pass. The implementation extends the existing route-suggestion seam instead of creating a second planner or a separate optimization subsystem.
- **Advisory Outputs Are Derived, Not Decisive**: Pass. The route planner remains informational, fed by authoritative game state, game settings, and shared services.
- **Stable Guidance Becomes Standard**: Pass. Any special weighting or tie-break decisions stay feature-local unless they later recur beyond this routing feature.

**Post-design re-check**

- Expected pass if the design keeps all ranking inputs derived from authoritative engine state and existing shared services.
- Expected pass if bonus-out probability and network-strength tie breaks are implemented through existing engine/service concepts rather than UI-only heuristics.
- No planned constitution exceptions require complexity tracking.

## Project Structure

### Documentation (this feature)

```text
specs/005-better-unfriendly-routing/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── Boxcars/
│   ├── Components/
│   │   └── Map/
│   │       └── MapComponent.razor
│   ├── Data/
│   │   └── Maps/
│   │       ├── RouteSuggestionModels.cs
│   │       └── RouteSuggestionProjection.cs
│   └── Services/
│       ├── NetworkCoverageService.cs
│       └── Maps/
│           └── MapRouteService.cs
├── Boxcars.Engine/
│   ├── Domain/
│   │   ├── GameEngine.cs
│   │   └── Enums.cs
│   └── Persistence/
│       └── GameSettings.cs

tests/
└── Boxcars.Engine.Tests/
    └── Unit/
        ├── GameEngineSettingsFeeTests.cs
        ├── MapRouteServiceTests.cs
        ├── RouteSuggestionProjectionTests.cs
        └── RouteSuggestionTests.cs
```

**Structure Decision**: Keep the feature inside the existing advisory-routing flow. Extend `MapRouteService` for richer candidate evaluation, keep `GameEngine` as the authoritative route-suggestion entry point, reuse `GameSettings` and ownership semantics already defined in the engine, and validate behavior through the existing engine test project.

## Implementation Phases

### Phase 0 - Research

- Confirm the exact authoritative fee-trigger semantics to model during route ranking, including public, personal, unfriendly, and grandfathered fees.
- Confirm which existing engine state exposes enough data to evaluate unavoidable next-turn exit pressure after reaching an unfriendly destination.
- Confirm the exact authoritative bonus-roll rules needed for `BonusOut` probability calculation.
- Confirm the network-strength metric inputs required for the `AccessibleDestinationPercent` and `MonopolyDestinationPercent` tie breaks.

### Phase 1 - Design

- Define a richer advisory route-ranking model that captures arrival fees, exit exposure, bonus-out probabilities, fee-recipient distribution, and tie-break metadata.
- Decide where grandfathered-fee evaluation should live so route ranking and actual fee resolution use the same effective values.
- Define deterministic ordering for candidate comparison: expected fee, worst-case fee, owner cash, owner network, payment spread, then final stable tie break.
- Define the route suggestion result fields needed to explain unfriendly-destination choices in projections or future UI diagnostics.

### Phase 2 - Implementation

- Extend `MapRouteService` candidate-state and ranking logic for combined arrival-plus-exit evaluation.
- Feed authoritative fee values, ownership semantics, and grandfathered-fee logic into route ranking.
- Add exact `BonusOut` evaluation using locomotive and bonus-roll rules.
- Extend suggestion models or projections only as needed to expose the selected route rationale.
- Keep `GameEngine.SuggestRoute` and current map-driven recomputation wired to the upgraded planner without mutating state.

### Phase 3 - Validation

- Add focused regression tests for unfriendly-destination ranking, next-turn exit planning, bonus-out preference, grandfathered-fee handling, and owner-spread tie breaks.
- Re-run existing route suggestion and fee tests to ensure the richer planner does not break current deterministic routing behavior outside the new feature scope.

## Complexity Tracking

No constitution exceptions are currently expected for this feature.
