using System.Runtime.CompilerServices;
using Boxcars.Data.Maps;
using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Domain;
using Boxcars.Engine.Persistence;
using Boxcars.Engine.Tests.TestDoubles;
using Boxcars.Services.Maps;
using Xunit.Abstractions;

namespace Boxcars.Engine.Tests.Unit;

/// <summary>
/// Bot route planning on positions taken from real games on the standard map.
/// </summary>
[Collection(RoutePlanningCollection.Name)]
public class RouteSuggestionRealMapTests(ITestOutputHelper output)
{
    [Fact]
    public void SuggestRoute_LosAngelesToAtlantaWithMostRailroadsOwned_FindsAPricedRoute()
    {
        // All railroads sold. El Cheapo (Superchief) is at Los Angeles with 1 dot of bonus left, has ridden
        // railroad 14 this turn, and is headed to Atlanta, which their SAL serves. The planner used to reject every
        // way into Atlanta and fall back to the fee-blind shortest path, reported as costing $0.
        var engine = CreateEngine(StandardGameOwners);
        var player = PlaceActivePlayer(engine, "El Cheapo", "Los Angeles", "Atlanta", LocomotiveType.Superchief, movementRemaining: 1);
        engine.CurrentTurn.RailroadsRiddenThisTurn.Add(14);

        var route = engine.SuggestRoute();
        WriteRoute(engine, player, route);

        // AT&SF (Pooper) for one turn and thermous's SP and CRI&P for one, then El Cheapo's own railroads into
        // Atlanta. At 48 dots it's longer than the 40-dot limit the planner used to have.
        Assert.Equal(FindCityNode(engine, "Atlanta"), route.NodeIds[^1]);
        Assert.Equal(24_000, route.TotalCost);
    }

    [Fact]
    public void SuggestRoute_ChesapeakeToSacramento_GetsOntoOwnRailroadsAndStaysThere()
    {
        // Issue #69: Connecto (Superchief, all railroads sold) on thermous's C&O west of Norfolk, headed to
        // Sacramento. Its own IC and SP cross the country, but that route is 48 dots and the planner used to drop
        // anything over 40, so it rode 24 dots of thermous's C&O, CB&Q and UP instead.
        var engine = CreateEngine(LateGameOwners);
        var player = PlaceActivePlayer(engine, "Connecto", "Norfolk", "Sacramento", LocomotiveType.Superchief, movementRemaining: 7);
        player.CurrentNodeId = "2:10";

        var route = engine.SuggestRoute();
        WriteRoute(engine, player, route);

        Assert.Equal(FindCityNode(engine, "Sacramento"), route.NodeIds[^1]);
        // Four dots of C&O to reach its own IC, then nothing but its own railroads.
        Assert.Equal(4, CountOpponentDots(engine, player, route));
    }

    [Theory]
    [InlineData(7)]
    [InlineData(10)]
    [InlineData(14)]
    public void SuggestRoute_UnionPacificToRichmond_TakesTheShortestWayOntoOwnRailroads(int movementRemaining)
    {
        // Issue #70: Connecto on thermous's UP, seven dots from its own SLSF, headed to Richmond. With a bigger roll
        // the planner used to stay on thermous's lines for the whole turn, since that turn's fee was already paid.
        var engine = CreateEngine(LateGameOwners);
        var player = PlaceActivePlayer(engine, "Connecto", "Sacramento", "Richmond", LocomotiveType.Superchief, movementRemaining);
        player.CurrentNodeId = "6:8";

        var route = engine.SuggestRoute();
        WriteRoute(engine, player, route);

        Assert.Equal(FindCityNode(engine, "Richmond"), route.NodeIds[^1]);
        Assert.Equal(7, CountOpponentDots(engine, player, route));
    }

    private static int CountOpponentDots(Boxcars.Engine.Domain.GameEngine engine, Player player, Route route)
    {
        return route.Segments.Count(segment =>
            engine.Railroads.First(railroad => railroad.Index == segment.RailroadIndex).Owner is { } owner && owner != player);
    }

