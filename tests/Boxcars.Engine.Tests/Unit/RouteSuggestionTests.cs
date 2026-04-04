using System.Reflection;
using Boxcars.Engine.Domain;
using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Persistence;
using Boxcars.Engine.Tests.Fixtures;
using Boxcars.Engine.Tests.TestDoubles;

namespace Boxcars.Engine.Tests.Unit;

/// <summary>
/// Tests for route suggestion and save route (T033, T034).
/// </summary>
public class RouteSuggestionTests
{
    [Fact]
    public void SuggestRoute_WithDestination_ReturnsRoute()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();

        // Draw destination
        random.QueueWeightedDraw(1); // SE region
        random.QueueWeightedDraw(0); // First city
        engine.DrawDestination();

        var route = engine.SuggestRoute();

        Assert.NotNull(route);
        Assert.True(route.NodeIds.Count > 0);
    }

    [Fact]
    public void SuggestRoute_NoDestination_ThrowsInvalidOperation()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        // No destination drawn yet, but we need to be able to call SuggestRoute
        // It requires a destination
        var ex = Assert.Throws<InvalidOperationException>(() => engine.SuggestRoute());
        Assert.Contains("No destination assigned", ex.Message);
    }

    [Fact]
    public void SuggestRoute_DoesNotMutateState()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();

        random.QueueWeightedDraw(1);
        random.QueueWeightedDraw(0);
        engine.DrawDestination();

        var phase = engine.CurrentTurn.Phase;
        var playerRoute = engine.CurrentTurn.ActivePlayer.ActiveRoute;

        engine.SuggestRoute();

        // State should be unchanged
        Assert.Equal(phase, engine.CurrentTurn.Phase);
        Assert.Equal(playerRoute, engine.CurrentTurn.ActivePlayer.ActiveRoute);
    }

    [Fact]
    public void SuggestRoute_WhenPlanningTimeLimitIsReached_ReturnsFallbackRoute()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        random.QueueWeightedDraw(1);
        random.QueueWeightedDraw(0);
        engine.DrawDestination();

        var originalTimeLimit = GetRoutePlanningTimeLimit();

        try
        {
            SetRoutePlanningTimeLimit(TimeSpan.Zero);

            var route = engine.SuggestRoute();

            Assert.Equal([engine.CurrentTurn.ActivePlayer.CurrentNodeId!], route.NodeIds);
            Assert.Empty(route.Segments);
            Assert.Equal(0, route.TotalCost);
        }
        finally
        {
            SetRoutePlanningTimeLimit(originalTimeLimit);
        }
    }

    [Fact]
    public void SuggestRouteForPlayer_UsesCurrentPositionAfterMovement()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();

        random.QueueWeightedDraw(1);
        random.QueueWeightedDraw(0);
        engine.DrawDestination();

        var initialRoute = engine.SuggestRouteForPlayer(0);
        engine.SaveRoute(initialRoute);

        random.QueueDiceRoll(1, 1);
        engine.RollDice();
        engine.MoveAlongRoute(2);

        var updatedRoute = engine.SuggestRouteForPlayer(0);

        Assert.Equal(engine.Players[0].CurrentNodeId, updatedRoute.NodeIds[0]);
        Assert.Equal(initialRoute.Segments.Count - 2, updatedRoute.Segments.Count);
    }

    [Fact]
    public void SaveRoute_ValidRoute_SetsActiveRoute()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();

        random.QueueWeightedDraw(1);
        random.QueueWeightedDraw(0);
        engine.DrawDestination();

        var route = engine.SuggestRoute();
        engine.SaveRoute(route);

        Assert.Equal(route, engine.CurrentTurn.ActivePlayer.ActiveRoute);
    }

    [Fact]
    public void SaveRoute_NullRoute_ThrowsArgumentNull()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();

        random.QueueWeightedDraw(1);
        random.QueueWeightedDraw(0);
        engine.DrawDestination();

        Assert.Throws<ArgumentNullException>(() => engine.SaveRoute(null!));
    }

    [Fact]
    public void SaveRoute_NoDestination_ThrowsInvalidOperation()
    {
        var (engine, _) = GameEngineFixture.CreateTestEngine();

        var dummyRoute = new Route(new[] { "0:0" }, Array.Empty<RouteSegment>(), 0);

        // No destination drawn — should fail
        var ex = Assert.Throws<InvalidOperationException>(() => engine.SaveRoute(dummyRoute));
        Assert.Contains("No destination assigned", ex.Message);
    }

    [Fact]
    public void SaveRoute_FiresPropertyChanged()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        var player = engine.Players[0];
        var changedProps = new List<string>();

        random.QueueWeightedDraw(1);
        random.QueueWeightedDraw(0);
        engine.DrawDestination();

        player.PropertyChanged += (s, e) => changedProps.Add(e.PropertyName!);

        var route = engine.SuggestRoute();
        engine.SaveRoute(route);

        Assert.Contains("ActiveRoute", changedProps);
    }

    [Fact]
    public void SaveRoute_ResetsRouteProgressIndex()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();

        random.QueueWeightedDraw(1);
        random.QueueWeightedDraw(0);
        engine.DrawDestination();

        var route = engine.SuggestRoute();
        engine.SaveRoute(route);

        Assert.Equal(0, engine.CurrentTurn.ActivePlayer.RouteProgressIndex);
    }

    [Fact]
    public void SaveRoute_DoesNotCommitUsedSegmentsUntilMovementOccurs()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Roll);

        var player = engine.CurrentTurn.ActivePlayer;
        var route = engine.SuggestRoute();

        Assert.Empty(player.UsedSegments);

        engine.SaveRoute(route);

        Assert.Empty(player.UsedSegments);

        random.QueueDiceRoll(1, 1);
        engine.RollDice();
        engine.MoveAlongRoute(1);

        Assert.Single(player.UsedSegments);
    }

    [Fact]
    public void SuggestRouteForPlayer_UnfriendlyDestination_PrefersLowerExitCostRoute()
    {
        var engine = CreateUnfriendlyDestinationPlanningEngine();
        var player = engine.Players[0];
        var owner = engine.Players[1];

        foreach (var railroad in engine.Railroads.Where(railroad => railroad.Index is 2 or 3))
        {
            railroad.Owner = owner;
            owner.OwnedRailroads.Add(railroad);
        }

        player.CurrentNodeId = "0:0";
        player.CurrentCity = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Start", StringComparison.Ordinal));
        player.Destination = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Finish", StringComparison.Ordinal));
        player.TripOriginCity = player.CurrentCity;

        var route = engine.SuggestRouteForPlayer(player.Index);

        Assert.Equal([1, 2], route.Segments.Take(2).Select(segment => segment.RailroadIndex).ToArray());
        Assert.Equal(6000, route.TotalCost);

        var segmentKeys = route.Segments
            .Select(segment => new SegmentKey(segment.FromNodeId, segment.ToNodeId, segment.RailroadIndex))
            .ToList();
        Assert.Equal(segmentKeys.Count, segmentKeys.Distinct().Count());
    }

    [Fact]
    public void SuggestRouteForPlayer_PrefersMoreDirectRouteWhenDetourOnlySavesOneThousand()
    {
        var engine = CreateDirectnessPlanningEngine();
        var player = engine.Players[0];
        var ownedDetourRailroad = engine.Railroads.First(railroad => railroad.Index == 1);

        ownedDetourRailroad.Owner = player;
        player.OwnedRailroads.Add(ownedDetourRailroad);
        player.LocomotiveType = LocomotiveType.Superchief;
        player.CurrentNodeId = "0:0";
        player.CurrentCity = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Start", StringComparison.Ordinal));
        player.Destination = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Finish", StringComparison.Ordinal));
        player.TripOriginCity = player.CurrentCity;

        var route = engine.SuggestRouteForPlayer(player.Index);

        Assert.Equal([2], route.Segments.Select(segment => segment.RailroadIndex).ToArray());
        Assert.Equal(["0:0", "0:6"], route.NodeIds);
        Assert.Equal(3000, route.TotalCost);
    }

    [Fact]
    public void SuggestRouteForPlayer_PrefersFriendlyExitWhenHostileRailroadCanBeLeftForDestinationPath()
    {
        var engine = CreateHostileExitPlanningEngine();
        var player = engine.Players[0];
        var opponent = engine.Players[1];
        var hostileRailroad = engine.Railroads.First(railroad => railroad.Index == 1);

        hostileRailroad.Owner = opponent;
        opponent.OwnedRailroads.Add(hostileRailroad);
        player.CurrentNodeId = "0:0";
        player.CurrentCity = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Start", StringComparison.Ordinal));
        player.Destination = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Finish", StringComparison.Ordinal));
        player.TripOriginCity = player.CurrentCity;

        var route = engine.SuggestRouteForPlayer(player.Index);

        Assert.Equal([1, 2, 2], route.Segments.Select(segment => segment.RailroadIndex).ToArray());
        Assert.Equal(["0:0", "0:1", "0:2", "0:3"], route.NodeIds);
    }

    [Fact]
    public void SuggestRouteForPlayer_RejectsRoutesLongerThanMaximumSegments()
    {
        var engine = CreateLongPlanningEngine();
        var player = engine.Players[0];

        player.CurrentNodeId = "0:0";
        player.CurrentCity = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Start", StringComparison.Ordinal));
        player.Destination = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Finish", StringComparison.Ordinal));
        player.TripOriginCity = player.CurrentCity;

        var route = engine.SuggestRouteForPlayer(player.Index);

        Assert.Equal(["0:0"], route.NodeIds);
        Assert.Empty(route.Segments);
    }

    [Fact]
    public void SuggestRouteForPlayer_TieBreak_PrefersLowerCashOwner()
    {
        var engine = CreateStrategicTieBreakEngine();
        var player = engine.Players[0];
        var lowCashOwner = engine.Players[1];
        var highCashOwner = engine.Players[2];

        AssignOwnership(engine, 2, lowCashOwner);
        AssignOwnership(engine, 3, highCashOwner);
        lowCashOwner.Cash = 5_000;
        highCashOwner.Cash = 25_000;

        player.CurrentNodeId = "0:0";
        player.CurrentCity = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Start", StringComparison.Ordinal));
        player.Destination = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Finish", StringComparison.Ordinal));
        player.TripOriginCity = player.CurrentCity;

        var route = engine.SuggestRouteForPlayer(player.Index);

        Assert.Equal([1, 2], route.Segments.Take(2).Select(segment => segment.RailroadIndex).ToArray());
    }

    [Fact]
    public void SuggestRouteForPlayer_TieBreak_PrefersWeakerNetworkOwner()
    {
        var engine = CreateStrategicTieBreakEngine();
        var player = engine.Players[0];
        var weakerOwner = engine.Players[1];
        var strongerOwner = engine.Players[2];

        AssignOwnership(engine, 2, weakerOwner);
        AssignOwnership(engine, 3, strongerOwner);
        AssignOwnership(engine, 5, strongerOwner);
        weakerOwner.Cash = 10_000;
        strongerOwner.Cash = 10_000;

        player.CurrentNodeId = "0:0";
        player.CurrentCity = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Start", StringComparison.Ordinal));
        player.Destination = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Finish", StringComparison.Ordinal));
        player.TripOriginCity = player.CurrentCity;

        var route = engine.SuggestRouteForPlayer(player.Index);

        Assert.Equal([1, 2], route.Segments.Take(2).Select(segment => segment.RailroadIndex).ToArray());
    }

    [Fact]
    public void SuggestRouteForPlayer_TieBreak_PrefersSpreadPaymentsAcrossOwners()
    {
        var engine = CreateSpreadTieBreakEngine();
        var player = engine.Players[0];
        var firstOwner = engine.Players[1];
        var secondOwner = engine.Players[2];

        AssignOwnership(engine, 2, firstOwner);
        AssignOwnership(engine, 3, secondOwner);
        firstOwner.Cash = 10_000;
        secondOwner.Cash = 10_000;

        player.CurrentNodeId = "0:0";
        player.CurrentCity = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Start", StringComparison.Ordinal));
        player.Destination = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Finish", StringComparison.Ordinal));
        player.TripOriginCity = player.CurrentCity;

        var route = engine.SuggestRouteForPlayer(player.Index);

        Assert.Equal([1, 2, 3], route.Segments.Take(3).Select(segment => segment.RailroadIndex).ToArray());
    }

    private static Boxcars.Engine.Domain.GameEngine CreateUnfriendlyDestinationPlanningEngine()
    {
        var random = new FixedRandomProvider();
        random.QueueWeightedDraw(0);
        random.QueueWeightedDraw(0);
        random.QueueWeightedDraw(0);
        random.QueueWeightedDraw(1);

        return new Boxcars.Engine.Domain.GameEngine(
            CreateUnfriendlyDestinationPlanningMap(),
            GameEngineFixture.DefaultPlayerNames,
            random,
            GameSettings.Default with
            {
                HomeCityChoice = false,
                HomeSwapping = false
            });
    }

    private static Boxcars.Engine.Domain.GameEngine CreateLongPlanningEngine()
    {
        var random = new FixedRandomProvider();
        random.QueueWeightedDraw(0);
        random.QueueWeightedDraw(0);
        random.QueueWeightedDraw(0);
        random.QueueWeightedDraw(1);

        return new Boxcars.Engine.Domain.GameEngine(
            CreateLongPlanningMap(),
            GameEngineFixture.DefaultPlayerNames,
            random,
            GameSettings.Default with
            {
                HomeCityChoice = false,
                HomeSwapping = false
            });
    }

    private static Boxcars.Engine.Domain.GameEngine CreateDirectnessPlanningEngine()
    {
        var random = new FixedRandomProvider();
        random.QueueWeightedDraw(0);
        random.QueueWeightedDraw(0);
        random.QueueWeightedDraw(0);
        random.QueueWeightedDraw(1);

        return new Boxcars.Engine.Domain.GameEngine(
            CreateDirectnessPlanningMap(),
            GameEngineFixture.DefaultPlayerNames,
            random,
            GameSettings.Default with
            {
                HomeCityChoice = false,
                HomeSwapping = false,
                PublicFee = 3000,
                PrivateFee = 1000
            });
    }

    private static Boxcars.Engine.Domain.GameEngine CreateHostileExitPlanningEngine()
    {
        var random = new FixedRandomProvider();
        random.QueueWeightedDraw(0);
        random.QueueWeightedDraw(0);
        random.QueueWeightedDraw(0);
        random.QueueWeightedDraw(1);

        return new Boxcars.Engine.Domain.GameEngine(
            CreateHostileExitPlanningMap(),
            GameEngineFixture.DefaultPlayerNames,
            random,
            GameSettings.Default with
            {
                HomeCityChoice = false,
                HomeSwapping = false
            });
    }

    private static Boxcars.Engine.Domain.GameEngine CreateStrategicTieBreakEngine()
    {
        var random = new FixedRandomProvider();
        random.QueueWeightedDraw(0);
        random.QueueWeightedDraw(0);
        random.QueueWeightedDraw(0);
        random.QueueWeightedDraw(1);

        return new Boxcars.Engine.Domain.GameEngine(
            CreateStrategicTieBreakMap(),
            ["Player", "OwnerOne", "OwnerTwo"],
            random,
            GameSettings.Default with
            {
                HomeCityChoice = false,
                HomeSwapping = false
            });
    }

    private static Boxcars.Engine.Domain.GameEngine CreateSpreadTieBreakEngine()
    {
        var random = new FixedRandomProvider();
        random.QueueWeightedDraw(0);
        random.QueueWeightedDraw(0);
        random.QueueWeightedDraw(0);
        random.QueueWeightedDraw(1);

        return new Boxcars.Engine.Domain.GameEngine(
            CreateSpreadTieBreakMap(),
            ["Player", "OwnerOne", "OwnerTwo"],
            random,
            GameSettings.Default with
            {
                HomeCityChoice = false,
                HomeSwapping = false
            });
    }

    private static void AssignOwnership(Boxcars.Engine.Domain.GameEngine engine, int railroadIndex, Player owner)
    {
        var railroad = engine.Railroads.First(railroad => railroad.Index == railroadIndex);
        railroad.Owner = owner;
        owner.OwnedRailroads.Add(railroad);
    }

    private static MapDefinition CreateUnfriendlyDestinationPlanningMap()
    {
        var map = new MapDefinition
        {
            Name = "UnfriendlyDestinationPlanningMap",
            Version = "1.0"
        };

        map.Regions.Add(new RegionDefinition { Index = 0, Name = "Region", Code = "RG", Probability = 1.0 });

        map.Cities.Add(new CityDefinition { Name = "Start", RegionCode = "RG", Probability = 0.5, PayoutIndex = 0, MapDotIndex = 0 });
        map.Cities.Add(new CityDefinition { Name = "Finish", RegionCode = "RG", Probability = 0.5, PayoutIndex = 1, MapDotIndex = 3 });

        map.Railroads.Add(new RailroadDefinition { Index = 1, Name = "Friendly Approach", ShortName = "FA" });
        map.Railroads.Add(new RailroadDefinition { Index = 2, Name = "Short Exit Unfriendly", ShortName = "SU" });
        map.Railroads.Add(new RailroadDefinition { Index = 3, Name = "Long Exit Unfriendly", ShortName = "LU" });
        map.Railroads.Add(new RailroadDefinition { Index = 4, Name = "Safe Exit One", ShortName = "SE1" });

        for (var dot = 0; dot < 10; dot++)
        {
            map.TrainDots.Add(new TrainDot
            {
                Id = $"0:{dot}",
                RegionIndex = 0,
                DotIndex = dot,
                X = dot * 10,
                Y = dot * 10
            });
        }

        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition { RailroadIndex = 1, StartRegionIndex = 0, StartDotIndex = 0, EndRegionIndex = 0, EndDotIndex = 1 });
        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition { RailroadIndex = 2, StartRegionIndex = 0, StartDotIndex = 1, EndRegionIndex = 0, EndDotIndex = 3 });
        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition { RailroadIndex = 1, StartRegionIndex = 0, StartDotIndex = 0, EndRegionIndex = 0, EndDotIndex = 2 });
        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition { RailroadIndex = 3, StartRegionIndex = 0, StartDotIndex = 2, EndRegionIndex = 0, EndDotIndex = 6 });
        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition { RailroadIndex = 3, StartRegionIndex = 0, StartDotIndex = 6, EndRegionIndex = 0, EndDotIndex = 7 });
        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition { RailroadIndex = 3, StartRegionIndex = 0, StartDotIndex = 7, EndRegionIndex = 0, EndDotIndex = 3 });
        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition { RailroadIndex = 2, StartRegionIndex = 0, StartDotIndex = 3, EndRegionIndex = 0, EndDotIndex = 4 });
        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition { RailroadIndex = 4, StartRegionIndex = 0, StartDotIndex = 4, EndRegionIndex = 0, EndDotIndex = 5 });
        return map;
    }

    private static MapDefinition CreateStrategicTieBreakMap()
    {
        var map = new MapDefinition
        {
            Name = "StrategicTieBreakMap",
            Version = "1.0"
        };

        map.Regions.Add(new RegionDefinition { Index = 0, Name = "Region", Code = "RG", Probability = 100.0 });

        map.Cities.Add(new CityDefinition { Name = "Start", RegionCode = "RG", Probability = 25.0, PayoutIndex = 0, MapDotIndex = 0 });
        map.Cities.Add(new CityDefinition { Name = "Finish", RegionCode = "RG", Probability = 25.0, PayoutIndex = 1, MapDotIndex = 3 });
        map.Cities.Add(new CityDefinition { Name = "Junction", RegionCode = "RG", Probability = 25.0, PayoutIndex = 2, MapDotIndex = 4 });
        map.Cities.Add(new CityDefinition { Name = "Extension", RegionCode = "RG", Probability = 25.0, PayoutIndex = 3, MapDotIndex = 5 });

        map.Railroads.Add(new RailroadDefinition { Index = 1, Name = "Approach", ShortName = "AP" });
        map.Railroads.Add(new RailroadDefinition { Index = 2, Name = "Owner One Route", ShortName = "O1" });
        map.Railroads.Add(new RailroadDefinition { Index = 3, Name = "Owner Two Route", ShortName = "O2" });
        map.Railroads.Add(new RailroadDefinition { Index = 4, Name = "Public Exit", ShortName = "PX" });
        map.Railroads.Add(new RailroadDefinition { Index = 5, Name = "Owner Two Extension", ShortName = "OX" });

        for (var dot = 0; dot < 6; dot++)
        {
            map.TrainDots.Add(new TrainDot
            {
                Id = $"0:{dot}",
                RegionIndex = 0,
                DotIndex = dot,
                X = dot * 10,
                Y = dot * 10
            });
        }

        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition { RailroadIndex = 1, StartRegionIndex = 0, StartDotIndex = 0, EndRegionIndex = 0, EndDotIndex = 1 });
        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition { RailroadIndex = 2, StartRegionIndex = 0, StartDotIndex = 1, EndRegionIndex = 0, EndDotIndex = 3 });
        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition { RailroadIndex = 1, StartRegionIndex = 0, StartDotIndex = 0, EndRegionIndex = 0, EndDotIndex = 2 });
        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition { RailroadIndex = 3, StartRegionIndex = 0, StartDotIndex = 2, EndRegionIndex = 0, EndDotIndex = 3 });
        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition { RailroadIndex = 4, StartRegionIndex = 0, StartDotIndex = 3, EndRegionIndex = 0, EndDotIndex = 4 });
        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition { RailroadIndex = 5, StartRegionIndex = 0, StartDotIndex = 4, EndRegionIndex = 0, EndDotIndex = 5 });

        return map;
    }

    private static MapDefinition CreateSpreadTieBreakMap()
    {
        var map = new MapDefinition
        {
            Name = "SpreadTieBreakMap",
            Version = "1.0"
        };

        map.Regions.Add(new RegionDefinition { Index = 0, Name = "Region", Code = "RG", Probability = 1.0 });

        map.Cities.Add(new CityDefinition { Name = "Start", RegionCode = "RG", Probability = 0.5, PayoutIndex = 0, MapDotIndex = 0 });
        map.Cities.Add(new CityDefinition { Name = "Finish", RegionCode = "RG", Probability = 0.5, PayoutIndex = 1, MapDotIndex = 3 });

        map.Railroads.Add(new RailroadDefinition { Index = 1, Name = "Approach", ShortName = "AP" });
        map.Railroads.Add(new RailroadDefinition { Index = 2, Name = "Owner One Route", ShortName = "O1" });
        map.Railroads.Add(new RailroadDefinition { Index = 3, Name = "Owner Two Route", ShortName = "O2" });
        map.Railroads.Add(new RailroadDefinition { Index = 4, Name = "Public Exit", ShortName = "PX" });

        for (var dot = 0; dot < 7; dot++)
        {
            map.TrainDots.Add(new TrainDot
            {
                Id = $"0:{dot}",
                RegionIndex = 0,
                DotIndex = dot,
                X = dot * 10,
                Y = dot * 10
            });
        }

        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition { RailroadIndex = 1, StartRegionIndex = 0, StartDotIndex = 0, EndRegionIndex = 0, EndDotIndex = 1 });
        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition { RailroadIndex = 2, StartRegionIndex = 0, StartDotIndex = 1, EndRegionIndex = 0, EndDotIndex = 2 });
        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition { RailroadIndex = 2, StartRegionIndex = 0, StartDotIndex = 2, EndRegionIndex = 0, EndDotIndex = 3 });
        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition { RailroadIndex = 1, StartRegionIndex = 0, StartDotIndex = 0, EndRegionIndex = 0, EndDotIndex = 4 });
        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition { RailroadIndex = 2, StartRegionIndex = 0, StartDotIndex = 4, EndRegionIndex = 0, EndDotIndex = 5 });
        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition { RailroadIndex = 3, StartRegionIndex = 0, StartDotIndex = 5, EndRegionIndex = 0, EndDotIndex = 3 });
        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition { RailroadIndex = 4, StartRegionIndex = 0, StartDotIndex = 3, EndRegionIndex = 0, EndDotIndex = 6 });

        return map;
    }

    private static MapDefinition CreateDirectnessPlanningMap()
    {
        var map = new MapDefinition
        {
            Name = "DirectnessPlanningMap",
            Version = "1.0"
        };

        map.Regions.Add(new RegionDefinition { Index = 0, Name = "Region", Code = "RG", Probability = 1.0 });

        map.Cities.Add(new CityDefinition { Name = "Start", RegionCode = "RG", Probability = 0.5, PayoutIndex = 0, MapDotIndex = 0 });
        map.Cities.Add(new CityDefinition { Name = "Finish", RegionCode = "RG", Probability = 0.5, PayoutIndex = 1, MapDotIndex = 6 });

        map.Railroads.Add(new RailroadDefinition { Index = 1, Name = "Owned Detour", ShortName = "OD" });
        map.Railroads.Add(new RailroadDefinition { Index = 2, Name = "Direct Public", ShortName = "DP" });

        for (var dot = 0; dot <= 6; dot++)
        {
            map.TrainDots.Add(new TrainDot
            {
                Id = $"0:{dot}",
                RegionIndex = 0,
                DotIndex = dot,
                X = dot * 10,
                Y = dot * 10
            });
        }

        for (var dot = 0; dot < 6; dot++)
        {
            map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition
            {
                RailroadIndex = 1,
                StartRegionIndex = 0,
                StartDotIndex = dot,
                EndRegionIndex = 0,
                EndDotIndex = dot + 1
            });
        }

        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition
        {
            RailroadIndex = 2,
            StartRegionIndex = 0,
            StartDotIndex = 0,
            EndRegionIndex = 0,
            EndDotIndex = 6
        });

        return map;
    }

    private static MapDefinition CreateHostileExitPlanningMap()
    {
        var map = new MapDefinition
        {
            Name = "HostileExitPlanningMap",
            Version = "1.0"
        };

        map.Regions.Add(new RegionDefinition { Index = 0, Name = "Region", Code = "RG", Probability = 1.0 });

        map.Cities.Add(new CityDefinition { Name = "Start", RegionCode = "RG", Probability = 0.5, PayoutIndex = 0, MapDotIndex = 0 });
        map.Cities.Add(new CityDefinition { Name = "Finish", RegionCode = "RG", Probability = 0.5, PayoutIndex = 1, MapDotIndex = 3 });

        map.Railroads.Add(new RailroadDefinition { Index = 1, Name = "Hostile Main", ShortName = "HM" });
        map.Railroads.Add(new RailroadDefinition { Index = 2, Name = "Public Connection", ShortName = "PC" });

        for (var dot = 0; dot <= 3; dot++)
        {
            map.TrainDots.Add(new TrainDot
            {
                Id = $"0:{dot}",
                RegionIndex = 0,
                DotIndex = dot,
                X = dot * 10,
                Y = dot * 10
            });
        }

        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition
        {
            RailroadIndex = 1,
            StartRegionIndex = 0,
            StartDotIndex = 0,
            EndRegionIndex = 0,
            EndDotIndex = 1
        });
        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition
        {
            RailroadIndex = 1,
            StartRegionIndex = 0,
            StartDotIndex = 1,
            EndRegionIndex = 0,
            EndDotIndex = 3
        });
        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition
        {
            RailroadIndex = 2,
            StartRegionIndex = 0,
            StartDotIndex = 1,
            EndRegionIndex = 0,
            EndDotIndex = 2
        });
        map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition
        {
            RailroadIndex = 2,
            StartRegionIndex = 0,
            StartDotIndex = 2,
            EndRegionIndex = 0,
            EndDotIndex = 3
        });

        return map;
    }

    private static MapDefinition CreateLongPlanningMap()
    {
        var map = new MapDefinition
        {
            Name = "LongPlanningMap",
            Version = "1.0"
        };

        map.Regions.Add(new RegionDefinition { Index = 0, Name = "Region", Code = "RG", Probability = 1.0 });
        map.Cities.Add(new CityDefinition { Name = "Start", RegionCode = "RG", Probability = 0.5, PayoutIndex = 0, MapDotIndex = 0 });
        map.Cities.Add(new CityDefinition { Name = "Finish", RegionCode = "RG", Probability = 0.5, PayoutIndex = 1, MapDotIndex = Boxcars.Engine.Domain.GameEngine.RoutePlanningMaximumSegments + 1 });
        map.Railroads.Add(new RailroadDefinition { Index = 1, Name = "Long Line", ShortName = "LL" });

        for (var dot = 0; dot <= Boxcars.Engine.Domain.GameEngine.RoutePlanningMaximumSegments + 1; dot++)
        {
            map.TrainDots.Add(new TrainDot
            {
                Id = $"0:{dot}",
                RegionIndex = 0,
                DotIndex = dot,
                X = dot * 10,
                Y = dot * 10
            });
        }

        for (var dot = 0; dot <= Boxcars.Engine.Domain.GameEngine.RoutePlanningMaximumSegments; dot++)
        {
            map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition
            {
                RailroadIndex = 1,
                StartRegionIndex = 0,
                StartDotIndex = dot,
                EndRegionIndex = 0,
                EndDotIndex = dot + 1
            });
        }

        return map;
    }

    private static TimeSpan GetRoutePlanningTimeLimit()
    {
        var property = typeof(Boxcars.Engine.Domain.GameEngine).GetProperty("RoutePlanningTimeLimit", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("RoutePlanningTimeLimit was not found.");

        return (TimeSpan)(property.GetValue(null)
            ?? throw new InvalidOperationException("RoutePlanningTimeLimit returned null."));
    }

    private static void SetRoutePlanningTimeLimit(TimeSpan timeLimit)
    {
        var property = typeof(Boxcars.Engine.Domain.GameEngine).GetProperty("RoutePlanningTimeLimit", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("RoutePlanningTimeLimit was not found.");

        property.SetValue(null, timeLimit);
    }
}
