# Research: Observable Game Engine Object Model

**Feature**: 004-observable-game-model  
**Date**: 2026-03-04

## R1: Observable Pattern in .NET for Game Engine

**Decision**: Use `INotifyPropertyChanged` with a shared `ObservableBase` base class and `ObservableCollection<T>` for collection properties.

**Rationale**: `INotifyPropertyChanged` is the standard .NET observable interface, natively understood by Blazor's data binding, requires zero third-party dependencies, and aligns with the constitution's Simplicity principle (Principle III). A shared base class (`ObservableBase`) eliminates boilerplate `PropertyChanged` invocations via a `SetField<T>` helper method using `CallerMemberName`.

**Alternatives considered**:
- **ReactiveUI / System.Reactive**: Powerful but introduces a heavy dependency (ReactiveUI, Rx.NET) for a problem that `INotifyPropertyChanged` solves natively. Violates Principle III (YAGNI).
- **Custom event per property**: More explicit but creates an explosion of event types and doesn't integrate with Blazor binding.
- **CommunityToolkit.Mvvm `[ObservableProperty]` source generator**: Generates `INotifyPropertyChanged` boilerplate automatically. Appealing but adds a NuGet dependency for code generation that can be achieved with a ~20-line base class. Consider adopting later if boilerplate becomes painful.

---

## R2: Serialization Strategy for Azure Table Storage

**Decision**: Use `System.Text.Json` to serialize the entire `GameEngine` state into a JSON string stored as a blob property (`string` column) on an Azure Table Storage entity. Deserialization reconstructs a fully functional `GameEngine` with all observable wiring intact.

**Rationale**: `System.Text.Json` is built into .NET 8, has source-generator support for AOT/trimming, and handles records and POCOs natively. Azure Table Storage `string` properties support up to 32KB (UTF-16), which is sufficient for game state JSON. If state exceeds this, it can be compressed or split across properties — but for 6 players and 28 railroads, typical state is well under 10KB.

**Alternatives considered**:
- **Newtonsoft.Json**: Mature but adds an external dependency when System.Text.Json is sufficient.
- **MessagePack / Protobuf**: Binary formats give smaller payloads but lose human readability for debugging and don't integrate as cleanly with Azure Table Storage string properties.
- **Separate entities per player/railroad**: Spreads state across many rows, complicating atomic save/restore. A single blob is simpler and atomic.

**Implementation notes**:
- Create a `GameState` snapshot DTO that captures all mutable state (no circular references).
- `GameEngine` provides `ToSnapshot()` → `GameState` and a static `FromSnapshot(GameState, MapDefinition)` factory.
- The snapshot DTO is serialization-friendly (no interfaces, no base classes, no events).
- The `GameEngine` constructor from snapshot rewires all observable properties and events.

---

## R3: Payout Table Implementation

**Decision**: Implement the payout table as a static lookup indexed by `PayoutIndex` pairs. The `CityDefinition.PayoutIndex` values already parsed from the map file serve as indices into a symmetric payout matrix.

