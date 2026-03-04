# Feature Specification: Observable Game Engine Object Model

**Feature Branch**: `004-observable-game-model`  
**Created**: 2026-03-04  
**Status**: Draft  
**Input**: User description: "GameEngine as observable object model with bindable state, persistable objects, action methods, and events"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Initialize and Inspect a Game (Priority: P1)

As a player, I can create a new game and immediately see a fully constructed game object model with players, railroads, and turn state so the UI can bind to the game state and display the board.

**Why this priority**: The object model is the foundation of all gameplay. Without a constructible, inspectable game state, no interactions or rendering are possible.

**Independent Test**: Can be fully tested by creating a `GameEngine` instance with 2–6 player names, verifying all child objects (players, railroads, turn tracker) exist with correct defaults, and confirming all property values are accessible.

**Acceptance Scenarios**:

1. **Given** a list of 2–6 player names, **When** a new game is initialized, **Then** the game engine exposes a `Players` collection containing one player object per name with starting cash of $20,000, no owned railroads, no assigned destination, and a randomly assigned `HomeCity` matching their `CurrentCity`.
2. **Given** a new game is initialized, **When** the game state is inspected, **Then** the game exposes a `Railroads` collection containing all railroads defined by the loaded map.
3. **Given** a new game is initialized, **When** the game state is inspected, **Then** the game exposes a `CurrentTurn` object identifying the active player, the current turn phase, and the turn number (starting at 1).
4. **Given** fewer than 2 or more than 6 player names, **When** initialization is attempted, **Then** the engine rejects the request with a clear validation error.

---

### User Story 2 - Observe State Changes in Real Time (Priority: P1)

As a UI developer, I can bind to any game state property and receive change notifications whenever the underlying value changes so the display stays current without polling.

**Why this priority**: Observability is the defining capability of this object model; without it the engine is just a data bag.

**Independent Test**: Can be tested by subscribing to `PropertyChanged` on any game object, performing a state-mutating action, and verifying that the notification fires with the correct property name and updated value.

**Acceptance Scenarios**:

1. **Given** a player object, **When** the player's cash changes (e.g., after buying a railroad), **Then** a `PropertyChanged` event fires for `Cash` with the new value.
2. **Given** a railroad object, **When** an owner is assigned, **Then** a `PropertyChanged` event fires for `Owner`.
3. **Given** the turn tracker, **When** the active player advances, **Then** `PropertyChanged` fires for `ActivePlayer`, `TurnPhase`, and `TurnNumber` as applicable.
4. **Given** the player's owned-railroads collection, **When** a railroad is added or removed, **Then** a `CollectionChanged` event fires on that collection.

---

### User Story 3 - Roll Dice and Move (Priority: P2)

As a player, I can call `RollDice()` on the game engine during my turn and see the dice result applied to my movement state, with all property changes propagated to the UI.

**Why this priority**: Dice rolling is the most frequent player action and the first gameplay interaction exercised each turn.

**Independent Test**: Can be tested by calling `RollDice()` on a game in the Roll phase, verifying dice values are populated, the player's position or movement allowance updates, and the turn phase advances.

**Acceptance Scenarios**:

1. **Given** it is my turn and the phase is Roll, **When** I call `RollDice()`, **Then** the engine generates a dice result, applies it to the current turn, and advances the phase to Move.
2. **Given** the phase is Move and I have movement remaining, **When** I call `MoveAlongRoute(steps)`, **Then** the engine advances my position along my active route by the given steps, deducts usage fees for railroads I don't own, and updates `CurrentCity` and `MovementRemaining` observably.
3. **Given** it is not my turn, **When** I call `RollDice()`, **Then** the engine rejects the action and raises no state change.
4. **Given** dice have already been rolled this turn, **When** `RollDice()` is called again, **Then** the engine rejects the duplicate action.

---

### User Story 4 - Suggest and Save a Route (Priority: P2)

As a player, I can call `SuggestRoute()` to get a recommended route to my destination and `SaveRoute()` to commit that route as my planned path.

**Why this priority**: Route planning is the core strategic loop and depends on the object model exposing route and destination objects.

**Independent Test**: Can be tested by assigning a destination city, calling `SuggestRoute()`, inspecting the returned route, calling `SaveRoute()`, and verifying the player's active route updates with change notifications.

