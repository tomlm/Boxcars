<!--
Sync Impact Report
===================
Version change: 1.12.0 → 1.13.0
Modified principles:
  - Blazor UI Conventions (rewrote styling rules to mandate MudBlazor-only
    UI with inline styles as the sole fallback; eliminated CSS isolation
    and custom CSS class references; removed CSS scope anchor guidance;
    added explicit prohibition on .razor.css files and class attributes;
    codified MudBlazor parameter hierarchy for styling)
Added sections:
  - N/A
Removed sections: N/A
Templates requiring updates:
  - ✅ Reviewed only: .specify/templates/plan-template.md
  - ✅ Reviewed only: .specify/templates/agent-file-template.md
  - ✅ Reviewed only: .specify/templates/spec-template.md
  - ✅ Reviewed only: .specify/templates/tasks-template.md
  - ✅ Reviewed only: .specify/templates/checklist-template.md
  - ✅ No command templates present under .specify/templates/commands/
  - ✅ No runtime guidance docs present at repo root (README.md/docs/quickstart.md)
Follow-up TODOs: None
-->

# Boxcars Constitution

Boxcars is an online multiplayer web adaptation of the classic
board game Rail Baron (also known as Box Cars). This constitution
defines the non-negotiable principles, constraints, and governance
rules that guide all development on the project.

## Core Principles

### I. Gameplay Fidelity

The game MUST faithfully reproduce the rules, mechanics, and
strategic experience of the original Rail Baron board game.

- All dice rolls, route calculations, payoff tables, railroad
  purchases, and destination logic MUST match the original
  game's published rules.
- Deviations from the original rules are permitted ONLY when
  adapting physical-world mechanics to a digital interface
  (e.g., automated payoff lookups, digital dice). Such
  adaptations MUST NOT alter outcomes or strategy.
- Rule accuracy MUST be verified against the official Rail Baron
  rules at https://www.railgamefans.com/rbp/rb21rules.htm.
  When ambiguity exists in the original rules, the
  chosen interpretation MUST be documented and applied
  consistently.
- Turn-flow edge cases involving locomotive bonuses, deferred
  movement, use-fee timing, purchase continuation, and segment
  reuse MUST be implemented as explicit rule behavior in the
  authoritative engine and covered by focused regression tests
  when changed.
- New "house rule" variants or quality-of-life features (e.g.,
  game speed options, undo) MUST be clearly separated from
  the core rule engine and MUST NOT modify base game behavior.

**Rationale**: Players choosing an online Rail Baron experience
expect the same strategic depth and fairness as the physical
game. Rule drift erodes trust and replayability.

### II. Real-Time Multiplayer First

Every feature MUST be designed for concurrent multiplayer play
from the start. Single-player or hot-seat modes are secondary
concerns.

- Game state MUST be authoritative on the server. Clients
  render state but MUST NOT determine game outcomes.
- All player-facing state changes MUST propagate to connected
  clients in real time via SignalR (or equivalent push
  mechanism).
- The game MUST handle player disconnection and reconnection
  gracefully — a disconnected player's game MUST be resumable
  without data loss.
- Turn management, input validation, and rule enforcement MUST
  occur server-side to prevent cheating or desynchronization.
- Latency and network failures MUST be handled with clear user
  feedback (e.g., connection status indicators, retry logic).

**Rationale**: Rail Baron is inherently a multiplayer game.
Designing for single-player first and retrofitting multiplayer
leads to architectural debt, race conditions, and poor UX.

### III. Simplicity & Ship Fast

Favor the simplest implementation that correctly solves the
problem. Avoid speculative abstractions and premature
optimization.

- Apply YAGNI: do not build features, abstractions, or
  infrastructure until there is a concrete, immediate need.
- Prefer fewer projects and layers. Every additional project,
  interface, or abstraction layer MUST be justified by a
  specific problem it solves today.
- Start with Blazor's built-in patterns and .NET conventions
  before introducing third-party libraries or custom
  frameworks.
- Feature-specific notes, experiments, migrations, and one-off
  workarounds MUST stay local to the relevant spec, plan, issue,
  or code until repeated use proves they belong in project-wide
  standards.
- Complexity MUST be justified: if a reviewer questions whether
  something is over-engineered, the author MUST demonstrate
  the concrete problem that necessitates the complexity.
