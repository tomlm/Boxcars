# Data Model: Observable Game Engine Object Model

**Feature**: 004-observable-game-model  
**Date**: 2026-03-04

## Entity Relationship Overview

```
GameEngine (root)
├── GameStatus : GameStatus
├── Players : ObservableCollection<Player>
│   └── Player
│       ├── Name : string
│       ├── Cash : int
│       ├── HomeCity : CityDefinition
│       ├── CurrentCity : CityDefinition
│       ├── Destination : CityDefinition?
│       ├── ActiveRoute : Route?
│       ├── OwnedRailroads : ObservableCollection<Railroad>
│       ├── LocomotiveType : LocomotiveType
│       ├── IsActive : bool
│       ├── IsBankrupt : bool
│       └── UsedSegments : HashSet<SegmentKey> (internal)
├── Railroads : ObservableCollection<Railroad>
│   └── Railroad
│       ├── Name : string
│       ├── ShortName : string
│       ├── Owner : Player?
│       ├── PurchasePrice : int
│       ├── IsPublic : bool
│       └── Definition : RailroadDefinition (readonly ref)
├── CurrentTurn : Turn
│   └── Turn
│       ├── ActivePlayer : Player
│       ├── TurnNumber : int
│       ├── Phase : TurnPhase
│       ├── DiceResult : DiceResult?
│       ├── MovementRemaining : int
│       ├── BonusRollAvailable : bool
│       └── SegmentsRiddenThisTurn : HashSet<SegmentKey> (internal)
├── AllRailroadsSold : bool
└── Winner : Player?
```

## Entities

### GameEngine

Root object; owns the full object graph. All action methods are invoked on this object.

| Property | Type | Observable | Description |
|----------|------|-----------|-------------|
| `GameStatus` | `GameStatus` | Yes | NotStarted, InProgress, Completed |
| `Players` | `ObservableCollection<Player>` | Collection | All players in turn order |
| `Railroads` | `ObservableCollection<Railroad>` | Collection | All railroads from map definition |
| `CurrentTurn` | `Turn` | Yes | Current turn state |
| `AllRailroadsSold` | `bool` | Yes | True when all non-public railroads are owned |
| `Winner` | `Player?` | Yes | Set when game ends |
| `MapDefinition` | `MapDefinition` | No (readonly) | Reference to loaded map |

**Action Methods**:
| Method | Phase Required | Description |
|--------|---------------|-------------|
| `RollDice()` | Roll | Rolls dice, sets DiceResult and MovementRemaining |
| `MoveAlongRoute(int steps)` | Move | Advances position along active route |
| `DrawDestination()` | DrawDestination | Rolls on region/city tables, assigns destination |
| `SuggestRoute()` | Any (with destination) | Computes cheapest route recommendation |
| `SaveRoute(Route route)` | Any (with destination) | Commits a route as active route |
| `BuyRailroad(Railroad railroad)` | Purchase | Buys unowned railroad |
| `UpgradeLocomotive(LocomotiveType target)` | Purchase | Upgrades locomotive |
| `AuctionRailroad(Railroad railroad)` | Purchase / UseFees | Initiates auction |
| `DeclinePurchase()` | Purchase | Skips purchase opportunity |
| `EndTurn()` | EndTurn | Advances to next player |

---

### Player

| Property | Type | Observable | Description |
|----------|------|-----------|-------------|
| `Name` | `string` | No (immutable) | Player display name |
| `Cash` | `int` | Yes | Current cash in dollars |
| `HomeCity` | `CityDefinition` | No (immutable after init) | Randomly assigned starting city |
| `CurrentCity` | `CityDefinition` | Yes | Current location on the map |
| `Destination` | `CityDefinition?` | Yes | Current destination (null when between destinations) |
| `ActiveRoute` | `Route?` | Yes | Currently planned route |
| `OwnedRailroads` | `ObservableCollection<Railroad>` | Collection | Railroads owned by this player |
| `LocomotiveType` | `LocomotiveType` | Yes | Freight, Express, or Superchief |
| `IsActive` | `bool` | Yes | True if still in the game (not bankrupt) |
| `IsBankrupt` | `bool` | Yes | True if eliminated |

**Internal state** (not exposed as observable, used by engine logic):
| Field | Type | Description |
|-------|------|-------------|
| `UsedSegments` | `HashSet<SegmentKey>` | Track segments used since last destination arrival (non-reuse rule) |

**Validation rules**:
- `Name` must be non-empty and unique across all players
- `Cash` cannot go below $0 without triggering bankruptcy/auction
- `Destination` is null after arrival, before next draw

---

### Railroad

| Property | Type | Observable | Description |
|----------|------|-----------|-------------|
| `Index` | `int` | No (immutable) | Railroad index from map definition |
| `Name` | `string` | No (immutable) | Full railroad name |
| `ShortName` | `string` | No (immutable) | Abbreviated name |
| `Owner` | `Player?` | Yes | Current owner (null = bank-held) |
| `PurchasePrice` | `int` | No (immutable) | Cost to purchase |
| `IsPublic` | `bool` | No (immutable) | Public railroads cannot be purchased |
| `Definition` | `RailroadDefinition` | No (readonly ref) | Reference to map definition data |

**Validation rules**:
- Only unowned, non-public railroads can be purchased
- Owner changes only through `BuyRailroad()` or auction resolution

---

### Turn

