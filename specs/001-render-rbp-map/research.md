# Research: RBP Map Board Rendering

## Decision 1: Parse `.RBP`/`.RB3` map files with a tolerant section-based parser
- **Decision**: Use a line-oriented parser that recognizes known section headers (for example `[header]`, `[city]`, `[label]`, `[re*]`, `[map]`, `[sep]`) and ignores unknown optional sections.
- **Rationale**: The sample data includes many sections and comments; tolerant parsing satisfies FR-009 and avoids hard failure on non-required sections.
- **Alternatives considered**:
  - Strict full-schema parser for all sections (rejected: brittle and unnecessary for MVP render scope).
  - Regex-only extraction without section model (rejected: harder to validate required sections and produce useful errors).

## Decision 2: Keep board composition server-rendered in existing Blazor Server app
- **Decision**: Implement map load, parse, and board view using existing Blazor Server page/component patterns and current Fluent UI controls.
- **Rationale**: User explicitly confirmed existing UI/tooling; aligns with constitution simplicity and avoids introducing new frontend frameworks.
- **Alternatives considered**:
  - Add separate SPA frontend for rendering (rejected: unnecessary architecture expansion).
  - Build desktop renderer outside web app (rejected: outside feature scope and platform target).

## Decision 3: Render board with layered scalable drawing surface
- **Decision**: Use a layered rendering model (background image + city rectangles + labels + train-dot markers + map line overlays) that scales uniformly with a single zoom transform.
- **Rationale**: Uniform scaling preserves relative alignment (FR-015) and makes zoom behavior deterministic (FR-012).
- **Alternatives considered**:
  - Recompute each layer independently per zoom event (rejected: higher drift risk and complexity).
  - Render a pre-composited bitmap only (rejected: poor label/marker clarity at zoom levels).

## Decision 4: Zoom interaction model and bounds
- **Decision**: Enforce zoom range 25%–300%, default fit-to-board, cursor-centered wheel zoom, viewport-centered scroll-bar zoom.
- **Rationale**: Matches clarified product behavior and provides predictable map navigation.
- **Alternatives considered**:
  - Unbounded or very large zoom ranges (rejected: unstable UX and rendering overhead).
  - Single focal behavior for all zoom inputs (rejected: does not match clarified requirement).

## Decision 5: Background asset resolution strategy
- **Decision**: Resolve background image paths relative to the loaded map file location first, then fall back to known static asset paths when configured.
- **Rationale**: Keeps map bundles portable while supporting app-hosted assets; improves user success rate for valid files.
- **Alternatives considered**:
  - Only app static folder lookup (rejected: breaks map-relative bundles).
  - Only map-relative lookup (rejected: limits centrally hosted asset usage).

## Decision 6: Validation and error reporting style
- **Decision**: Validate required sections/fields before render and return user-facing errors with missing/invalid section names.
- **Rationale**: Satisfies FR-007/FR-008 and supports fast troubleshooting for malformed input.
- **Alternatives considered**:
  - Best-effort partial render (rejected: risks misleading board state).
  - Generic “invalid file” message only (rejected: poor debuggability).
