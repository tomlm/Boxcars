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
[Collection(RoutePlanningCollection.Name)]
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

            // Cheapest route times out, but BFS fallback should still find a path, priced with the fee rules
            Assert.StartsWith(engine.CurrentTurn.ActivePlayer.CurrentNodeId!, route.NodeIds[0]);
            Assert.True(route.Segments.Count > 0, "BFS fallback should produce a non-empty route");
            Assert.True(route.TotalCost > 0, "The fallback route should not be reported as free");
        }
        finally
        {
            SetRoutePlanningTimeLimit(originalTimeLimit);
        }
    }

    [Fact]
    public void SuggestRoute_FallbackRoute_SkipsTrackAlreadyRiddenThisTrip()
    {
        // 0 --[RR3]--> 1 --[RR3]--> 3          (shortest, but 0-1 was ridden earlier this trip)
        // 0 --[RR3]--> 4 --[RR3]--> 5 --[RR3]--> 3
        var engine = CreateLineEngine([(3, 0, 1), (3, 1, 3), (3, 0, 4), (3, 4, 5), (3, 5, 3), (4, 3, 9)]);
        var player = PlaceOnLine(engine);
        player.UsedSegments.Add(new SegmentKey("0:0", "0:1", 3));
        var originalTimeLimit = GetRoutePlanningTimeLimit();

        try
        {
            SetRoutePlanningTimeLimit(TimeSpan.Zero);

            var route = engine.SuggestRouteForPlayer(player.Index);

            Assert.Equal(["0:0", "0:4", "0:5", "0:3"], route.NodeIds);
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

        // Both routes take one turn: the owned detour costs $1,000 and the direct public line $2,000.
        Assert.Equal([2], route.Segments.Select(segment => segment.RailroadIndex).ToArray());
        Assert.Equal(["0:0", "0:6"], route.NodeIds);
        Assert.Equal(2000, route.TotalCost);
    }

    [Fact]
    public void SuggestRouteForPlayer_PrefersDirectRouteAcrossPublicRailroadsOverLongerSingleRailroad()
    {
        var engine = CreatePublicSwitchPlanningEngine();
        var player = engine.Players[0];

        player.LocomotiveType = LocomotiveType.Superchief;
        player.CurrentNodeId = "0:0";
        player.CurrentCity = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Start", StringComparison.Ordinal));
        player.Destination = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Finish", StringComparison.Ordinal));
        player.TripOriginCity = player.CurrentCity;

        var route = engine.SuggestRouteForPlayer(player.Index);

        // Both routes fit in one turn and every railroad is public, so the turn pays one public fee either way.
        Assert.Equal([1, 2], route.Segments.Select(segment => segment.RailroadIndex).ToArray());
        Assert.Equal(["0:0", "0:1", "0:4"], route.NodeIds);
        Assert.Equal(1000, route.TotalCost);
    }

    [Fact]
    public void SuggestRouteForPlayer_BonusMovement_StaysOnRailroadAlreadyPaidThisTurn()
    {
        var engine = CreateBonusOutPlanningEngine();
        var player = engine.Players[0];
        AssignOwnership(engine, 1, engine.Players[1]);
        AssignOwnership(engine, 2, engine.Players[2]);

        player.CurrentNodeId = "0:0";
        player.CurrentCity = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Start", StringComparison.Ordinal));
        player.Destination = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Finish", StringComparison.Ordinal));
        player.TripOriginCity = player.CurrentCity;

        // The player arrived at Start on Bob's RR1 this turn, so that fee is already owed.
        engine.CurrentTurn.RailroadsRiddenThisTurn.Add(1);

        var route = engine.SuggestRouteForPlayer(player.Index);

        // Staying on RR1 and switching to public RR3 adds only the public fee; Charlie's RR2 would add $5,000.
        Assert.Equal([1, 3], route.Segments.Select(segment => segment.RailroadIndex).ToArray());
        Assert.Equal(["0:0", "0:1", "0:2"], route.NodeIds);
        Assert.Equal(1000, route.TotalCost);
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

        // Cheapest route exceeds max segments, but BFS fallback should find a path
        Assert.Equal("0:0", route.NodeIds[0]);
        Assert.True(route.Segments.Count > 0, "BFS fallback should produce a non-empty route");
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

        // Both routes run into a second turn and pay two $5,000 fees; the second pays them to different owners.
        Assert.Equal([1, 2, 2, 2, 2, 2, 2, 3], route.Segments.Take(8).Select(segment => segment.RailroadIndex).ToArray());
    }

    [Fact]
    public void SuggestRouteForPlayer_BonusOut_UsesTheBonusToGetOffAnOpponentsRailroadBeforeNextTurn()
    {
        var engine = CreateBonusOutNextTurnEngine();
        var player = engine.Players[0];
        AssignOwnership(engine, 1, engine.Players[1]);

        player.LocomotiveType = LocomotiveType.Superchief;
        player.CurrentNodeId = "0:0";
        player.CurrentCity = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Start", StringComparison.Ordinal));
        player.Destination = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Finish", StringComparison.Ordinal));
        player.TripOriginCity = player.CurrentCity;

        // The player arrived at Start on Bob's RR1 this turn and has a bonus of 2 left to move.
        engine.CurrentTurn.RailroadsRiddenThisTurn.Add(1);
        engine.CurrentTurn.Phase = TurnPhase.Move;
        engine.CurrentTurn.MovementRemaining = 2;

        var route = engine.SuggestRouteForPlayer(player.Index);

        // Staying on RR1 is shorter but leaves the player 1 dot short, on RR1, when the next turn starts ($5,000).
        // The bonus is better spent reaching the public line, so the next turn pays only the public fee.
        Assert.Equal([1, 1, 2, 2], route.Segments.Select(segment => segment.RailroadIndex).ToArray());
        Assert.Equal(["0:0", "0:4", "0:5", "0:6", "0:3"], route.NodeIds);
        Assert.Equal(1000, route.TotalCost);
    }

    private static Boxcars.Engine.Domain.GameEngine CreateBonusOutNextTurnEngine()
    {
        return new Boxcars.Engine.Domain.GameEngine(
            CreateBonusOutNextTurnMap(),
            GameEngineFixture.ThreePlayerNames,
            new FixedRandomProvider(),
            GameSettings.Default with
            {
                HomeCityChoice = false,
                HomeSwapping = false,
                PublicFee = 1000
            });
    }

    private static MapDefinition CreateBonusOutNextTurnMap()
    {
        // 0 --[RR1]--> 1 --[RR1]--> 2 --[RR1]--> 3            (stay on the arrival railroad: 3 dots)
        // 0 --[RR1]--> 4 --[RR1]--> 5 --[RR2]--> 6 --[RR2]--> 3 (2 dots on it, then the public line)
        // 3 --[RR2]--> 7                                       (friendly exit from Finish)
        var map = new MapDefinition
        {
            Name = "BonusOutNextTurnMap",
            Version = "1.0"
        };

        map.Regions.Add(new RegionDefinition { Index = 0, Name = "Region", Code = "RG", Probability = 1.0 });

        map.Cities.Add(new CityDefinition { Name = "Start", RegionCode = "RG", Probability = 0.5, PayoutIndex = 0, MapDotIndex = 0 });
        map.Cities.Add(new CityDefinition { Name = "Finish", RegionCode = "RG", Probability = 0.5, PayoutIndex = 1, MapDotIndex = 3 });

        map.Railroads.Add(new RailroadDefinition { Index = 1, Name = "Arrival Line", ShortName = "AL" });
        map.Railroads.Add(new RailroadDefinition { Index = 2, Name = "Public Line", ShortName = "PL" });

        for (var dot = 0; dot <= 7; dot++)
        {
            map.TrainDots.Add(new TrainDot { Id = $"0:{dot}", RegionIndex = 0, DotIndex = dot, X = dot * 10, Y = dot * 10 });
        }

        foreach (var (railroadIndex, startDot, endDot) in new[] { (1, 0, 1), (1, 1, 2), (1, 2, 3), (1, 0, 4), (1, 4, 5), (2, 5, 6), (2, 6, 3), (2, 3, 7) })
        {
            map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition
            {
                RailroadIndex = railroadIndex,
                StartRegionIndex = 0,
                StartDotIndex = startDot,
                EndRegionIndex = 0,
                EndDotIndex = endDot
            });
        }

        return map;
    }

    [Fact]
    public void SuggestRouteForPlayer_ClearLeader_PaysLaggardWhenItCostsNoMore()
    {
        var engine = CreateLeaderAvoidanceEngine(laggardRouteSegments: 2);
        var player = PrepareLeaderAvoidancePlayer(engine);
        SetOpponents(engine, leaderCash: 150_000, laggardCash: 10_000);

        var route = engine.SuggestRouteForPlayer(player.Index);

        // Both routes pay one public fee and one $5,000 opponent fee in a single turn; the laggard route is one dot longer.
        Assert.Equal([1, 3, 3], route.Segments.Select(segment => segment.RailroadIndex).ToArray());
        Assert.Equal(6000, route.TotalCost);
    }

    [Fact]
    public void SuggestRouteForPlayer_NoClearLeader_TakesTheDirectRoute()
    {
        var engine = CreateLeaderAvoidanceEngine(laggardRouteSegments: 2);
        var player = PrepareLeaderAvoidancePlayer(engine);
        SetOpponents(engine, leaderCash: 50_000, laggardCash: 50_000);

        var route = engine.SuggestRouteForPlayer(player.Index);

        Assert.Equal([1, 2], route.Segments.Select(segment => segment.RailroadIndex).ToArray());
        Assert.Equal(6000, route.TotalCost);
    }

    [Fact]
    public void SuggestRouteForPlayer_ClearLeader_StillPaysLeaderWhenAvoidingCostsAnotherTurn()
    {
        var engine = CreateLeaderAvoidanceEngine(laggardRouteSegments: 5);
        var player = PrepareLeaderAvoidancePlayer(engine);
        SetOpponents(engine, leaderCash: 150_000, laggardCash: 10_000);

        var route = engine.SuggestRouteForPlayer(player.Index);

        // The laggard route needs a second turn (and a second fee), so reaching the destination on the leader wins.
        Assert.Equal([1, 2], route.Segments.Select(segment => segment.RailroadIndex).ToArray());
        Assert.Equal(6000, route.TotalCost);
    }

    [Fact]
    public void SuggestRouteForPlayer_PaysEachOwnerOncePerTurn_EvenAfterLeavingTheirRailroad()
    {
        // 0 --[RR1 Bob]--> 1 --[RR2 own]--> 2 --[RR1 Bob]--> 3   (one turn for a Freight)
        var engine = CreateLineEngine([(1, 0, 1), (2, 1, 2), (1, 2, 3), (3, 3, 9)]);
        AssignOwnership(engine, 1, engine.Players[1]);
        AssignOwnership(engine, 2, engine.Players[0]);
        var player = PlaceOnLine(engine);

        var route = engine.SuggestRouteForPlayer(player.Index);

        // Bob is paid once ($5,000) and the player's own railroad once ($1,000), not Bob twice.
        Assert.Equal([1, 2, 1], route.Segments.Select(segment => segment.RailroadIndex).ToArray());
        Assert.Equal(6000, route.TotalCost);
    }

    [Fact]
    public void SuggestRouteForPlayer_SameCostAndTurns_StaysOffOpponentsRailroadsLonger()
    {
        // 0 --[RR1 Bob]--> 1 --[RR1 Bob]--> 2 --[RR3 public]--> 3     (2 dots on Bob's railroad, found first)
        // 0 --[RR3 public]--> 4 --[RR1 Bob]--> 5 --[RR3 public]--> 3  (1 dot on Bob's railroad)
        var engine = CreateLineEngine([(1, 0, 1), (1, 1, 2), (3, 2, 3), (3, 0, 4), (1, 4, 5), (3, 5, 3), (4, 3, 9)]);
        AssignOwnership(engine, 1, engine.Players[1]);
        var player = PlaceOnLine(engine);

        var route = engine.SuggestRouteForPlayer(player.Index);

        // Both are 3 dots in one turn and pay the public fee and Bob once each.
        Assert.Equal(["0:0", "0:4", "0:5", "0:3"], route.NodeIds);
        Assert.Equal(6000, route.TotalCost);
    }

    [Fact]
    public void SuggestRouteForPlayer_SuperchiefWouldArriveStuckOnOpponent_LaysUpOnPublicTrack()
    {
        // 0 --[RR1 Bob]--> 1 --[RR1 Bob]--> 3          (go in now, arriving after the white dice: stuck on Bob's railroad)
        // 0 --[RR2 public]--> 4 --[RR2 public]--> 5 --[RR1 Bob]--> 3
        // 3 --[RR1 Bob]--> 6 --[RR2 public]--> 7       (Finish is served only by Bob)
        var engine = CreateLineEngine([(1, 0, 1), (1, 1, 3), (2, 0, 4), (2, 4, 5), (1, 5, 3), (1, 3, 6), (2, 6, 7)]);
        AssignOwnership(engine, 1, engine.Players[1]);
        var player = PlaceOnLine(engine);
        player.LocomotiveType = LocomotiveType.Superchief;
        // Without a trip, a turn costs the default $1,000. (This 2-dot trip's turn would be worth its whole payout.)
        player.TripOriginCity = null;

        // White 2+2 and red 1, with 3 dots already moved: the last 2 are past the white dice.
        engine.CurrentTurn.Phase = TurnPhase.Move;
        engine.CurrentTurn.DiceResult = new DiceResult([2, 2], 1);
        engine.CurrentTurn.MovementAllowance = 5;
        engine.CurrentTurn.MovementRemaining = 2;

        var route = engine.SuggestRouteForPlayer(player.Index);

        // Next turn the white dice always reach Finish and the red die gets off Bob's railroad 5 times in 6.
        Assert.True(engine.LastApproachDecision?.LayUp);
        Assert.Equal(["0:0", "0:4", "0:5", "0:3"], route.NodeIds);
        Assert.Equal([2, 2, 1], route.Segments.Select(segment => segment.RailroadIndex).ToArray());
    }

    private static Boxcars.Engine.Domain.GameEngine CreateLineEngine(IReadOnlyList<(int Railroad, int Start, int End)> segments)
    {
        // Start is dot 0 and Finish dot 3; dot 9 is a public exit from Finish.
        var map = new MapDefinition { Name = "LineMap", Version = "1.0" };
        map.Regions.Add(new RegionDefinition { Index = 0, Name = "Region", Code = "RG", Probability = 1.0 });
        map.Cities.Add(new CityDefinition { Name = "Start", RegionCode = "RG", Probability = 0.5, PayoutIndex = 0, MapDotIndex = 0 });
        map.Cities.Add(new CityDefinition { Name = "Finish", RegionCode = "RG", Probability = 0.5, PayoutIndex = 1, MapDotIndex = 3 });

        foreach (var railroadIndex in segments.Select(segment => segment.Railroad).Distinct())
        {
            map.Railroads.Add(new RailroadDefinition { Index = railroadIndex, Name = $"Line {railroadIndex}", ShortName = $"L{railroadIndex}" });
        }

        foreach (var dot in segments.SelectMany(segment => new[] { segment.Start, segment.End }).Distinct())
        {
            map.TrainDots.Add(new TrainDot { Id = $"0:{dot}", RegionIndex = 0, DotIndex = dot, X = dot * 10, Y = dot * 10 });
        }

        foreach (var (railroadIndex, startDot, endDot) in segments)
        {
            map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition
            {
                RailroadIndex = railroadIndex,
                StartRegionIndex = 0,
                StartDotIndex = startDot,
                EndRegionIndex = 0,
                EndDotIndex = endDot
            });
        }

        return new Boxcars.Engine.Domain.GameEngine(
            map,
            GameEngineFixture.ThreePlayerNames,
            new FixedRandomProvider(),
            GameSettings.Default with { HomeCityChoice = false, HomeSwapping = false, PublicFee = 1000 });
    }

    private static Player PlaceOnLine(Boxcars.Engine.Domain.GameEngine engine)
    {
        var player = engine.Players[0];
        player.CurrentNodeId = "0:0";
        player.CurrentCity = engine.MapDefinition.Cities.First(city => city.Name == "Start");
        player.Destination = engine.MapDefinition.Cities.First(city => city.Name == "Finish");
        player.TripOriginCity = player.CurrentCity;
        return player;
    }

    private static Player PrepareLeaderAvoidancePlayer(Boxcars.Engine.Domain.GameEngine engine)
    {
        var player = engine.Players[0];
        player.LocomotiveType = LocomotiveType.Superchief;
        player.CurrentNodeId = "0:0";
        player.CurrentCity = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Start", StringComparison.Ordinal));
        player.Destination = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Finish", StringComparison.Ordinal));
        player.TripOriginCity = player.CurrentCity;
        return player;
    }

    private static void SetOpponents(Boxcars.Engine.Domain.GameEngine engine, int leaderCash, int laggardCash)
    {
        AssignOwnership(engine, 2, engine.Players[1]);
        AssignOwnership(engine, 3, engine.Players[2]);
        engine.Players[1].Cash = leaderCash;
        engine.Players[2].Cash = laggardCash;
    }

    private static Boxcars.Engine.Domain.GameEngine CreateLeaderAvoidanceEngine(int laggardRouteSegments)
    {
        return new Boxcars.Engine.Domain.GameEngine(
            CreateLeaderAvoidanceMap(laggardRouteSegments),
            GameEngineFixture.ThreePlayerNames,
            new FixedRandomProvider(),
            GameSettings.Default with
            {
                HomeCityChoice = false,
                HomeSwapping = false,
                PublicFee = 1000
            });
    }

    private static MapDefinition CreateLeaderAvoidanceMap(int laggardRouteSegments)
    {
        // 0 --[RR1 public]--> 1 --[RR2, Players[1]]--> 3                       (direct: 2 segments)
        // 0 --[RR1 public]--> 2 --[RR3, Players[2]] x laggardRouteSegments--> 3 (laggard route)
        // 3 --[RR4 public]--> 9                                                  (friendly exit from Finish)
        var map = new MapDefinition
        {
            Name = "LeaderAvoidanceMap",
            Version = "1.0"
        };

        map.Regions.Add(new RegionDefinition { Index = 0, Name = "Region", Code = "RG", Probability = 1.0 });

        map.Cities.Add(new CityDefinition { Name = "Start", RegionCode = "RG", Probability = 0.5, PayoutIndex = 0, MapDotIndex = 0 });
        map.Cities.Add(new CityDefinition { Name = "Finish", RegionCode = "RG", Probability = 0.5, PayoutIndex = 1, MapDotIndex = 3 });

        map.Railroads.Add(new RailroadDefinition { Index = 1, Name = "Public Approach", ShortName = "PA" });
        map.Railroads.Add(new RailroadDefinition { Index = 2, Name = "Leader Line", ShortName = "LL" });
        map.Railroads.Add(new RailroadDefinition { Index = 3, Name = "Laggard Line", ShortName = "GL" });
        map.Railroads.Add(new RailroadDefinition { Index = 4, Name = "Public Exit", ShortName = "PX" });

        // Laggard route runs 2 -> 10 -> 11 -> ... -> 3.
        var laggardDots = new List<int> { 2 };
        laggardDots.AddRange(Enumerable.Range(10, laggardRouteSegments - 1));
        laggardDots.Add(3);

        foreach (var dot in new[] { 0, 1, 2, 3, 9 }.Concat(laggardDots).Distinct())
        {
            map.TrainDots.Add(new TrainDot { Id = $"0:{dot}", RegionIndex = 0, DotIndex = dot, X = dot * 10, Y = dot * 10 });
        }

        var segments = new List<(int Railroad, int Start, int End)> { (1, 0, 1), (2, 1, 3), (1, 0, 2), (4, 3, 9) };
        segments.AddRange(laggardDots.Zip(laggardDots.Skip(1), (start, end) => (3, start, end)));

        foreach (var (railroadIndex, startDot, endDot) in segments)
        {
            map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition
            {
                RailroadIndex = railroadIndex,
                StartRegionIndex = 0,
                StartDotIndex = startDot,
                EndRegionIndex = 0,
                EndDotIndex = endDot
            });
        }

        return map;
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
                PublicFee = 2000,
                PrivateFee = 1000
            });
    }

    private static Boxcars.Engine.Domain.GameEngine CreatePublicSwitchPlanningEngine()
    {
        var random = new FixedRandomProvider();
        random.QueueWeightedDraw(0);
        random.QueueWeightedDraw(0);
        random.QueueWeightedDraw(0);
        random.QueueWeightedDraw(1);

        return new Boxcars.Engine.Domain.GameEngine(
            CreatePublicSwitchPlanningMap(),
            GameEngineFixture.DefaultPlayerNames,
            random,
            GameSettings.Default with
            {
                HomeCityChoice = false,
                HomeSwapping = false,
                PublicFee = 1000
            });
    }

    private static Boxcars.Engine.Domain.GameEngine CreateBonusOutPlanningEngine()
    {
        return new Boxcars.Engine.Domain.GameEngine(
            CreateBonusOutPlanningMap(),
            GameEngineFixture.ThreePlayerNames,
            new FixedRandomProvider(),
            GameSettings.Default with
            {
                HomeCityChoice = false,
                HomeSwapping = false,
                PublicFee = 1000
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

        // Two 8-dot routes from 0 to 3; a Freight's first turn (7 dots) ends one dot short on Owner One's RR2.
        // 0 --[RR1]--> 10 --[RR2 x6]--> 16 --[RR2]--> 3   (pays Owner One again next turn)
        // 0 --[RR1]--> 20 --[RR2 x6]--> 26 --[RR3]--> 3   (pays Owner Two next turn)
        // 3 --[RR4]--> 6                                  (friendly exit from Finish)
        var segments = new List<(int Railroad, int Start, int End)> { (1, 0, 10), (1, 0, 20), (2, 16, 3), (3, 26, 3), (4, 3, 6) };
        for (var dot = 10; dot < 16; dot++)
        {
            segments.Add((2, dot, dot + 1));
            segments.Add((2, dot + 10, dot + 11));
        }

        foreach (var dot in segments.SelectMany(segment => new[] { segment.Start, segment.End }).Distinct())
        {
            map.TrainDots.Add(new TrainDot { Id = $"0:{dot}", RegionIndex = 0, DotIndex = dot, X = dot * 10, Y = dot * 10 });
        }

        foreach (var (railroadIndex, startDot, endDot) in segments)
        {
            map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition
            {
                RailroadIndex = railroadIndex,
                StartRegionIndex = 0,
                StartDotIndex = startDot,
                EndRegionIndex = 0,
                EndDotIndex = endDot
            });
        }

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

    private static MapDefinition CreatePublicSwitchPlanningMap()
    {
        // 0 --[RR1]--> 1 --[RR2]--> 4           (direct, 2 segments, switches public railroads)
        // 0 --[RR3]--> 2 --[RR3]--> 3 --[RR3]--> 4 (detour, 3 segments, one public railroad)
        var map = new MapDefinition
        {
            Name = "PublicSwitchPlanningMap",
            Version = "1.0"
        };

        map.Regions.Add(new RegionDefinition { Index = 0, Name = "Region", Code = "RG", Probability = 1.0 });

        map.Cities.Add(new CityDefinition { Name = "Start", RegionCode = "RG", Probability = 0.5, PayoutIndex = 0, MapDotIndex = 0 });
        map.Cities.Add(new CityDefinition { Name = "Finish", RegionCode = "RG", Probability = 0.5, PayoutIndex = 1, MapDotIndex = 4 });

        map.Railroads.Add(new RailroadDefinition { Index = 1, Name = "First Public", ShortName = "FP" });
        map.Railroads.Add(new RailroadDefinition { Index = 2, Name = "Second Public", ShortName = "SP" });
        map.Railroads.Add(new RailroadDefinition { Index = 3, Name = "Single Detour", ShortName = "SD" });

        for (var dot = 0; dot <= 4; dot++)
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

        foreach (var (railroadIndex, startDot, endDot) in new[] { (1, 0, 1), (2, 1, 4), (3, 0, 2), (3, 2, 3), (3, 3, 4) })
        {
            map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition
            {
                RailroadIndex = railroadIndex,
                StartRegionIndex = 0,
                StartDotIndex = startDot,
                EndRegionIndex = 0,
                EndDotIndex = endDot
            });
        }

        return map;
    }

    private static MapDefinition CreateBonusOutPlanningMap()
    {
        // 0 --[RR1]--> 1 --[RR3]--> 2   (arrival railroad, then public)
        // 0 --[RR2]--> 2                (shorter, but another opponent's railroad)
        // 2 --[RR3]--> 3                (friendly exit from Finish)
        var map = new MapDefinition
        {
            Name = "BonusOutPlanningMap",
            Version = "1.0"
        };

        map.Regions.Add(new RegionDefinition { Index = 0, Name = "Region", Code = "RG", Probability = 1.0 });

        map.Cities.Add(new CityDefinition { Name = "Start", RegionCode = "RG", Probability = 0.5, PayoutIndex = 0, MapDotIndex = 0 });
        map.Cities.Add(new CityDefinition { Name = "Finish", RegionCode = "RG", Probability = 0.5, PayoutIndex = 1, MapDotIndex = 2 });

        map.Railroads.Add(new RailroadDefinition { Index = 1, Name = "Arrival Line", ShortName = "AL" });
        map.Railroads.Add(new RailroadDefinition { Index = 2, Name = "Other Owner Line", ShortName = "OO" });
        map.Railroads.Add(new RailroadDefinition { Index = 3, Name = "Public Line", ShortName = "PL" });

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

        foreach (var (railroadIndex, startDot, endDot) in new[] { (1, 0, 1), (3, 1, 2), (2, 0, 2), (3, 2, 3) })
        {
            map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition
            {
                RailroadIndex = railroadIndex,
                StartRegionIndex = 0,
                StartDotIndex = startDot,
                EndRegionIndex = 0,
                EndDotIndex = endDot
            });
        }

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