**Acceptance Scenarios**:

1. **Given** a player has a destination assigned, **When** `SuggestRoute()` is called, **Then** the engine computes and returns a route object with ordered node list and total cost.
2. **Given** a suggested route exists, **When** `SaveRoute()` is called, **Then** the player's `ActiveRoute` property updates and `PropertyChanged` fires.
3. **Given** no destination is assigned, **When** `SuggestRoute()` is called, **Then** the engine rejects the action with a clear error.

---

### User Story 5 - Buy or Auction a Railroad (Priority: P3)

As a player, I can call `BuyRailroad()` to purchase an unowned railroad or `AuctionRailroad()` to put a railroad up for auction, with ownership and financial state updating observably.

**Why this priority**: Railroad ownership drives the economic engine and depends on players, railroads, and cash all being observable.

**Independent Test**: Can be tested by calling `BuyRailroad()` with sufficient funds, verifying the railroad's owner changes, the player's cash decreases, and the player's owned-railroads collection updates — all with change notifications.

**Acceptance Scenarios**:

1. **Given** a railroad is unowned and the player has sufficient cash, **When** `BuyRailroad(railroad)` is called, **Then** the railroad's `Owner` changes to the player, the player's `Cash` decreases by the purchase price, and the railroad is added to the player's `OwnedRailroads` collection — all with appropriate notifications.
2. **Given** a railroad is already owned, **When** `BuyRailroad(railroad)` is called, **Then** the engine rejects the action.
3. **Given** a player declines to buy a railroad, **When** `AuctionRailroad(railroad)` is called, **Then** the engine raises an `AuctionStarted` event exposing the railroad and eligible bidders.
4. **Given** an auction is in progress, **When** a player wins the bid, **Then** ownership and cash transfer observably as in a direct purchase.

---

### User Story 6 - Persist and Restore Game State (Priority: P3)

As a host, I can save the current game state to storage and later restore it, resuming play exactly where it left off with all object model state intact.

**Why this priority**: Persistence is required for games that span multiple sessions but depends on the object model being fully established first.

**Independent Test**: Can be tested by creating a game, performing several actions, serializing the state, deserializing into a new engine instance, and verifying all property values match and are observable.

**Acceptance Scenarios**:

1. **Given** a game in progress with player state, owned railroads, and turn history, **When** the game is serialized, **Then** all object state is captured including player cash, owned railroads, destinations, routes, turn number, and phase.
2. **Given** a serialized game state, **When** it is deserialized into a new `GameEngine` instance, **Then** all property values match the original and all objects fire change notifications when mutated.
3. **Given** a restored game, **When** play resumes, **Then** the turn order, active player, and phase continue from the saved position.

---

### Edge Cases

- Initializing a game with duplicate player names is rejected with a validation error.
- Calling any action method (e.g., `RollDice()`, `BuyRailroad()`) when it is not the caller's turn or the wrong phase raises a clear error without corrupting state.
- Buying a railroad when cash is insufficient is rejected; cash and ownership remain unchanged.
- Serializing a game mid-auction captures auction state so it can resume after restore.
- An empty or null player name list is rejected at initialization.
- Restoring from corrupted or incomplete serialized data produces a clear error rather than a partially initialized engine.
- Rolling dice with a deterministic provider (for testing) produces repeatable results.
- Concurrent property-change subscriptions from multiple UI bindings all receive notifications.
- A player who reaches $200,000 cash but has not returned to their home city does not win; the game continues.
- If multiple players could satisfy the win condition on the same turn (e.g., payouts), the active player (whose turn it is) wins.
- Calling `MoveAlongRoute(steps)` with more steps than `MovementRemaining` is rejected.
- When a player arrives at their destination, payout is automatic; the player cannot decline or defer collection.

## Requirements *(mandatory)*

### Functional Requirements

#### Object Model Structure

- **FR-001**: System MUST provide a `GameEngine` root object that exposes all game state as an observable object graph.
- **FR-002**: `GameEngine` MUST expose a `Players` collection of player objects, each with observable properties: `Name`, `Cash`, `CurrentCity`, `Destination`, `ActiveRoute`, `OwnedRailroads`, `IsActive`, and `LocomotiveType` (Freight, Express, Superchief).
- **FR-003**: `GameEngine` MUST expose a `Railroads` collection of railroad objects, each with observable properties: `Name`, `ShortName`, `Owner`, `PurchasePrice`, and `Segments`.
- **FR-004**: `GameEngine` MUST expose a `CurrentTurn` object with observable properties: `ActivePlayer`, `TurnNumber`, `Phase`, `DiceResult`, and `MovementRemaining`.
- **FR-005**: `GameEngine` MUST expose a `GameStatus` observable property reflecting the overall state (NotStarted, InProgress, Completed).