    [Fact]
    public void SuggestRoute_KansasCityToColumbus_StaysOnOwnRailroadsUntilTheLastDot()
    {
        // thermous (Superchief) is on CRI&P west of Kansas City with 8 to move (white 3, red 5), headed to
        // Columbus, which only opponents serve. The suggestion used to exhaust its search budget and fall back to
        // the fee-blind shortest path, hopping onto Pooper's AT&SF for the last dot of the turn.
        var engine = CreateEngine(StandardGameOwners);
        var player = PlaceActivePlayer(engine, "thermous", "San Diego", "Columbus", LocomotiveType.Superchief, movementRemaining: 8);
        player.CurrentNodeId = "4:66";
        engine.CurrentTurn.DiceResult = new DiceResult([1, 2], 5);
        // Track already ridden on this trip (from San Diego), which can't be ridden again.
        foreach (var segmentKey in new[]
        {
            "7:41|7:42|14", "7:42|7:68|14", "7:68|7:43|14", "7:43|7:35|21", "7:35|7:40|21", "7:40|7:39|21",
            "7:39|7:38|21", "7:38|7:37|21", "7:37|7:36|21", "7:36|7:17|21", "7:17|7:15|21", "7:15|7:14|21",
            "7:14|7:7|21", "7:7|7:8|21", "7:8|7:9|21", "7:9|7:2|21", "7:2|4:68|23", "4:68|4:66|11"
        })
        {
            var parts = segmentKey.Split('|');
            player.UsedSegments.Add(new SegmentKey(parts[0], parts[1], int.Parse(parts[2])));
        }

        var suggestion = EngineRouteSuggestion.Suggest(engine, player.Index, new RouteSuggestionOptions());
        WriteSuggestion(engine, suggestion);

        // All 8 dots this turn stay on thermous's own railroads; the only opponent fee is the final dot into Columbus.
        Assert.Equal(RouteSuggestionStatus.Success, suggestion.Status);
        Assert.All(suggestion.Segments.Take(8), segment => Assert.Equal(RailroadOwnershipCategory.Friendly, segment.OwnershipCategory));
        Assert.Single(suggestion.Segments, segment => segment.OwnershipCategory == RailroadOwnershipCategory.Unfriendly);
        Assert.Equal(12_000, suggestion.TotalCost);
    }

    [Fact]
    public void SuggestRoute_BonusOutOfBuffalo_LeavesTheOpponentsLineBeforeTheTurnEnds()
    {
        // thermous (Superchief) reached Buffalo on El Cheapo's NYC and PA (already paid this turn) and has a red-die
        // bonus of 4 toward Kansas City. Staying on El Cheapo's lines for the bonus means paying them $10,000 again
        // next turn; spending $1,000 to get onto B&O now keeps the next turn on the player's own railroads.
        var engine = CreateEngine(StandardGameOwners);
        var player = PlaceActivePlayer(engine, "thermous", "Buffalo", "Kansas City", LocomotiveType.Superchief, movementRemaining: 4);
        engine.CurrentTurn.DiceResult = new DiceResult([0, 0], 4);
        engine.CurrentTurn.RailroadsRiddenThisTurn.Add(engine.Railroads.First(railroad => railroad.ShortName == "NYC").Index);
        engine.CurrentTurn.RailroadsRiddenThisTurn.Add(engine.Railroads.First(railroad => railroad.ShortName == "PA").Index);

        var suggestion = EngineRouteSuggestion.Suggest(engine, player.Index, new RouteSuggestionOptions());
        WriteSuggestion(engine, suggestion);

        // The 4 bonus dots end on the player's own B&O, so no opponent is paid next turn: $1,000 now and $1,000 next turn.
        Assert.Equal(RouteSuggestionStatus.Success, suggestion.Status);
        Assert.Equal(RailroadOwnershipCategory.Friendly, suggestion.Segments[3].OwnershipCategory);
        Assert.All(suggestion.Segments.Skip(4), segment => Assert.Equal(RailroadOwnershipCategory.Friendly, segment.OwnershipCategory));
        Assert.Equal(2_000, suggestion.TotalCost);
    }

