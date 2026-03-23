# Research: Game Creation Settings

## Decision: Persist settings directly on nullable `GameEntity` properties

**Rationale**: Azure Table Storage handles simple scalar columns well, and this feature adds a bounded set of immutable settings. Storing them directly on `GameEntity` makes row inspection, debugging, and targeted querying easier while keeping all game setup on the owning row.

**Alternatives considered**:

- Keep a single `SettingsJson` payload: rejected because it hides first-class game setup behind serialization, makes row inspection harder, and adds avoidable parse/serialization glue for a small fixed set of values.
- Create a separate `GameSettingsEntity` row or table: rejected because the feature does not need independent lifecycle or query behavior beyond the owning game.

## Decision: Use nullable persisted columns plus a runtime resolver with default fallback

**Rationale**: Existing game rows will not have the new setting columns. Using nullable persisted properties lets runtime code distinguish missing legacy values from explicit `false` or `0` inputs, then normalize everything into one fully populated runtime settings object.

**Alternatives considered**:

- Fail to load legacy games until migrated: rejected because it would break existing games and violates the spec requirement for backward compatibility.
- Run a storage migration first: rejected because it adds operational work and delay for a feature that can safely default on read.
- Use non-nullable direct properties on `GameEntity`: rejected because absent legacy values would collapse into CLR defaults like `false` and `0`, making missing data indistinguishable from intentional values.

## Decision: Inject settings into the authoritative engine as one immutable rules object

**Rationale**: The current engine already accepts a single rule parameter for `SuperchiefPrice`. Expanding that seam to a full immutable runtime settings object is the smallest change that centralizes rule evaluation and keeps server-side logic authoritative while allowing persistence to remain flat on `GameEntity`.

**Alternatives considered**:

- Keep app-wide `PurchaseRulesOptions` and add more global options: rejected because the requested rules are per-game, not per-deployment.
- Pass many scalar parameters through engine constructors and helper methods: rejected because it scatters rule ownership and makes restore/create paths easier to drift apart.

## Decision: Keep mutable gameplay snapshots separate from immutable game settings

**Rationale**: Event snapshots already capture mutable game and turn state. Immutable settings belong to the game record, not each event payload. Loading settings from the owning `GameEntity` keeps snapshots smaller and avoids duplicated rule payloads across every persisted event.

**Alternatives considered**:

- Embed settings into every `GameState` snapshot: rejected because it duplicates immutable data across all events and increases drift risk if serialization evolves.
- Recompute settings from UI state on demand: rejected because it would make the client or request path part of rule resolution.

## Decision: Extend the create-game request contract with a typed settings object and validate on both client and server

**Rationale**: The create-game page already assembles a typed `CreateGameRequest`. Adding a typed settings payload keeps the request explicit, allows UI defaults and validation messaging, and still leaves the server as the final validator before mapping values onto `GameEntity` columns.

**Alternatives considered**:

- Submit raw JSON from the page: rejected because it weakens validation and makes the request contract harder to reason about.
- Validate only in the UI: rejected because multiplayer rule integrity depends on server-owned validation.

## Decision: Drive board projections and advice text from the same resolved settings used by gameplay

**Rationale**: Cash visibility, fee previews, engine prices, and purchase advice are advisory outputs that must align with authoritative rules. Routing them through one resolved settings object avoids a second client-side rule system.

**Alternatives considered**:

- Keep hard-coded UI thresholds and only update the engine: rejected because the board would mislead players whenever a game uses non-default settings.
- Duplicate a separate UI settings model disconnected from engine resolution: rejected because it invites drift.

## Decision: Implement home-selection configuration by extending current region-choice behavior instead of replacing it

**Rationale**: The engine already has a `PendingRegionChoice` flow for destination-region replacement and already randomizes home-city assignment. Extending those paths for `HomeCityChoice` and later home/destination swap support preserves existing map logic and keeps new behavior inside the authoritative engine.

**Alternatives considered**:

- Implement home selection entirely in the Blazor page or map UI: rejected because home-city legality and uniqueness are rule decisions that belong on the server.
- Introduce a separate pre-engine setup subsystem: rejected because it adds complexity before the current engine seams are exhausted.
