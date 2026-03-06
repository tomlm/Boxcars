# Contract: GameEngine Public API

**Feature**: 004-observable-game-model  
**Date**: 2026-03-04  
**Type**: C# class library public API

## Overview

The `GameEngine` class is the sole entry point for all game interactions. It is an in-memory observable object model — not a service, not async, not DI-registered. Callers create instances directly or restore from snapshots.

## Construction

```csharp
// New game
var engine = new GameEngine(mapDefinition, playerNames, randomProvider);

// Restore from snapshot
var engine = GameEngine.FromSnapshot(gameState, mapDefinition, randomProvider);
```

### Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `mapDefinition` | `MapDefinition` | Yes | Loaded map with regions, cities, railroads, train dots |
| `playerNames` | `IReadOnlyList<string>` | Yes | 2–6 unique, non-empty player names |
| `randomProvider` | `IRandomProvider` | Yes | Dice/random source (inject `FixedRandomProvider` for tests) |

### Validation on construction
- Throws `ArgumentException` if fewer than 2 or more than 6 names
- Throws `ArgumentException` if any name is null/empty/whitespace
- Throws `ArgumentException` if duplicate names exist (case-insensitive)

## Observable Properties

All properties that change during gameplay implement `INotifyPropertyChanged`. Collections implement `INotifyCollectionChanged` via `ObservableCollection<T>`.

```csharp
public class GameEngine : ObservableBase
{
    // Observable scalar properties
    public GameStatus GameStatus { get; }
    public Turn CurrentTurn { get; }
    public bool AllRailroadsSold { get; }
    public Player? Winner { get; }

    // Observable collections
    public ObservableCollection<Player> Players { get; }
    public ObservableCollection<Railroad> Railroads { get; }

    // Readonly
    public MapDefinition MapDefinition { get; }
}
```

## Action Methods

All action methods are synchronous, validate preconditions, and throw `InvalidOperationException` on violation without mutating state. On success, they mutate state and fire `PropertyChanged` / `CollectionChanged` / domain events synchronously.

### `RollDice()`

```csharp
public DiceResult RollDice();
```

| Precondition | Error |
|---|---|
| `GameStatus` is `InProgress` | "Game is not in progress" |
| `CurrentTurn.Phase` is `Roll` | "Not in Roll phase" |

**Effects**: Sets `CurrentTurn.DiceResult`, `CurrentTurn.MovementRemaining`, advances phase to `Move`. Checks bonus eligibility based on locomotive type.

**Returns**: The `DiceResult` for convenience (also available on `CurrentTurn.DiceResult`).

---

### `MoveAlongRoute(int steps)`

```csharp
public void MoveAlongRoute(int steps);
```

| Precondition | Error |
|---|---|
| `CurrentTurn.Phase` is `Move` | "Not in Move phase" |
| `steps > 0` | "Steps must be positive" |
| `steps <= CurrentTurn.MovementRemaining` | "Exceeds movement remaining" |
| Active player has an `ActiveRoute` | "No active route set" |
| Movement does not reuse segments | "Segment reuse violation" |

**Effects**: Advances player position along route by `steps` mileposts. Updates `Player.CurrentCity`, `CurrentTurn.MovementRemaining`. Tracks segments ridden for use fee calculation. If player arrives at destination, triggers arrival (payoff, phase change). Raises `UsageFeeCharged` if applicable during fee resolution.

---

### `DrawDestination()`

```csharp
public CityDefinition DrawDestination();
```

| Precondition | Error |
|---|---|
| `CurrentTurn.Phase` is `DrawDestination` | "Not in DrawDestination phase" |
| Active player has no current destination | "Player already has a destination" |

**Effects**: Rolls on region table then city table using `IRandomProvider`. Sets `Player.Destination`. Raises `DestinationAssigned` event. Returns the assigned city.

**Special rule**: If the drawn region is the same as the player's current region, the implementation auto-redraws (per simplified rule adaptation).

---

### `SuggestRoute()`

```csharp
public Route SuggestRoute();
```

| Precondition | Error |
|---|---|
| Active player has a `Destination` | "No destination assigned" |

**Effects**: Computes cheapest-cost route from player's `CurrentCity` to `Destination` using the map's graph. Does NOT change any state. Returns a `Route` object.

---

### `SaveRoute(Route route)`