    [Fact]
    public void ApproachToChattanooga_GoesInOverPoopersLines_InsteadOfDetouringThroughChicago()
    {
        // Issue #95: thermous (Superchief) on its own UP in Wyoming, LA -> Chattanooga, rolled 6+1+(3). The way in
        // ends with 11 dots of Pooper's track, which a short roll next turn pays for twice. The suggestion used to
        // "lay up" on a 3-dot-longer route north through Chicago that only saved that risk at $1,000 a turn.
        var (engine, player) = PlaceGame4184Player("6:8", "Los Angeles", "Chattanooga", whiteDice: [6, 1], redDie: 3,
            usedSegments: ["7:43-7:68:24", "7:43-7:44:24", "7:33-7:44:24", "7:27-7:33:24", "7:26-7:27:24", "7:25-7:26:24", "7:24-7:25:24", "7:23-7:24:24", "6:61-7:23:24", "6:60-6:61:24", "6:58-6:60:24", "6:58-6:59:24", "6:28-6:59:24", "6:17-6:28:24", "6:14-6:17:24", "6:12-6:14:24", "6:12-6:8:24"]);
        var chicago = FindCityNode(engine, "Chicago");

        var route = engine.SuggestRoute();
        var suggestion = EngineRouteSuggestion.Suggest(engine, player.Index, new RouteSuggestionOptions());
        WriteRoute(engine, player, route);
        WriteSuggestion(engine, suggestion);

        Assert.NotEqual(true, engine.LastApproachDecision?.LayUp);
        Assert.Equal(FindCityNode(engine, "Chattanooga"), route.NodeIds[^1]);
        Assert.DoesNotContain(chicago, route.NodeIds);
        Assert.Equal(RouteSuggestionStatus.Success, suggestion.Status);
        Assert.NotEqual(true, suggestion.Approach?.LayUp);
        Assert.DoesNotContain(chicago, suggestion.NodeIds);

        // Both stay on thermous's own lines until close in, rather than riding 11 dots of Pooper's track where a short
        // roll next turn pays Pooper twice.
        Assert.True(CountOpponentDots(engine, player, route) <= 4);
        Assert.True(suggestion.Segments.Count(segment => segment.OwnershipCategory == RailroadOwnershipCategory.Unfriendly) <= 4);
    }

    [Fact]
    public void TripToPhoenix_TakesTheUnionPacificThroughLasVegas_InsteadOfLayingUp()
    {
        // Issue #96: thermous (Superchief) on its own UP east of Kansas City, Chattanooga -> Phoenix, rolled
        // 2+5+(2). Phoenix is 26 dots away, well beyond next turn, but the suggestion used to weigh a "lay-up" and
        // sent the player through Minneapolis: once the shared search budget ran out, getting off Phoenix after the
        // detour was priced at one fee instead of two.
        var (engine, player) = PlaceGame4184Player("5:66", "Chattanooga", "Phoenix", whiteDice: [2, 5], redDie: 2,
            usedSegments: ["2:42-4:15:8", "4:15-4:19:8", "4:19-4:21:8", "4:21-4:22:8", "4:22-4:36:26", "3:58-4:36:26", "3:56-3:58:26", "3:56-5:14:26", "5:14-5:16:26", "5:16-5:19:26", "5:19-5:20:26", "5:20-5:77:24", "5:66-5:77:24"]);
        var lasVegas = FindCityNode(engine, "Las Vegas");

        var route = engine.SuggestRoute();
        var suggestion = EngineRouteSuggestion.Suggest(engine, player.Index, new RouteSuggestionOptions());
        WriteRoute(engine, player, route);
        WriteSuggestion(engine, suggestion);

        Assert.False(engine.LastApproachDecision?.Evaluated ?? false);
        Assert.Contains(lasVegas, route.NodeIds);
        Assert.Equal(RouteSuggestionStatus.Success, suggestion.Status);
        Assert.False(suggestion.Approach?.Evaluated ?? false);
        Assert.Contains(lasVegas, suggestion.NodeIds);
    }

    // Railroad owners by index (player index) in game 4184179d, from issues #95 and #96 (all railroads sold).
    private static readonly int[] Game4184Owners = [0, 0, 3, 3, 0, 1, 2, 2, 3, 1, 0, 3, 0, 3, 3, 0, 1, 2, 0, 0, 0, 1, 2, 2, 0, 0, 0, 3, 2];