#### Observability

- **FR-006**: All game objects MUST implement `INotifyPropertyChanged` so that scalar property mutations raise `PropertyChanged` notifications.
- **FR-007**: All collection properties (e.g., `OwnedRailroads`, `Players`) MUST raise `CollectionChanged` notifications when items are added or removed.
- **FR-008**: Change notifications MUST fire synchronously on the same call path as the mutation, so subscribers see consistent state.

#### Action Methods

- **FR-009**: `GameEngine` MUST expose a `RollDice()` method that generates a dice result, applies it to the current turn, and advances the turn phase from Roll to Move.
- **FR-010**: `GameEngine` MUST expose a `SuggestRoute()` method that computes a cheapest-route recommendation from the active player's current position to their destination.
- **FR-011**: `GameEngine` MUST expose a `SaveRoute()` method that commits a route as the active player's planned path.
- **FR-012**: `GameEngine` MUST expose a `BuyRailroad(railroad)` method that transfers ownership to the active player and deducts cash.
- **FR-013**: `GameEngine` MUST expose a `AuctionRailroad(railroad)` method that initiates an auction when the active player declines a purchase.
- **FR-026**: `GameEngine` MUST expose a `DrawDestination()` method that rolls on the map's region lookup table then city lookup table (using the injected random provider) to assign a new destination to the active player, raising `PropertyChanged` on the player's `Destination` and a `DestinationAssigned` domain event.
- **FR-027**: `GameEngine` MUST expose a `MoveAlongRoute(steps)` method that advances the active player's position along their active route by the given number of train-dot steps, updating `CurrentCity` and `MovementRemaining`. When traversing segments of a railroad not owned by the moving player, the engine MUST automatically deduct the usage fee from the player's cash and credit it to the railroad's owner (or the bank if unowned), raising `PropertyChanged` on both players' `Cash` and a `UsageFeeCharged` domain event.
- **FR-028**: When a player arrives at their destination city (via `MoveAlongRoute`), the engine MUST automatically look up the payout amount from the map's payout table (based on origin city and destination city), credit the payout to the player's `Cash`, clear the player's `Destination`, and raise a `DestinationReached` domain event carrying the payout amount. The player then draws a new destination via `DrawDestination()` (or proceeds to win-condition check if eligible).
- **FR-029**: `GameEngine` MUST expose an `UpgradeLocomotive(target)` method that applies valid locomotive upgrades during the Purchase phase and deducts the correct upgrade cost.
- **FR-030**: `GameEngine` MUST expose a `DeclinePurchase()` method that skips purchase and advances to fee resolution without mutating ownership state.
- **FR-031**: `GameEngine` MUST expose an `EndTurn()` method that advances to the next active player and begins the next turn with the correct phase.
- **FR-014**: Action methods MUST validate preconditions (correct turn, correct phase, sufficient funds) and reject invalid calls without mutating state.
- **FR-015**: Action methods MUST advance the turn phase appropriately after successful execution (DrawDestination → Roll → Move → Arrival → Purchase → UseFees → EndTurn).

#### Events

- **FR-016**: `GameEngine` MUST raise domain events for actions that are not fully representable through property changes alone (e.g., `AuctionStarted`, `AuctionCompleted`, `GameOver`, `TurnStarted`, `DestinationAssigned`, `DestinationReached`, `UsageFeeCharged`). The `GameOver` event fires when a player meets the win condition ($200,000+ cash and returns to home city).
- **FR-017**: Domain events MUST carry contextual data (e.g., `AuctionStarted` includes the railroad and eligible bidders).

#### Persistence

- **FR-018**: The entire game object model MUST be serializable to a format suitable for persistent storage.
- **FR-019**: The game object model MUST be deserializable back into a fully functional, observable `GameEngine` instance.
- **FR-020**: Persistence MUST be compatible with Azure Table Storage (serialized state stored as a blob property).

