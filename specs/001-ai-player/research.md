# Research: AI-Controlled Player Turns

## Decision 1: Keep OpenAI orchestration server-side and call it through `HttpClient`

- **Decision**: Add a small server-side bot decision client that uses the existing DI-managed `HttpClient` stack and typed configuration sourced from `OpenAIKey`, with `gpt-4o-mini` as the default model.
- **Rationale**: This keeps the OpenAI key off the client, preserves server authority, matches the constitution's simplicity guidance, and avoids a new SDK abstraction when the app already uses DI, options, and `HttpClient` successfully.
- **Alternatives considered**:
  - Official OpenAI .NET SDK: viable, but adds another dependency and abstraction layer before there is a proven need.
  - Client-side OpenAI calls: rejected because it would expose credentials and violate server-authoritative multiplayer design.

## Decision 2: Persist global bot definitions in a dedicated `BotsTable`

- **Decision**: Introduce a new Azure Table named `BotsTable` to store global bot definitions.
- **Rationale**: `UsersTable` is currently a mono-type table for `ApplicationUser`, and the constitution explicitly prefers mono tables named after their object type. A dedicated `BotsTable` keeps storage intent clear, supports optimistic concurrency with table ETags, and avoids overloading user records with shared global objects.
- **Alternatives considered**:
  - Store bots in `UsersTable`: rejected because bot definitions are not user entities and the library is global rather than user-owned.
  - Store bots inside `GamesTable`: rejected because bot definitions outlive any individual game and must be reusable across games.

## Decision 3: Persist active assignments as game metadata, not just presence state

- **Decision**: Store active bot assignments in the authoritative game record/snapshot housed in `GamesTable`, while keeping delegated-controller connection ownership in `GamePresenceService`.
- **Rationale**: Delegated control already depends on live connection state, but bot assignments must survive server restarts, reloads, and state restoration. Persisting assignment references alongside game state satisfies the feature's replay/reload requirements without trying to turn `GamePresenceService` into a durable store.
- **Alternatives considered**:
  - Keep assignments only in `GamePresenceService`: rejected because assignments would be lost on process restart and could not satisfy reload scenarios.
  - Create a separate assignments table: rejected as unnecessary additional storage complexity for metadata that is only meaningful in the context of a game.

## Decision 4: Resolve bot choices from authoritative phase snapshots and validate before commit

- **Decision**: For each eligible bot phase, build a phase-specific decision context from the current authoritative game state, enumerate legal actions, send only that sanitized context plus bot strategy text to OpenAI, and require server validation against the latest legal action set before commit.
- **Rationale**: This enforces server authority, limits hallucination damage, reduces prompt ambiguity, and ensures the returned choice can never bypass normal game validation.
- **Alternatives considered**:
  - Send a broader unbounded serialized game object: rejected because it increases prompt size, ambiguity, and parsing risk.
  - Trust model output directly: rejected because it violates multiplayer integrity and would allow illegal or stale actions.

## Decision 5: Use deterministic fallbacks for every AI-backed phase

- **Decision**: Implement explicit fallbacks for every AI-assisted phase: auto-select the sole legal action, choose a legal region when region choice fails, choose no-purchase or no-bid/pass when purchase or auction resolution fails, and compute sells through a deterministic built-in evaluation.
- **Rationale**: The feature's primary value is continuity of play. Timeouts, malformed model output, or stale choices must not stall the table.
- **Alternatives considered**:
  - Block until OpenAI returns a parseable answer: rejected because it can freeze live multiplayer turns.
  - Require manual intervention after AI failure: rejected because it defeats the purpose of unattended disconnected-seat recovery.

## Decision 6: Keep sell behavior non-AI and deterministic

- **Decision**: Implement sell resolution with built-in server logic that ranks legal railroad sales by least negative effect on access and monopoly position, then applies a stable tie-breaker.
- **Rationale**: The spec explicitly requests deterministic low-impact sales, and this decision is easier to test, faster to execute, and safer than asking the model to reason about a constrained optimization problem during a forced liquidation path.
- **Alternatives considered**:
  - Let OpenAI choose a railroad to sell: rejected because determinism and regression testing are more important than stylistic variability in forced-sale scenarios.
  - Choose an arbitrary legal railroad: rejected because it would undercut the stated product behavior.

## Decision 7: Use optimistic concurrency for shared bot-definition edits

- **Decision**: Use Azure Table ETags on `BotsTable` records and surface update conflicts back to the dashboard UI so concurrent edits do not silently overwrite one another.
- **Rationale**: The global bot library is editable by any signed-in user, and the spec explicitly forbids silent loss of concurrent changes.
- **Alternatives considered**:
  - Last-write-wins updates: rejected because they violate FR-032.
  - Distributed locking: rejected as unnecessary complexity for simple dashboard CRUD.

## Decision 8: Keep assignments as live references and clear them when the referenced bot disappears

- **Decision**: Store only the bot-definition identifier in the active assignment. At decision time, resolve the current bot definition from `BotsTable`. If the definition is missing, clear or disable the assignment and stop further bot decisions until a valid bot is assigned again.
- **Rationale**: This matches the clarified live-link requirement and makes edits immediately visible to active assignments. It also gives deterministic handling for deletion.
- **Alternatives considered**:
  - Snapshot the strategy into the assignment: rejected because it conflicts with the clarified requirement.
  - Keep running the last cached definition after deletion: rejected because the deleted definition is no longer part of the global library and would be invisible to users.