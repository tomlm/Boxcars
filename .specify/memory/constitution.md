<!--
Sync Impact Report
===================
Version change: 1.6.0 → 1.7.0
Modified principles: N/A
Added sections:
  - N/A
Removed sections: N/A
Templates requiring updates: None
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
- Complexity MUST be justified: if a reviewer questions whether
  something is over-engineered, the author MUST demonstrate
  the concrete problem that necessitates the complexity.
- Refactor when pain is felt, not in anticipation of pain.

**Rationale**: A shipped game with simple code is infinitely
more valuable than an elegantly architected game that is never
finished. Rail Baron has well-defined, bounded scope — the
architecture should reflect that simplicity.

## Technology Stack & Constraints

- **Language**: C# / .NET (latest LTS)
- **Web Framework**: Blazor Server 
- **Real-Time Communication**: ASP.NET Core SignalR
- **Target Platform**: Modern web browsers (desktop and mobile)
- **Data Storage**: Azure Table storage
- **Testing**: Tests are encouraged for game rule logic,
  multiplayer state management, and integration points. Tests
  are NOT mandated for every change but SHOULD accompany any
  non-trivial game logic or state synchronization code.
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

- **Component library**: Use MudBlazor components as the
  primary UI toolkit. Follow MudBlazor best practices for
  component usage, theming, and layout.
- **No raw HTML**: Avoid raw HTML elements. Use MudBlazor
  components for all UI rendering. Raw HTML is permitted
  only when no suitable MudBlazor component exists and MUST be
  justified in review.
- **No inline CSS**: Styling MUST NOT be applied via inline
  `style` attributes. Use CSS classes, CSS isolation
  (`.razor.css` files), or MudBlazor theming/tokens instead.
- **MudBlazor-first styling**: When customizing MudBlazor
  components, contributors MUST prefer built-in MudBlazor
  parameters, properties, variants, colors, spacing, and
  theming options before falling back to custom CSS. Custom CSS
  SHOULD be used only when MudBlazor does not provide an
  appropriate built-in mechanism.
- **CSS isolation scope anchors**: Blazor scoped CSS
  (`.razor.css`) only applies to elements rendered directly by
  the component — MudBlazor components render their own internal
  DOM that lacks the parent's scope attribute. When scoped styles
  need to target a MudBlazor component or its internals,
  contributors MUST wrap it in a plain HTML element (`<div>`,
  `<span>`) that carries the CSS class. Use `::deep` from that
  scope anchor to reach into MudBlazor internals. Placing
  `Class="..."` directly on a MudBlazor component will NOT work
  with scoped CSS.
- **Component decomposition**: Pages MUST NOT be monolithic.
  Major UI sections (e.g., map, player panel, dice controls,
  railroad list, chat) MUST be extracted into dedicated Blazor
  components to keep each file focused and independently
  testable.
- **Component naming**: Blazor components SHOULD be named
  after the domain concept they represent (e.g.,
  `GameMap.razor`, `PlayerPanel.razor`, `DiceRoller.razor`).
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
- User stories SHOULD be scoped to independently deliverable,
  testable slices of functionality.

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
  Violations MUST be resolved before merge.
- **Conflict Resolution**: When principles conflict (e.g.,
  Simplicity vs. Fidelity), Gameplay Fidelity (Principle I)
  takes precedence. If Principle I is not involved, prefer
  Simplicity (Principle III).

**Version**: 1.7.0 | **Ratified**: 2026-02-26 | **Last Amended**: 2026-03-08