    private static (Boxcars.Engine.Domain.GameEngine Engine, Player Player) PlaceGame4184Player(
        string nodeId,
        string tripStartCityName,
        string destinationName,
        int[] whiteDice,
        int redDie,
        IReadOnlyList<string> usedSegments)
    {
        var map = MapDefinition.Parse(File.ReadAllText(FindStandardMapPath())).Definition
            ?? throw new InvalidOperationException("The standard map could not be parsed.");
        var engine = new Boxcars.Engine.Domain.GameEngine(
            map,
            ["thermous", "Balenco", "Connecto", "Pooper"],
            new FixedRandomProvider(),
            GameSettings.Default with { HomeCityChoice = false, HomeSwapping = false });
        foreach (var railroad in engine.Railroads)
        {
            var owner = engine.Players[Game4184Owners[railroad.Index]];
            railroad.Owner = owner;
            owner.OwnedRailroads.Add(railroad);
        }

        typeof(Boxcars.Engine.Domain.GameEngine).GetProperty(nameof(engine.AllRailroadsSold))!.SetValue(engine, true);

        var player = engine.Players[0];
        player.LocomotiveType = LocomotiveType.Superchief;
        player.CurrentNodeId = nodeId;
        player.TripOriginCity = map.Cities.First(city => city.Name == tripStartCityName);
        player.Destination = map.Cities.First(city => city.Name == destinationName);
        foreach (var segmentKey in usedSegments)
        {
            var separator = segmentKey.LastIndexOf(':');
            var nodes = segmentKey[..separator].Split('-');
            player.UsedSegments.Add(new SegmentKey(nodes[0], nodes[1], int.Parse(segmentKey[(separator + 1)..])));
        }

        engine.CurrentTurn.ActivePlayer = player;
        engine.CurrentTurn.Phase = TurnPhase.Move;
        engine.CurrentTurn.DiceResult = new DiceResult(whiteDice, redDie);
        engine.CurrentTurn.MovementAllowance = whiteDice.Sum() + redDie;
        engine.CurrentTurn.MovementRemaining = whiteDice.Sum() + redDie;
        return (engine, player);
    }

    private static RailroadOwnershipCategory ResolveOwnership(Boxcars.Engine.Domain.GameEngine engine, Player player, int railroadIndex)
    {
        var owner = engine.Railroads.First(railroad => railroad.Index == railroadIndex).Owner;
        return owner is null ? RailroadOwnershipCategory.Public
            : owner == player ? RailroadOwnershipCategory.Friendly
            : RailroadOwnershipCategory.Unfriendly;
    }

    private void WriteSuggestion(Boxcars.Engine.Domain.GameEngine engine, RouteSuggestionResult suggestion)
    {
        output.WriteLine($"{suggestion.Status} {suggestion.Message} {suggestion.Segments.Count} dots, cost {suggestion.TotalCost:N0}");
        foreach (var segment in suggestion.Segments)
        {
            output.WriteLine($"  {segment.FromNodeId}->{segment.ToNodeId} {engine.Railroads.First(railroad => railroad.Index == segment.RailroadIndex).ShortName} ({segment.OwnershipCategory})");
        }
    }

    // Owners in the game these positions come from (all railroads sold).
    private static readonly Dictionary<string, string[]> StandardGameOwners = new()
    {
        ["thermous"] = ["B&O", "SOU", "CB&Q", "T&P", "SP", "CRI&P", "WP", "GN"],
        ["Balenco"] = ["MP", "C&NW", "L&N", "UP", "SLSF"],
        ["El Cheapo"] = ["NYC", "PA", "B&M", "NYNH&H", "SAL", "N&W", "GM&O", "NP"],
        ["Pooper"] = ["C&O", "ACL", "RF&P", "IC", "AT&SF", "CMStP&P", "D&RGW"]
    };

    // Owners late in the game from issues #68-#70 (all railroads sold).
    private static readonly Dictionary<string, string[]> LateGameOwners = new()
    {
        ["thermous"] = ["NYC", "NYNH&H", "C&O", "CB&Q", "AT&SF", "CMStP&P", "GM&O", "UP", "WP", "NP"],
        ["Balenco"] = ["PA"],
        ["Connecto"] = ["B&M", "ACL", "SAL", "IC", "SP", "SLSF"],
        ["gjhill03"] = ["B&O", "SOU", "RF&P", "MP", "C&NW", "L&N", "T&P", "N&W", "D&RGW", "CRI&P", "GN"]
    };

