using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Domain;
using Boxcars.Engine.Tests.TestDoubles;
using GE = Boxcars.Engine.Domain.GameEngine;

namespace Boxcars.Engine.Tests.Fixtures;

/// <summary>
/// Shared test setup helpers for creating GameEngine instances with known state.
/// </summary>
public static class GameEngineFixture
{
    /// <summary>Default player names for tests.</summary>
    public static readonly string[] DefaultPlayerNames = ["Alice", "Bob"];

    /// <summary>Three player names.</summary>
    public static readonly string[] ThreePlayerNames = ["Alice", "Bob", "Charlie"];

    /// <summary>
    /// Creates a minimal but valid MapDefinition with 2 regions, 4 cities, 2 railroads,
    /// and a connected route graph suitable for testing.
    /// </summary>
    public static MapDefinition CreateTestMap()
    {
        var map = new MapDefinition
        {
            Name = "TestMap",
            Version = "1.0"
        };

        // Regions
        map.Regions.Add(new RegionDefinition { Name = "Northeast", Code = "NE", Probability = 0.5 });
        map.Regions.Add(new RegionDefinition { Name = "Southeast", Code = "SE", Probability = 0.5 });

        // Cities (with payout indices for payout table)
        map.Cities.Add(new CityDefinition { Name = "New York", RegionCode = "NE", Probability = 0.5, PayoutIndex = 0, MapDotIndex = 0 });
        map.Cities.Add(new CityDefinition { Name = "Boston", RegionCode = "NE", Probability = 0.5, PayoutIndex = 1, MapDotIndex = 1 });
        map.Cities.Add(new CityDefinition { Name = "Miami", RegionCode = "SE", Probability = 0.5, PayoutIndex = 2, MapDotIndex = 0 });
        map.Cities.Add(new CityDefinition { Name = "Atlanta", RegionCode = "SE", Probability = 0.5, PayoutIndex = 3, MapDotIndex = 1 });

        // Railroads
        map.Railroads.Add(new RailroadDefinition { Index = 0, Name = "Pennsylvania Railroad", ShortName = "PRR" });
        map.Railroads.Add(new RailroadDefinition { Index = 1, Name = "Baltimore & Ohio", ShortName = "B&O" });

        // Train dots — create a connected network
        // Region 0 (NE): dots 0,1,2,3,4
        // Region 1 (SE): dots 0,1,2,3,4
        for (int r = 0; r < 2; r++)
        {
            for (int d = 0; d < 5; d++)
            {
                map.TrainDots.Add(new TrainDot
                {
                    Id = $"{r}:{d}",
                    RegionIndex = r,
                    DotIndex = d,
                    X = r * 100 + d * 20,
                    Y = r * 100 + d * 20
                });
            }
        }

        // Route segments — connect dots into a linear path
        // PRR: NE:0 -> NE:1 -> NE:2 -> NE:3 -> NE:4
        for (int d = 0; d < 4; d++)
        {
            map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition
            {
                RailroadIndex = 0,
                StartRegionIndex = 0,
                StartDotIndex = d,
                EndRegionIndex = 0,
                EndDotIndex = d + 1
            });
        }

        // B&O: SE:0 -> SE:1 -> SE:2 -> SE:3 -> SE:4
        for (int d = 0; d < 4; d++)
        {
            map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition
            {
                RailroadIndex = 1,
                StartRegionIndex = 1,
                StartDotIndex = d,
                EndRegionIndex = 1,
                EndDotIndex = d + 1
            });
        }

        // Cross-connection: NE:4 -> SE:0 (PRR)
        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition
        {
            RailroadIndex = 0,
            StartRegionIndex = 0,
            StartDotIndex = 4,
            EndRegionIndex = 1,
            EndDotIndex = 0
        });

        return map;
    }

    /// <summary>
    /// Creates a FixedRandomProvider that will produce deterministic home city assignments
    /// and allows further queueing for test-specific scenarios.
    /// </summary>
    public static FixedRandomProvider CreateDeterministicRandom(int playerCount = 2)
    {
        var random = new FixedRandomProvider();
        // Queue home city draws for each player (region draw + city draw per player)
        for (int i = 0; i < playerCount; i++)
        {
            random.QueueWeightedDraw(i % 2); // Alternate between regions
            random.QueueWeightedDraw(i % 2); // Pick city within region
        }
        return random;
    }

    /// <summary>
    /// Creates a GameEngine with a minimal test map and deterministic random.
    /// Returns the engine and the random provider for further test setup.
    /// </summary>
    public static (GE Engine, FixedRandomProvider Random) CreateTestEngine(
        string[]? playerNames = null,
        int? playerCount = null)
    {
        var names = playerNames ?? DefaultPlayerNames;
        var count = playerCount ?? names.Length;
        var map = CreateTestMap();
        var random = CreateDeterministicRandom(count);
        var engine = new GE(map, names, random);
        return (engine, random);
    }

    /// <summary>
    /// Advances the engine to a specific turn phase by performing necessary actions.
    /// Queues appropriate random values on the provider.
    /// </summary>
    public static void AdvanceToPhase(GE engine, FixedRandomProvider random, TurnPhase targetPhase)
    {
        if (engine.CurrentTurn.Phase == targetPhase) return;

        switch (targetPhase)
        {
            case TurnPhase.Roll:
                if (engine.CurrentTurn.Phase == TurnPhase.DrawDestination)
                {
                    // Queue destination draw (region + city)
                    random.QueueWeightedDraw(1); // Different region
                    random.QueueWeightedDraw(0); // First city in region
                    engine.DrawDestination();
                }
                break;

            case TurnPhase.Move:
                AdvanceToPhase(engine, random, TurnPhase.Roll);
                if (engine.CurrentTurn.Phase == TurnPhase.Roll)
                {
                    // Save a route first for movement
                    var route = engine.SuggestRoute();
                    engine.SaveRoute(route);
                    random.QueueDiceRoll(3, 4); // Roll 7
                    engine.RollDice();
                }
                break;

            case TurnPhase.Purchase:
                AdvanceToPhase(engine, random, TurnPhase.Move);
                if (engine.CurrentTurn.Phase == TurnPhase.Move)
                {
                    // Move minimum to exhaust movement
                    while (engine.CurrentTurn.Phase == TurnPhase.Move && engine.CurrentTurn.MovementRemaining > 0)
                    {
                        int steps = Math.Min(engine.CurrentTurn.MovementRemaining, 1);
                        try { engine.MoveAlongRoute(steps); }
                        catch { break; }
                    }
                }
                break;

            case TurnPhase.EndTurn:
                AdvanceToPhase(engine, random, TurnPhase.Purchase);
                if (engine.CurrentTurn.Phase == TurnPhase.Purchase)
                {
                    engine.DeclinePurchase();
                }
                break;
        }
    }
}
