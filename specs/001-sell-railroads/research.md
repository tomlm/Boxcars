# Research: Forced Railroad Sales and Auctions

## Decision 1: Model forced liquidation inside the existing `UseFees` flow

- **Decision**: Keep insufficient-funds handling inside the authoritative fee-resolution flow instead of creating a separate top-level turn phase.
- **Rationale**: The current engine already resolves fees from `TurnPhase.UseFees`, and the spec requires repeated sales until the debt is covered. Extending the existing fee flow avoids a second rule path and keeps bankruptcy elimination tied directly to unpaid fees.
- **Alternatives considered**:
  - Add a new top-level `SellRailroad` phase: rejected because it duplicates fee-resolution state transitions and increases turn-flow complexity.
  - Let the UI decide when to interrupt fee payment: rejected because it violates the multiplayer-first and server-authoritative principles.

## Decision 2: Persist auction progress as turn snapshot state, not transient hub state

- **Decision**: Store current forced-sale and auction progress in persisted game snapshot data that is rebroadcast through the existing authoritative game state pipeline.
- **Rationale**: Auctions span multiple player turns within a single sale workflow, and players may disconnect or reconnect mid-auction. Persisted turn-state fields make auction progress recoverable and ensure every client renders the same seller, selected railroad, eligible participants, last bidder, and next actor.
- **Alternatives considered**:
  - Keep auction progress only in SignalR/hub memory: rejected because reconnection and event replay would be incomplete.
  - Resolve the entire auction in one synchronous action: rejected because the spec requires per-player bid, pass, and drop-out decisions.

## Decision 3: Extend the existing `PlayerAction` queue for auction commands

- **Decision**: Implement sell-to-bank, auction start, auction bid, auction pass, and auction drop-out as queued player actions processed by `GameEngineService`.
- **Rationale**: The application already persists and propagates gameplay actions through `PlayerAction` and `GameEngineService`. Using the same queue preserves authorization, event logging, state snapshots, and SignalR fan-out with the least new infrastructure.
- **Alternatives considered**:
  - Add a separate auction controller or hub RPC path: rejected because it would bypass existing authorization and persistence patterns.
  - Encode pass and drop-out as special bid values: rejected because it obscures intent and makes validation less explicit.

## Decision 4: Reuse network coverage services for sale-impact advisory UI

- **Decision**: Compute the `Network` tab and railroad sale-impact projections from the same network coverage services already used for purchase analysis, with projected ownership removal instead of projected acquisition.
- **Rationale**: The constitution requires advisory outputs to be derived from shared authoritative inputs. Reusing the current coverage-analysis pipeline avoids a parallel client-side rule implementation and keeps buy/sell network projections consistent.
- **Alternatives considered**:
  - Calculate sale impact only in the Blazor page: rejected because it would duplicate domain logic on the client.
  - Show only static railroad details with no projection: rejected because it fails the spec's requirement to show access and monopoly impact.

## Decision 5: Use existing player activity flags for insolvency elimination

- **Decision**: Represent insolvency elimination by updating the existing player activity/bankruptcy state and authorization checks rather than removing the player record from the game.
- **Rationale**: The spec says eliminated players remain able to watch the game. Existing player snapshot flags already distinguish active and bankrupt states, so the safest path is to prevent further gameplay actions while continuing to broadcast state to that user.
- **Alternatives considered**:
  - Remove the player entirely from persisted game state: rejected because it complicates historical events, turn order, and spectator visibility.
  - Leave the player active with zero actions available: rejected because auction eligibility and turn authorization would remain ambiguous.

## Decision 6: Keep bank-sale fallback explicit and deterministic

- **Decision**: A no-bid auction resolves as a bank sale at half price using the same authoritative ownership-transfer logic as a direct bank sale.
- **Rationale**: The spec requires the exact same half-price outcome when no player bids. Using one authoritative sale-resolution path prevents drift between direct bank sales and auction fallback.
- **Alternatives considered**:
  - Treat no-bid auctions as cancelled and force the seller to choose again: rejected because it contradicts the spec.
  - Create separate direct-sale and fallback-sale resolution code paths: rejected because it introduces unnecessary duplication.