- Refactor when pain is felt, not in anticipation of pain.

**Rationale**: A shipped game with simple code is infinitely
more valuable than an elegantly architected game that is never
finished. Rail Baron has well-defined, bounded scope — the
architecture should reflect that simplicity.

### IV. Advisory Outputs Are Derived, Not Decisive

Route suggestions, fee previews, map overlays, purchase analysis,
recommendation inputs, and other player-assistance outputs MUST be
derived from the same authoritative map and game-state inputs as the
server rule engine. They MUST remain informational and MUST NOT decide
or mutate outcomes on the client.

- Client-visible projections MUST be reproducible from authoritative
  state or shared domain services rather than parallel client-only
  rule implementations.
- Hypothetical or projected values MUST be clearly scoped as advisory
  so players can distinguish them from committed game outcomes.
- Configuration-backed rule values (for example, engine upgrade
  pricing) MUST be defined in typed configuration and validated on the
  server before any client displays or submits them.
- If an advisory UI output conflicts with server rule resolution, the
  server result MUST win and the advisory output MUST be corrected.

**Rationale**: Boxcars now includes purchase analysis, route
suggestions, fee previews, and other strategic aids. These features
are useful only when they mirror authoritative rules without becoming
a second rule engine.

### V. Stable Guidance Becomes Standard

Implementation guidance discovered during feature work MUST be promoted
into this constitution only when it has become an enduring Boxcars
standard rather than a one-off implementation note.

- Guidance qualifies for constitutional promotion only when it is
  recurring across multiple features or reviews, applies to the shared
  Boxcars architecture, stack, or workflow, is likely to remain valid
  for future work, and is specific enough to verify in code review.
- Guidance that is temporary, experimental, migration-specific,
  workaround-driven, or limited to a single feature MUST remain in the
  nearest feature spec, plan, task list, issue, or code comment rather
  than being elevated into the constitution.
- When a pull request uncovers guidance that meets the promotion bar,
  the contributor MUST either amend this constitution in the same
  change or link a follow-up governance change before merge.
- When a new constitutional rule is adopted, overlapping local notes
  MUST be removed, narrowed, or explicitly linked so the constitution
  remains the authoritative source for enduring project-wide guidance.

**Rationale**: Boxcars gains clarity from lessons repeated across
multiple features, but it loses clarity when temporary notes are
mistaken for lasting standards. This principle preserves consistency
without turning the constitution into a backlog of feature-specific
exceptions.

## Technology Stack & Constraints

- **Language**: C# / .NET (latest LTS)
- **Web Framework**: Blazor Server 
- **Real-Time Communication**: ASP.NET Core SignalR
- **Target Platform**: Modern web browsers (desktop and mobile)
- **Data Storage**: Azure Table storage
- **Testing**: Tests are encouraged for game rule logic,
  multiplayer state management, and integration points. Tests
  are NOT mandated for every change but MUST accompany changes
  to non-trivial turn flow, fee resolution, configurable rule
  values, or derived advisory calculations that can mislead
  player decisions.
- **CI/CD**: Automated build and test on pull requests. The
  main branch MUST always be in a deployable state.

## Naming Conventions

- **Collections**: When a symbol represents a collection of a
  single object type, it MUST use the plural form of that
  object type.
  - Example: collection of `User` objects → `Users`.
- **Mono tables**: A storage table containing one object type
  MUST be named `<PluralObjectName>Table`.
  - Example: table of `User` records → `UsersTable`.
- **Hetero tables**: Tables intentionally containing multiple
  object types are exempt from the mono-table naming rule and
  may use a domain-oriented name.
- **Applies to**: Specifications, plans, data models, contracts,
  code, and schema definitions unless a documented exception is
  approved in review.

## Coding Conventions

- **.NET style guidance**: Contributors MUST follow Microsoft
  recommended .NET coding style guidelines and framework design
  guidelines for C# code.
- **LINQ style**: When manipulating data with LINQ in C#,
  contributors SHOULD use extension-method syntax as the default
  style.
  - Preferred: `users.Where(...).Select(...).OrderBy(...)`
  - Avoid query-expression syntax unless it is materially clearer
    for a specific case and justified in review.
- **Consistency**: Within a file or feature, use one LINQ style
  consistently to keep code readable and maintainable.