```csharp
public void SaveRoute(Route route);
```

| Precondition | Error |
|---|---|
| `route` is not null | "Route cannot be null" |
| Active player has a `Destination` | "No destination assigned" |

**Effects**: Sets `Player.ActiveRoute` to the given route. Fires `PropertyChanged` for `ActiveRoute`.

---

### `BuyRailroad(Railroad railroad)`

```csharp
public void BuyRailroad(Railroad railroad);
```

| Precondition | Error |
|---|---|
| `CurrentTurn.Phase` is `Purchase` | "Not in Purchase phase" |
| `railroad.Owner` is null | "Railroad is already owned" |
| `railroad.IsPublic` is false | "Public railroads cannot be purchased" |
| `Player.Cash >= railroad.PurchasePrice` | "Insufficient funds" |

**Effects**: Deducts `PurchasePrice` from player's `Cash`. Sets `railroad.Owner` to player. Adds railroad to `Player.OwnedRailroads`. Updates `AllRailroadsSold` if applicable. Advances phase to `UseFees`.

---

### `UpgradeLocomotive(LocomotiveType target)`

```csharp
public void UpgradeLocomotive(LocomotiveType target);
```

| Precondition | Error |
|---|---|
| `CurrentTurn.Phase` is `Purchase` | "Not in Purchase phase" |
| `target` is a valid upgrade from current type | "Invalid upgrade path" |
| Player has sufficient cash | "Insufficient funds" |

**Upgrade paths and costs**:
- Freight → Express: $4,000
- Freight → Superchief: $20,000
- Express → Superchief: $20,000

**Effects**: Deducts cost. Sets `Player.LocomotiveType`. Raises `LocomotiveUpgraded` event. Advances phase to `UseFees`.

---

### `AuctionRailroad(Railroad railroad)`

```csharp
public void AuctionRailroad(Railroad railroad);
```

| Precondition | Error |
|---|---|
| Railroad is owned by the active player | "Player does not own this railroad" |
| Phase is `Purchase` or `UseFees` | "Cannot auction in current phase" |

**Effects**: Raises `AuctionStarted` event with railroad and eligible bidders. Auction state tracked internally; resolved via `ResolveAuction()`.

---

### `DeclinePurchase()`

```csharp
public void DeclinePurchase();
```

| Precondition | Error |
|---|---|
| `CurrentTurn.Phase` is `Purchase` | "Not in Purchase phase" |

**Effects**: Advances phase to `UseFees` without making a purchase.

---

### `EndTurn()`

```csharp
public void EndTurn();
```

| Precondition | Error |
|---|---|
| `CurrentTurn.Phase` is `EndTurn` | "Not in EndTurn phase" |

**Effects**: Advances `CurrentTurn.ActivePlayer` to the next active (non-bankrupt) player. Increments `TurnNumber`. Sets phase to `DrawDestination` (or `Roll` if player already has a destination). Raises `TurnStarted` event.

---

## Snapshot / Persistence

```csharp
// Serialize
GameState snapshot = engine.ToSnapshot();
string json = JsonSerializer.Serialize(snapshot);

// Deserialize
GameState restored = JsonSerializer.Deserialize<GameState>(json);
GameEngine engine = GameEngine.FromSnapshot(restored, mapDefinition, randomProvider);
```

`GameState` is a plain DTO — no interfaces, no base classes, no events. It captures all mutable state needed to reconstruct a fully functional `GameEngine`.

## Domain Events

Events are exposed as standard C# events on `GameEngine`:

```csharp
public event EventHandler<DestinationAssignedEventArgs>? DestinationAssigned;
public event EventHandler<DestinationReachedEventArgs>? DestinationReached;
public event EventHandler<UsageFeeChargedEventArgs>? UsageFeeCharged;
public event EventHandler<AuctionStartedEventArgs>? AuctionStarted;
public event EventHandler<AuctionCompletedEventArgs>? AuctionCompleted;
public event EventHandler<TurnStartedEventArgs>? TurnStarted;
public event EventHandler<GameOverEventArgs>? GameOver;
public event EventHandler<PlayerBankruptEventArgs>? PlayerBankrupt;
public event EventHandler<LocomotiveUpgradedEventArgs>? LocomotiveUpgraded;
```

All events fire synchronously on the same thread as the mutation (FR-008).