| Property | Type | Observable | Description |
|----------|------|-----------|-------------|
| `ActivePlayer` | `Player` | Yes | Whose turn it is |
| `TurnNumber` | `int` | Yes | Current turn number (1-based) |
| `Phase` | `TurnPhase` | Yes | Current phase of the turn |
| `DiceResult` | `DiceResult?` | Yes | Result of last dice roll |
| `MovementRemaining` | `int` | Yes | Mileposts left to move this roll |
| `BonusRollAvailable` | `bool` | Yes | Whether a bonus roll can/must be taken |

**Internal state**:
| Field | Type | Description |
|-------|------|-------------|
| `RailroadsRiddenThisTurn` | `HashSet<int>` | Railroad indices used this turn (for use fee calculation) |
| `RailroadsRiddenByOwner` | `Dictionary<Player, HashSet<int>>` | Groups railroads by owner for fee calculation |

---

### Route

| Property | Type | Observable | Description |
|----------|------|-----------|-------------|
| `Nodes` | `IReadOnlyList<RouteNode>` | No (immutable) | Ordered list of nodes from start to destination |
| `Segments` | `IReadOnlyList<RouteSegment>` | No (immutable) | Ordered list of segments connecting nodes |
| `TotalCost` | `int` | No (immutable) | Estimated total use fee cost |

Route is immutable once created — a new route replaces the old one entirely.

---

### RouteNode

| Property | Type | Description |
|----------|------|-------------|
| `NodeId` | `string` | Map node identifier (format: `regionIndex:dotIndex`) |
| `City` | `CityDefinition?` | City at this node, if any |

---

### RouteSegment

| Property | Type | Description |
|----------|------|-------------|
| `FromNodeId` | `string` | Starting node |
| `ToNodeId` | `string` | Ending node |
| `RailroadIndex` | `int` | Railroad this segment belongs to |

---

### DiceResult

| Property | Type | Description |
|----------|------|-------------|
| `WhiteDice` | `int[]` | Individual white die values (2 dice) |
| `RedDie` | `int?` | Red bonus die value (null if not rolled) |
| `Total` | `int` | Sum of all dice |
| `IsDoubles` | `bool` | Whether white dice show the same value |

---

## Enumerations

### GameStatus
```
NotStarted → InProgress → Completed
```
| Value | Description |
|-------|-------------|
| `NotStarted` | Game created but not started |
| `InProgress` | Game is actively being played |
| `Completed` | Game has a winner or all but one player bankrupt |

### TurnPhase
```
DrawDestination → Roll → Move → Arrival → Purchase → UseFees → EndTurn
```
| Value | Description |
|-------|-------------|
| `DrawDestination` | Player must draw a destination |
| `Roll` | Player must roll dice |
| `Move` | Player is moving along route |
| `Arrival` | Player has arrived at destination (auto-resolves payoff) |
| `Purchase` | Player may buy a railroad or upgrade locomotive |
| `UseFees` | Use fees are being calculated and paid |
| `EndTurn` | Turn is ending, advance to next player |

### LocomotiveType
| Value | Cost | Dice | Bonus Rule |
|-------|------|------|-----------|
| `Freight` | Starting | 2d6 | Bonus roll on double-6 only |
| `Express` | $4,000 | 2d6 | Bonus roll on any doubles |
| `Superchief` | $20,000 | 3d6 | No separate bonus (red die already included) |

## Domain Events

| Event | Payload | Trigger |
|-------|---------|---------|
| `DestinationAssigned` | Player, City | After `DrawDestination()` |
| `DestinationReached` | Player, City, PayoutAmount | Player arrives at destination |
| `UsageFeeCharged` | Rider (Player), Owner (Player?), Amount, RailroadIndices | During UseFees phase |
| `AuctionStarted` | Railroad, EligibleBidders | When auction is initiated |
| `AuctionCompleted` | Railroad, Winner, WinningBid | When auction resolves |
| `TurnStarted` | Player, TurnNumber | At start of each turn |
| `GameOver` | Winner | When win condition met or last player standing |
| `PlayerBankrupt` | Player | When player eliminated |
| `LocomotiveUpgraded` | Player, OldType, NewType | After locomotive upgrade |

## Serialization Snapshot (GameState DTO)

The `GameState` DTO captures all mutable state for persistence:

```
GameState
├── GameStatus : string
├── TurnNumber : int
├── ActivePlayerIndex : int
├── AllRailroadsSold : bool
├── WinnerIndex : int?
├── Players : PlayerState[]
│   ├── Name : string
│   ├── Cash : int
│   ├── HomeCityName : string
│   ├── CurrentCityName : string
│   ├── DestinationCityName : string?
│   ├── LocomotiveType : string
│   ├── IsActive : bool
│   ├── IsBankrupt : bool
│   ├── OwnedRailroadIndices : int[]
│   ├── ActiveRoute : RouteState?
│   └── UsedSegments : string[]  (serialized SegmentKeys)
├── RailroadOwnership : Dictionary<int, int?>  (railroadIndex → playerIndex)
└── TurnState
    ├── Phase : string
    ├── DiceResult : DiceResultState?
    ├── MovementRemaining : int
    ├── BonusRollAvailable : bool
    └── RailroadsRiddenThisTurn : int[]
```

**Notes**:
- Cities referenced by name (stable across serialization)
- Players referenced by index position
- Railroads referenced by index from map definition
- Route serialized as ordered list of node IDs + segment railroad indices