    private void WriteRoute(Boxcars.Engine.Domain.GameEngine engine, Player player, Route route)
    {
        output.WriteLine($"{route.Segments.Count} dots, cost {route.TotalCost:N0}");
        var index = 0;
        while (index < route.Segments.Count)
        {
            var railroad = engine.Railroads.First(candidate => candidate.Index == route.Segments[index].RailroadIndex);
            var runEnd = index;
            while (runEnd < route.Segments.Count && route.Segments[runEnd].RailroadIndex == railroad.Index)
            {
                runEnd++;
            }

            var owner = railroad.Owner is null ? "bank" : railroad.Owner == player ? "own" : railroad.Owner.Name;
            output.WriteLine($"  {railroad.ShortName} ({owner}) x{runEnd - index}");
            index = runEnd;
        }
    }

    private static Boxcars.Engine.Domain.GameEngine CreateEngine(Dictionary<string, string[]> railroadsByOwner)
    {
        var map = MapDefinition.Parse(File.ReadAllText(FindStandardMapPath())).Definition
            ?? throw new InvalidOperationException("The standard map could not be parsed.");
        var engine = new Boxcars.Engine.Domain.GameEngine(
            map,
            railroadsByOwner.Keys.ToArray(),
            new FixedRandomProvider(),
            GameSettings.Default with { HomeCityChoice = false, HomeSwapping = false });

        foreach (var (ownerName, shortNames) in railroadsByOwner)
        {
            var owner = engine.Players.First(candidate => candidate.Name == ownerName);
            foreach (var shortName in shortNames)
            {
                var railroad = engine.Railroads.First(candidate => candidate.ShortName == shortName);
                railroad.Owner = owner;
                owner.OwnedRailroads.Add(railroad);
            }
        }

        // AllRailroadsSold has a private setter; every railroad is owned in these positions.
        typeof(Boxcars.Engine.Domain.GameEngine).GetProperty(nameof(engine.AllRailroadsSold))!.SetValue(engine, engine.Railroads.All(railroad => railroad.Owner is not null));
        return engine;
    }

    private static Player PlaceActivePlayer(
        Boxcars.Engine.Domain.GameEngine engine,
        string playerName,
        string cityName,
        string destinationName,
        LocomotiveType locomotiveType,
        int movementRemaining)
    {
        var player = engine.Players.First(candidate => candidate.Name == playerName);
        player.LocomotiveType = locomotiveType;
        player.CurrentCity = engine.MapDefinition.Cities.First(city => city.Name == cityName);
        player.CurrentNodeId = FindCityNode(engine, cityName);
        player.Destination = engine.MapDefinition.Cities.First(city => city.Name == destinationName);
        player.TripOriginCity = player.CurrentCity;

        engine.CurrentTurn.ActivePlayer = player;
        engine.CurrentTurn.Phase = TurnPhase.Move;
        engine.CurrentTurn.MovementRemaining = movementRemaining;
        return player;
    }

    private static string FindCityNode(Boxcars.Engine.Domain.GameEngine engine, string cityName)
    {
        var city = engine.MapDefinition.Cities.First(candidate => candidate.Name == cityName);
        var region = engine.MapDefinition.Regions.First(candidate => candidate.Code == city.RegionCode);
        return $"{region.Index}:{city.MapDotIndex}";
    }

    private static string FindStandardMapPath([CallerFilePath] string sourceFilePath = "")
    {
        // tests/Boxcars.Engine.Tests/Unit/<this file> -> src/Boxcars/U21MAP.RB3
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFilePath)!, "..", "..", "..", "src", "Boxcars", "U21MAP.RB3"));
    }
}

/// <summary>
/// Route planning tests that must not run in parallel: one of them sets the planner's shared time limit to zero.
/// </summary>
[CollectionDefinition(Name)]
public sealed class RoutePlanningCollection
{
    public const string Name = "Route planning";
}