#### Validation & Initialization

- **FR-021**: `GameEngine` initialization MUST accept a map definition and a list of 2–6 unique player names.
- **FR-022**: Initialization MUST populate all railroads from the map definition and assign starting defaults to each player ($20,000 cash, no railroads, no destination). Each player's `HomeCity` MUST be randomly assigned (using the injected random provider) by rolling on the map's region and city lookup tables, independently of the destination draw. The player's `CurrentCity` starts at their `HomeCity`.
- **FR-023**: `GameEngine` MUST support injecting a custom dice/random provider for deterministic testing.

#### Win Condition

- **FR-024**: The game ends when a player accumulates at least $200,000 in cash and returns to their home city (the city they started from). That player wins and `GameStatus` transitions to Completed.
- **FR-025**: `GameEngine` MUST expose a `HomeCity` property on each `Player` representing their randomly assigned starting city, determined during initialization via a separate roll from the destination draw.

### Key Entities

- **GameEngine**: Root object; owns the full object graph. Exposes players, railroads, current turn, game status, action methods, and domain events.
- **Player**: Represents a participant. Observable properties: name, cash, current city, home city, destination, active route, owned railroads, movement type, active status.
- **Railroad**: Represents a purchasable railroad. Observable properties: name, short name, owner, purchase price, route segments.
- **Turn**: Represents the current turn state. Observable properties: active player, turn number, phase (DrawDestination/Roll/Move/Arrival/Purchase/UseFees/EndTurn), dice result, movement remaining.
- **Route**: Represents a planned path. Contains ordered node list, segment details, and total cost.
- **Destination**: Represents a target city with region and city identifiers.

## Assumptions

- Starting cash is $20,000 per player, following standard Rail Baron rules.
- Player order is determined by the order names are provided at initialization.
- Railroad purchase prices are derived from the map definition data (payout index or a standard price table).
- The map definition structure (`MapDefinition`, `RailroadDefinition`, etc.) already exists and is reused as-is for populating the object model.
- Dice rolling uses two six-sided dice by default (2d6) and three six-sided dice (3d6) for players with the three-die movement type.
- Observable collections use `ObservableCollection<T>` or equivalent.
- Serialization format is JSON; the specific serialization framework is an implementation detail.
- Auction mechanics follow standard Rail Baron rules: all other players may bid, highest bidder wins.
- Turn phase progression follows: DrawDestination → Roll → Move → Arrival → Purchase → UseFees → EndTurn, cycling to the next player's DrawDestination (or Roll if destination is already assigned).

## Clarifications

### Session 2026-03-04

- Q: What determines when the game ends (win condition)? → A: Standard Rail Baron rules — a player wins by accumulating at least $200,000 cash and then returning to their home city.
- Q: How are destinations assigned to players? → A: Roll-based — engine rolls on the map's region lookup table then city lookup table (standard Rail Baron destination draw).
- Q: How does movement work after rolling dice? → A: Separate `MoveAlongRoute(steps)` method advances position along the active route, automatically deducting usage fees to railroad owners.
- Q: How does the player get paid upon reaching their destination? → A: Automatic payout — engine looks up payout from the map's payout table (origin/destination pair) and credits the player's cash upon arrival.
- Q: How is a player's home city determined? → A: Randomly assigned during initialization via a separate roll on the map's region/city tables, independent of destination draws.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of public properties on `GameEngine`, `Player`, `Railroad`, and `Turn` objects fire `PropertyChanged` when mutated.
- **SC-002**: All action methods (`RollDice`, `MoveAlongRoute`, `DrawDestination`, `SuggestRoute`, `SaveRoute`, `BuyRailroad`, `AuctionRailroad`, `UpgradeLocomotive`, `DeclinePurchase`, `EndTurn`) reject invalid-state calls without altering any observable property.
- **SC-003**: A game with 6 players can be serialized and deserialized with all property values matching the original within one round-trip.
- **SC-004**: Unit tests cover every public method and property on the object model with at least one positive and one negative test case.
- **SC-005**: All collection mutations (add/remove railroad ownership, player list at init) fire `CollectionChanged` notifications verified by tests.
- **SC-006**: A full game lifecycle (init → roll → move → buy railroad → end turn → next player → … → game over) can be driven entirely through method calls on the object model.