- **I/O methods**: Any method that performs I/O (network,
  database, filesystem, external service, or stream operations)
  MUST use the async pattern.
- **Cancellation**: I/O methods MUST accept a `CancellationToken`
  and propagate it to downstream async APIs whenever supported.
- **API shape**: Async methods SHOULD use the `Async` suffix and
  return `Task`/`Task<T>` (or `ValueTask`/`ValueTask<T>` when
  justified by performance and usage patterns).
- **Blocking calls**: Avoid blocking on async work (`.Result`,
  `.Wait()`, `GetAwaiter().GetResult()`) in application code.

## Blazor UI Conventions

- **Component library — MudBlazor only**: MudBlazor is the
  exclusive UI toolkit. Every UI element MUST be expressed as a
  MudBlazor component whenever one exists for the concept
  (drawers, panels, app bars, dialogs, tabs, grids, stacks,
  papers, tooltips, overlays, buttons, text, icons, tables,
  chips, alerts, avatars, switches, selects, radio groups, etc.).
  Contributors MUST NOT recreate these concepts with raw HTML
  and custom CSS.
- **No raw HTML**: Raw HTML elements (`<div>`, `<span>`, etc.)
  are permitted ONLY as lightweight layout wrappers when no
  MudBlazor layout component covers the need (e.g., a CSS grid
  container, a fixed-position overlay anchor). Raw HTML elements
  used for layout MUST use inline `style` attributes — never
  CSS classes.
- **SVG map exemption**: The SVG-based map components
  (`MapComponent.razor`, `GameMapComponent.razor`) and their
  companion `.razor.css` files are exempt from MudBlazor-only
  rules because SVG rendering has no MudBlazor equivalent.
- **No CSS files**: Contributors MUST NOT create or maintain
  `.razor.css` scoped CSS files (except for the exempt SVG map
  components above and `MainLayout.razor.css` which hosts the
  framework `#blazor-error-ui` styles). Existing empty CSS files
  MUST be deleted, not left as placeholders.
- **No CSS classes**: Contributors MUST NOT use the `Class`
  parameter on MudBlazor components to apply custom CSS class
  names. The only permitted `Class` values are MudBlazor's
  own utility classes (e.g., `pa-4`, `mb-3`, `d-flex`).
- **Styling hierarchy**: When styling is needed, contributors
  MUST follow this order of preference:
  1. **MudBlazor component parameters** — `Color`, `Variant`,
     `Size`, `Elevation`, `Spacing`, `Dense`, `Outlined`,
     `Square`, `Rounded`, `FullWidth`, `Wrap`, `Justify`,
     `AlignItems`, and other built-in parameters.
  2. **MudBlazor utility classes** via `Class` — `pa-*`,
     `ma-*`, `mb-*`, `mt-*`, `d-flex`, `flex-column`,
     `flex-grow-1`, `overflow-auto`, `flex-shrink-0`, and
     all other MudBlazor CSS utilities for display, flex,
     spacing, overflow, sizing, and positioning. Flex layout
     MUST be expressed through these utility classes — not
     inline `style` attributes — whenever a corresponding
     utility class exists.
  3. **MudBlazor theming** — palette tokens
     (`var(--mud-palette-*)`) and theme provider configuration.
  4. **Inline `Style` attribute** — for CSS that cannot be
     expressed through MudBlazor parameters, utility classes,
     or theming. Inline styles are the approved fallback; they
     are preferred over CSS files because they are co-located
     with the component, survive refactoring, and avoid scoped
     CSS pitfalls with MudBlazor's internal DOM.
- **Computed styles**: When a component needs dynamic styling
  (e.g., player colors, conditional visibility), contributors
  MUST use C# properties or methods that return inline style
  strings, not CSS class strings. Example:
  `private string CardStyle => "background: #0b3d91; color: #fff";`
- **Component-first layout and behavior**: Contributors MUST
  express layout, panel behavior, visibility, spacing, and
  interaction through MudBlazor components and normal Blazor
  component composition. Inline `style` is a fallback for
  visual refinement or gaps in MudBlazor's component model,
  not the primary way to invent UI structure or interaction.
