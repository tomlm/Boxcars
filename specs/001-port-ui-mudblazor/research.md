# Research: UI Component Library Migration

## Decision 1: Use phased migration with temporary mixed-dependency window
- **Decision**: Migrate in ordered slices (infrastructure, shared shell, page/component batches), allowing temporary side-by-side library packages during transition, then remove legacy packages at the end.
- **Rationale**: Keeps the app runnable while reducing regression risk from a full cutover.
- **Alternatives considered**:
  - Big-bang migration in one PR (rejected: high blast radius and difficult triage).
  - Parallel rewrite branch with delayed merge (rejected: long-lived drift and complex rebasing).

## Decision 2: Keep Blazor Server render model unchanged
- **Decision**: Preserve existing Blazor Server interactive model and SignalR wiring; migrate UI controls/layout only.
- **Rationale**: Maintains multiplayer/server-authoritative behavior and avoids architecture risk.
- **Alternatives considered**:
  - Render-mode refactor during migration (rejected: not required by feature scope).
  - Client-side/hybrid rewrite (rejected: outside current constraints and timeline).

## Decision 3: Standardize on MudBlazor layout/theming to minimize CSS and raw HTML
- **Decision**: Prefer component props, standard layout primitives, and shared theme tokens over custom CSS/HTML.
- **Rationale**: Directly satisfies the feature requirement to minimize custom CSS and raw HTML while improving consistency.
- **Alternatives considered**:
  - Keep existing CSS and only swap controls (rejected: does not meet minimization goal).
  - Fully custom HTML/CSS rewrite without library primitives (rejected: violates constitution UI conventions).

## Decision 4: Use explicit control-mapping patterns
- **Decision**: Define and apply a stable mapping catalog for frequently used controls (card, stack/layout, text, button, form input, menu/profile).
- **Rationale**: Reduces inconsistent replacements and improves implementation speed.
- **Alternatives considered**:
  - Ad hoc per-file substitutions (rejected: high inconsistency risk).
  - Generic wrapper abstraction layer for all components (rejected: unnecessary complexity for current scope).

## Decision 5: Introduce contract-driven no-regression validation
- **Decision**: Track parity checks per UI surface using behavior parity, responsive checks, and no-regression criteria.
- **Rationale**: Migration success is behavioral equivalence, not only dependency replacement.
- **Alternatives considered**:
  - Visual-only snapshot checks (rejected: misses interaction and realtime regressions).
  - Manual exploratory testing without documented contract (rejected: low repeatability and auditability).

## Clarifications Resolved
- **Primary framework direction**: Blazor Server on .NET remains the host model.
- **Target UI toolkit**: MudBlazor is the approved component library per current governance.
- **Scope boundary**: UI migration only; no gameplay rule or multiplayer architecture changes.
- **Quality target**: Remove legacy control library usage entirely and reduce custom CSS/custom HTML to only unavoidable cases.