**Rationale**: The official Rail Baron payoff chart (https://www.railgamefans.com/rbp/rbpchart.htm) is a symmetric matrix of city×city payoff amounts. Each city has a `PayoutIndex` already stored in `CityDefinition`. The payout amounts can be encoded as a static 2D array or dictionary keyed by `(fromIndex, toIndex)`.

**Alternatives considered**:
- **Embed payout data in the map file**: Would require extending the `.RB3` parser. The payout table is game-rule data, not map-layout data — better kept separate.
- **Calculate payouts from distance**: Not how Rail Baron works — payouts are fixed values from a lookup chart, not distance-based.

**Implementation notes**:
- Create a `PayoutTable` class with a `GetPayout(int fromPayoutIndex, int toPayoutIndex)` method.
- Payout data sourced from the official payoff chart and hardcoded as a static table.
- The table is symmetric: `Payout(A, B) == Payout(B, A)`.

---

## R4: Dice and Random Provider Pattern

**Decision**: Define an `IRandomProvider` interface with methods for rolling dice (2d6, 3d6) and performing weighted probability lookups (region draw, city draw). Inject via constructor for testability.

**Rationale**: The spec requires deterministic testing (FR-023). A single interface covers all randomness needs: dice rolls and destination draws. The default implementation uses `System.Random`. A test double (`FixedRandomProvider`) queues predetermined outcomes.

**Alternatives considered**:
- **Separate `IDiceProvider` and `IDestinationDrawProvider`**: Over-segmented — both use the same underlying randomness. One interface is simpler.
- **Pass `Random` directly**: Doesn't allow queuing specific sequences for tests without subclassing.

**Interface shape**:
```csharp
public interface IRandomProvider
{
    int RollDice(int count, int sides);  // Returns sum of N dice with S sides
    int[] RollDiceIndividual(int count, int sides);  // Returns individual die values
    int WeightedDraw(IReadOnlyList<double> probabilities);  // Returns index
}
```

---

## R5: Turn Phase Model (from Official Rules)

**Decision**: Model turn phases as a state machine: `DrawDestination → Roll → Move → Arrival → Purchase → UseFees → EndTurn`. Some phases are auto-advanced (e.g., `Arrival` triggers payout automatically).

**Rationale**: The official Rail Baron rules (https://www.railgamefans.com/rbp/rb21rules.htm) define a clear turn sequence: destination lookup → movement (dice roll + move) → arrival (payoff) → purchase opportunity → use fee payment → end turn. The `ResolveFinancials` phase from the spec maps to `Arrival` (collect payoff) + `Purchase` (buy railroad) + `UseFees` (pay track fees).

**Phase transitions**:
```
DrawDestination → Roll → Move → [Arrival → Purchase → UseFees] → EndTurn
                                  ↑ only if reached destination
```

**Key rules mapped to phases**:
- **DrawDestination**: Player draws destination via CITY LOOKUP if they don't have one.
- **Roll**: Roll 2d6 (Freight/Express) or 3d6 (Superchief). Doubles may trigger bonus.
- **Move**: Player moves along route counting mileposts. Non-reuse enforced. Must use full roll unless arriving at destination.
- **Arrival**: If reached destination — stop, collect payoff from payout table. Check for bounce-out eligibility.
- **Purchase**: May buy one unowned railroad or upgrade locomotive.
- **UseFees**: Pay use fees for all opponent/bank railroads ridden this turn ($1000 bank, $5000/$10,000 opponent).
- **EndTurn**: Advance to next player.

---

## R6: Locomotive Types and Upgrades

**Decision**: Model locomotive as an enum (`Freight`, `Express`, `Superchief`) on the `Player` object with observable property changes. Upgrade is an action method `UpgradeLocomotive()` called during the Purchase phase.

**Rationale**: Per official rules:
- **Freight** (starting): Roll 2d6. Bonus roll (red die) only on double-6s.
- **Express** ($4,000 upgrade): Roll 2d6. Bonus roll on any doubles.
- **Superchief** ($20,000 upgrade): Roll 3d6 (all three dice at once, no separate bonus).

This maps cleanly to the spec's `MovementType` property (2-die or 3-die) but we should use `LocomotiveType` to be more precise and derive the dice count from it.

**Alternatives considered**:
- **Just track dice count (2 or 3)**: Loses the distinction between Freight and Express (both roll 2d6 but have different bonus rules).

---

## R7: Use Fee Calculation

**Decision**: Track all railroad segments ridden during a turn. At the UseFees phase, compute fees per the official rules: $1,000 flat for any bank-held railroads used, $5,000 per opponent whose railroads were used (increases to $10,000 after all non-public railroads sold).

**Rationale**: Use fees in Rail Baron are NOT per-segment or per-distance. They are flat rates per owner category:
- Bank-held (including Public): $1,000 total regardless of how many bank railroads used
- Opponent-owned: $5,000 per opponent (not per railroad) whose tracks were used
- After all non-public railroads sold: opponent rate increases to $10,000

**Implementation notes**:
- `Turn` tracks a `Set<int>` of railroad indices ridden this turn.
- At `UseFees` phase, group by owner → compute fees → deduct from player, credit to owners.
- Track whether all non-public railroads have been sold (global game state flag).

---

## R8: Non-Reuse Rule

**Decision**: Track used segments per player across turns until destination arrival. Enforce during `MoveAlongRoute()` — reject moves that would reuse a segment.

**Rationale**: Per official rules, a player cannot ride a given track segment again (even in reverse) until after arriving at their destination. This is per-player tracking that resets on arrival. This affects route validation and movement legality.

**Implementation notes**:
- `Player` maintains a `HashSet<(string fromNodeId, string toNodeId)>` of used segments (normalized so direction doesn't matter).
- `MoveAlongRoute()` validates each step against this set.
- Set clears on destination arrival.

---

## R9: Declare / Rover Mechanics (Scope Decision)

**Decision**: Declare and rover mechanics are **out of scope** for the initial implementation of this feature. The core engine will support the full turn loop (draw destination → roll → move → arrival → purchase → use fees → end turn) and the win condition check, but declaring, alternate destinations, and rover play will be added in a subsequent feature.

**Rationale**: Declare/rover is a complex end-game mechanic that requires:
- Alternate destination tracking
- Special movement rules for declared players
- Inter-player interaction (rover requires moving through opponent's position)
- $50,000 penalty and status change

The core game loop works without it — a player simply wins when they arrive at their home city with $200,000+ via normal destination draws. Declare/rover is an optimization that lets skilled players shortcut the endgame. Implementing the core loop first follows Principle III (Ship Fast).

**Alternatives considered**:
- **Include declare/rover now**: Would significantly increase scope and delay shipping a playable core engine.

---

## R10: Bounce-Out Mechanics (Scope Decision)

**Decision**: Bounce-out movement is **included** in scope. When a player arrives at their destination using fewer mileposts than their white dice roll, and they qualify for a bonus roll, they may continue moving after drawing their next destination.

**Rationale**: Bounce-out is a core movement mechanic that occurs frequently and significantly impacts game pacing. Excluding it would noticeably deviate from the rules (violating Principle I). The implementation is straightforward: after arrival, check remaining movement + bonus eligibility, draw new destination, continue moving.

---

## R11: Establishment Rule (Scope Decision)

**Decision**: Establishment (fee grandfathering when prices increase) is **deferred** to a later feature. Initial implementation uses the simpler fee structure: $1,000 bank, $5,000 opponent, $10,000 after all sold — without tracking establishment status.

**Rationale**: Establishment is a nuanced edge-case rule that only matters after the specific event of "all non-public railroads sold." It adds tracking complexity (per-player per-railroad establishment status at each milepost) with minimal impact on most game turns. The simplified fee model is correct for the majority of the game.

---

## R12: Bankruptcy Mechanics

**Decision**: Include bankruptcy detection. If a player cannot pay use fees after auctioning/selling all railroads, they are bankrupt and eliminated. If only one player remains, they win.

**Rationale**: Bankruptcy is a core game mechanic that determines player elimination. Without it, the game has no consequence for running out of money. The implementation is straightforward: check cash after use fee calculation, trigger auction if insufficient, eliminate if still insufficient after all railroads sold.

---

## R13: Threading and Synchronization

**Decision**: `GameEngine` is single-threaded by design. All mutations and notifications occur on the calling thread. The Blazor Server host is responsible for marshalling calls to the engine onto the appropriate synchronization context.

**Rationale**: FR-008 requires synchronous notifications on the same call path. The engine is an in-memory object model, not a service. Blazor Server runs on a single synchronization context per circuit. No internal locking needed — the caller (SignalR hub / Blazor component) is responsible for serializing access.

**Alternatives considered**:
- **Internal locking**: Adds complexity without benefit since Blazor Server already serializes calls per circuit.
- **Async action methods**: Unnecessary — all operations are in-memory with no I/O. Persistence is a separate concern handled by the caller.