- **Renderer preference**: When the UI needs new rich-content
  rendering, contributors MUST prefer a maintained Blazor
  component package that fits the current stack before
  introducing custom JavaScript, ad hoc DOM manipulation, or
  hand-rolled HTML rendering. Any exception MUST be justified in
  review.
- **Component decomposition**: Pages MUST NOT be monolithic.
  Major UI sections (e.g., map, player panel, dice controls,
  railroad list, chat) MUST be extracted into dedicated Blazor
  components to keep each file focused and independently
  testable.
- **Component naming**: Blazor components SHOULD be named
  after the domain concept they represent (e.g.,
  `GameMap.razor`, `PlayerPanel.razor`, `DiceRoller.razor`).
- **Behavior-aligned naming**: Component names, action labels,
  helper methods, and similar UI-facing abstractions MUST describe
  the concrete behavior they currently implement. Avoid speculative
  generic names such as `PrimaryAction` or `TurnStatus` when the code
  actually commits movement or renders movement status. When a
  component or helper narrows to one responsibility over time, it MUST
  be renamed to match that responsibility.
- **Parameter discipline**: Components SHOULD accept data via
  `[Parameter]` properties and communicate upward via
  `EventCallback`. Avoid injecting broad state objects when
  a focused parameter set suffices.
- **Data binding**: Use Blazor's built-in data binding (`@bind`,
  `@bind:event`, `@bind:after`) for two-way bindings. Prefer
  one-way binding (`Value="@prop"`) with explicit
  `ValueChanged` callbacks over two-way `@bind` when the
  component needs to intercept or validate changes. Avoid
  manual `StateHasChanged()` calls — rely on Blazor's
  automatic re-rendering after event handlers and parameter
  updates.

## Development Workflow

- Features are developed on feature branches and merged to
  main via pull request.
- Each pull request SHOULD include a brief description of what
  changed and why.
- Code reviews SHOULD verify compliance with this constitution,
  particularly Gameplay Fidelity (Principle I) and server-
  authoritative state (Principle II).
- Game rule changes MUST reference the specific rule from the
  original Rail Baron rulebook being implemented or clarified.
- Changes to advisory UI, projections, or recommendation logic MUST
  identify the authoritative source they derive from (rulebook,
  engine state, typed configuration, or shared analysis service).
- Pull requests that introduce or reuse recurring implementation
  guidance MUST state whether that guidance is now constitutional
  project policy or remains feature-local, and why.
- User stories SHOULD be scoped to independently deliverable,
  testable slices of functionality.

## Decision Capture & Guidance Promotion

- A rule belongs in this constitution only when it is enduring,
  project-wide, and reviewable during normal code review.
- Guidance discovered while implementing features MUST be promoted
  when it recurs across multiple features or reviews and is expected
  to stay valid for future Boxcars work.
- Feature-local constraints, temporary migrations, experiments, and
  issue-specific workarounds MUST stay in feature documents, issues,
  or code comments rather than becoming constitutional rules.
- If guidance is not yet clearly enduring, contributors MUST document
  it locally and revisit it after additional feature work instead of
  constitutionalizing it prematurely.

## Governance

This constitution is the highest-authority document for the
Boxcars project. All development practices, code reviews, and
architectural decisions MUST align with these principles.

- **Amendments**: Any change to this constitution MUST be
  documented with a rationale, reviewed, and merged via pull
  request. The version MUST be incremented per the versioning
  policy below.
- **Versioning**: Constitution versions follow semantic
  versioning (MAJOR.MINOR.PATCH):
  - MAJOR: Removal or incompatible redefinition of a principle.
  - MINOR: New principle, section, or materially expanded
    guidance.
  - PATCH: Clarifications, wording, typo fixes.
- **Compliance**: All pull requests and code reviews MUST verify
  that changes do not violate constitution principles.
  Reviews MUST also check whether recurring implementation guidance
  discovered during the work now qualifies for constitutional
  promotion. Violations and missed durable guidance promotions MUST
  be resolved before merge or tracked by linked follow-up governance
  work.
- **Conflict Resolution**: When principles conflict (e.g.,
  Simplicity vs. Fidelity), Gameplay Fidelity (Principle I)
  takes precedence. If Principle I is not involved, prefer
  Simplicity (Principle III).

**Version**: 1.13.0 | **Ratified**: 2026-02-26 | **Last Amended**: 2026-03-20
