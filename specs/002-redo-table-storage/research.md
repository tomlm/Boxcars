# Research: Redo Table Storage

## Decision 1: Use a two-table model only (`UsersTable`, `GamesTable`)
- Decision: Consolidate persistence to exactly two Azure Table Storage tables and remove/de-scope other table dependencies for this feature.
- Rationale: Matches feature requirement and constitution simplicity principle, reduces cross-table consistency burden.
- Alternatives considered:
  - Keep existing index/helper tables (`UserEmailIndex`, `NicknameIndex`, game-player index): rejected due to extra write paths and drift risk.
  - Introduce new dedicated event table: rejected because requirement explicitly limits table set.

## Decision 2: Keep heterogenous entities in `GamesTable`
- Decision: Store both `GameEntity` (immutable setup) and `GameEventEntity` (timeline + snapshot) in `GamesTable` under `PartitionKey = GameId`.
- Rationale: Co-locates game setup and timeline for efficient game load and ordered history retrieval by partition.
- Alternatives considered:
  - Separate table per entity type: rejected by scope constraint.
  - Store snapshots only in latest record outside timeline: rejected due to rollback/history requirements.

## Decision 3: Event ordering key strategy
- Decision: Use UTC-sortable event row keys with monotonic ordering semantics (`Event_{UTC sortable tick}`) and query in ascending order.
- Rationale: Preserves action-history chronology and deterministic restore path for reconnection.
- Alternatives considered:
  - Random GUID row keys + server timestamp sort: rejected as non-deterministic under close write timing.
  - Integer sequence from separate allocator: rejected for added coordination complexity.

## Decision 4: Snapshot persistence on every game event
- Decision: Include serialized mutable engine snapshot on each persisted `GameEventEntity`.
- Rationale: Ensures direct resume after crash/disconnect from most recent event without replaying full history.
- Alternatives considered:
  - Snapshot every N events: rejected due to slower recovery and possible state gaps.
  - Event-only sourcing without snapshots: rejected due to resume latency and complexity.

## Decision 5: Authentication-managed `UserEntity` lifecycle
- Decision: Authentication flow creates or looks up `UserEntity` keyed by email in `UsersTable`; gameplay consumes those profiles.
- Rationale: Keeps user identity canonical and consistent with existing auth ownership boundaries.
- Alternatives considered:
  - Game-service-created users on demand: rejected because identity source should remain auth system.
  - Separate profile table: rejected by two-table scope.

## Decision 6: Seed Beatles mock users at table initialization
- Decision: Prepopulate `UsersTable` with Paul, Ringo, George, John (`@beatles.com`) in environments where initialization is enabled.
- Rationale: Enables immediate Create Game testing and consistent demo data.
- Alternatives considered:
  - Manual ad-hoc inserts: rejected due to setup inconsistency.
  - Seeding in production always-on path: rejected to avoid non-production identity data in live environments.

## Decision 7: Create Game UI contract requires slot-complete, unique assignments
- Decision: Require each slot to have one selected user and one selected color; reject duplicate users/colors prior to create.
- Rationale: Prevents invalid rosters and avoids downstream game-state correction.
- Alternatives considered:
  - Allow partial slots and auto-fill: rejected as ambiguous and not requested.
  - Resolve conflicts only after create: rejected because invalid records would already be persisted.

## Decision 8: Persistence-before-broadcast ordering for UI actions
- Decision: For every UI-triggered action, game engine persists `GameEventEntity` first, then publishes updates to connected clients.
- Rationale: Guarantees reconnection-safe state and consistent event history under failures.
- Alternatives considered:
  - Broadcast then persist asynchronously: rejected due to potential divergence after crash.
  - Dual-write from UI and engine: rejected because client must not be authoritative.
