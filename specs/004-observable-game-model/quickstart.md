# Quickstart: Observable Game Engine Object Model

**Feature**: 004-observable-game-model  
**Date**: 2026-03-04

## Prerequisites

- .NET 8 SDK
- Existing `Boxcars.slnx` solution checked out
- Branch: `004-observable-game-model`

## Project Location

```
src/Boxcars.GameEngine/           # Class library (already scaffolded)
tests/Boxcars.GameEngine.Tests/   # xUnit test project (already scaffolded)
```

Both projects already exist in the solution with correct references. The GameEngine project has `Azure.Data.Tables` and `Microsoft.Extensions.DependencyInjection.Abstractions` — the DI abstractions package can be removed since GameEngine is now a plain object model, not a DI-registered service.

## Build & Test

```powershell
dotnet build Boxcars.slnx
dotnet test Boxcars.slnx
```

## Usage Example: Create a New Game

```csharp
// Load the map
var mapLoadResult = mapParserService.Parse(rb3FileContent);
var map = mapLoadResult.Definition!;

// Create engine with default random provider
var random = new DefaultRandomProvider();
var engine = new GameEngine(map, ["Alice", "Bob", "Charlie"], random);

// Inspect state
Console.WriteLine(engine.GameStatus);           // InProgress
Console.WriteLine(engine.Players.Count);        // 3
Console.WriteLine(engine.Players[0].Name);      // Alice
Console.WriteLine(engine.Players[0].Cash);      // 20000
Console.WriteLine(engine.Players[0].HomeCity.Name);  // (random city)
Console.WriteLine(engine.Railroads.Count);      // 28 (from map)
Console.WriteLine(engine.CurrentTurn.ActivePlayer.Name);  // Alice
Console.WriteLine(engine.CurrentTurn.Phase);    // DrawDestination
```

## Usage Example: Subscribe to Changes

```csharp
var player = engine.Players[0];

player.PropertyChanged += (sender, e) =>
{
    if (e.PropertyName == nameof(Player.Cash))
        Console.WriteLine($"{player.Name} cash changed to ${player.Cash}");
};

player.OwnedRailroads.CollectionChanged += (sender, e) =>
{
    Console.WriteLine($"{player.Name} now owns {player.OwnedRailroads.Count} railroads");
};

engine.DestinationAssigned += (sender, e) =>
{
    Console.WriteLine($"{e.Player.Name} assigned destination: {e.City.Name}");
};
```

## Usage Example: Play a Turn

```csharp
// Phase: DrawDestination
var destination = engine.DrawDestination();
Console.WriteLine($"Destination: {destination.Name}");

// Phase: Roll (now that destination is assigned, suggest and save a route)
var route = engine.SuggestRoute();
engine.SaveRoute(route);

// Roll dice
var dice = engine.RollDice();
Console.WriteLine($"Rolled {dice.Total} ({string.Join("+", dice.WhiteDice)})");

// Phase: Move
engine.MoveAlongRoute(dice.Total);  // Move full distance

// If not arrived at destination, end turn
if (engine.CurrentTurn.Phase == TurnPhase.EndTurn)
    engine.EndTurn();

// If arrived: Arrival → Purchase → UseFees → EndTurn handled automatically or via:
if (engine.CurrentTurn.Phase == TurnPhase.Purchase)
{
    engine.DeclinePurchase();  // or engine.BuyRailroad(someRailroad);
    // Phase advances to UseFees (auto-resolved) then EndTurn
    engine.EndTurn();
}
```

## Usage Example: Deterministic Testing

```csharp
// Queue specific dice rolls and region/city draws
var fixedRandom = new FixedRandomProvider();
fixedRandom.QueueDiceRoll(7);    // First roll: 7
fixedRandom.QueueDiceRoll(10);   // Second roll: 10
fixedRandom.QueueWeightedDraw(2); // Region index 2
fixedRandom.QueueWeightedDraw(5); // City index 5

var engine = new GameEngine(map, ["Test1", "Test2"], fixedRandom);
// All random outcomes are now deterministic and predictable
```

## Usage Example: Save and Restore

```csharp
// Save
var snapshot = engine.ToSnapshot();
var json = JsonSerializer.Serialize(snapshot);
// Store json in Azure Table Storage...

// Restore
var restored = JsonSerializer.Deserialize<GameState>(json)!;
var engine2 = GameEngine.FromSnapshot(restored, map, new DefaultRandomProvider());
// engine2 is fully functional with all observable properties wired up
```

## Key Design Decisions

1. **Not a service**: `GameEngine` is `new`'d directly, not registered in DI. Callers manage its lifetime.
2. **Synchronous**: All methods are sync. No `Task`, no `async`. Persistence is the caller's responsibility.
3. **Observable**: All mutable properties fire `PropertyChanged`. All collections fire `CollectionChanged`. All domain-specific actions fire custom events.
4. **Immutable references**: `MapDefinition`, `Name`, `HomeCity`, railroad definitions are readonly after construction.
5. **Deterministic testing**: Inject `FixedRandomProvider` to control all random outcomes.
