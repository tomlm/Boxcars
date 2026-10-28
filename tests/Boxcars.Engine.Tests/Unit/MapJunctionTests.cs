using System.Runtime.CompilerServices;
using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Domain;
using Boxcars.Engine.Persistence;
using Boxcars.Engine.Tests.TestDoubles;
using Boxcars.Services.Maps;

namespace Boxcars.Engine.Tests.Unit;

/// <summary>
/// Junctions: dots where a railroad branches between pips (color 13 in the map file). On the B&amp;M, the line from
/// Portland ME (1:1) through 1:2 meets the lines from Albany (via 1:4) and Boston (1:10) at junction 1:3.
/// </summary>
public class MapJunctionTests
{
    private const string Junction = "1:3";
    private const string PortlandSide = "1:2";
    private const string AlbanySide = "1:4";
    private const string Boston = "1:10";

    [Fact]
    public void Parse_StandardMap_MarksItsElevenJunctions()
    {
        var map = LoadStandardMap();

        var junctions = map.TrainDots.Where(dot => dot.IsJunction).ToList();

        Assert.Equal(11, junctions.Count);
        Assert.Contains(junctions, dot => dot.RegionIndex == 1 && dot.DotIndex == 3);
        Assert.Contains(junctions, dot => dot.RegionIndex == 2 && dot.DotIndex == 62);
    }

    [Fact]
    public void BuildMovementLinks_TrackThroughAJunction_JoinsEveryPairOfStopsAroundIt()
    {
        var links = MovementLinks.Build(LoadStandardMap(), out var junctionDepartures);
        var railroadIndex = links.First(link => link.TrackNodeIds.Contains(Junction)).RailroadIndex;

        var throughJunction = links
            .Where(link => link.TrackNodeIds.Contains(Junction))
            .Select(link => (link.FromNodeId, link.ToNodeId, link.TrackNodeIds.Count, link.RailroadIndex))
            .OrderBy(link => link.FromNodeId, StringComparer.Ordinal)
            .ThenBy(link => link.ToNodeId, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            [(Boston, PortlandSide, 3, railroadIndex), (Boston, AlbanySide, 3, railroadIndex), (PortlandSide, AlbanySide, 3, railroadIndex)],
            throughJunction);
        Assert.DoesNotContain(links, link => link.FromNodeId == Junction || link.ToNodeId == Junction);
        // Only someone already standing on the junction rides away from it.
        Assert.Equal(3, junctionDepartures.Count(link => link.FromNodeId == Junction));
    }

    [Fact]
    public void SuggestRoute_BostonToPortland_PassesTheJunctionWithoutStopping()
    {
        var engine = CreateEngine();
        var player = PlaceActivePlayer(engine, Boston, "Portland ME", movementRemaining: 2);

        var route = engine.SuggestRoute();

        // Boston to Portland ME is two pips: through the junction to 1:2, then on to Portland ME (1:1).
        Assert.Equal([Boston, PortlandSide, "1:1"], route.NodeIds);
        Assert.Equal(player.CurrentNodeId, route.NodeIds[0]);
    }

    [Fact]
    public void MoveAlongRoute_ThroughAJunction_UsesOnePipAndRecordsEachPieceOfTrack()
    {
        var engine = CreateEngine();
        var player = PlaceActivePlayer(engine, Boston, "Portland ME", movementRemaining: 2);
        var railroadIndex = engine.Railroads.First(railroad => railroad.ShortName == "B&M").Index;
        engine.SaveRoute(new Route(
            [Boston, PortlandSide, "1:1"],
            [
                new RouteSegment { FromNodeId = Boston, ToNodeId = PortlandSide, RailroadIndex = railroadIndex },
                new RouteSegment { FromNodeId = PortlandSide, ToNodeId = "1:1", RailroadIndex = railroadIndex }
            ],
            0));

        engine.MoveAlongRoute(1);

        Assert.Equal(PortlandSide, player.CurrentNodeId);
        Assert.Equal(1, engine.CurrentTurn.MovementRemaining);
        Assert.Equal(
            [new SegmentKey(Boston, Junction, railroadIndex), new SegmentKey(Junction, PortlandSide, railroadIndex)],
            player.UsedSegments.Order().ToArray());
    }

    [Fact]
    public void MoveAlongRoute_BackThroughTheSameJunctionTrack_StopsBeforeRidingItAgain()
    {
        var engine = CreateEngine();
        var player = PlaceActivePlayer(engine, PortlandSide, "Albany", movementRemaining: 1);
        var railroadIndex = engine.Railroads.First(railroad => railroad.ShortName == "B&M").Index;
        // Earlier this trip the player came up from Boston, through the junction.
        player.UsedSegments.Add(new SegmentKey(Boston, Junction, railroadIndex));
        player.UsedSegments.Add(new SegmentKey(Junction, PortlandSide, railroadIndex));
        engine.SaveRoute(new Route(
            [PortlandSide, AlbanySide],
            [new RouteSegment { FromNodeId = PortlandSide, ToNodeId = AlbanySide, RailroadIndex = railroadIndex }],
            0));

        engine.MoveAlongRoute(1);

        // 1:2 to 1:4 rides the junction-to-1:2 track again, so the move goes nowhere.
        Assert.Equal(PortlandSide, player.CurrentNodeId);
    }

    [Fact]
    public void BuildContext_EdgeThroughAJunction_IsDrawnPieceByPiece()
    {
        var context = new MapRouteService().BuildContext(LoadStandardMap());

        var edge = context.Adjacency[Boston].Single(candidate => candidate.ToNodeId == PortlandSide);

        Assert.Equal(2, edge.Pieces.Count);
        Assert.Equal(edge.X1, edge.Pieces[0].X1);
        Assert.Equal(edge.Pieces[0].X2, edge.Pieces[1].X1);
        Assert.Equal(edge.X2, edge.Pieces[1].X2);
        Assert.True(edge.SharesTrackWith(context.Adjacency[Boston].Single(candidate => candidate.ToNodeId == AlbanySide)));
        Assert.False(context.Adjacency.ContainsKey(Junction) && context.Adjacency.Values.SelectMany(edges => edges).Any(candidate => candidate.ToNodeId == Junction));
    }

    private static Boxcars.Engine.Domain.GameEngine CreateEngine()
    {
        var engine = new Boxcars.Engine.Domain.GameEngine(
            LoadStandardMap(),
            ["thermous", "Pooper"],
            new FixedRandomProvider(),
            GameSettings.Default with { HomeCityChoice = false, HomeSwapping = false });
        return engine;
    }

    private static Player PlaceActivePlayer(Boxcars.Engine.Domain.GameEngine engine, string nodeId, string destinationName, int movementRemaining)
    {
        var player = engine.Players[0];
        player.CurrentNodeId = nodeId;
        player.Destination = engine.MapDefinition.Cities.First(city => city.Name == destinationName);
        player.TripOriginCity = player.CurrentCity;
        engine.CurrentTurn.ActivePlayer = player;
        engine.CurrentTurn.Phase = TurnPhase.Move;
        engine.CurrentTurn.MovementAllowance = movementRemaining;
        engine.CurrentTurn.MovementRemaining = movementRemaining;
        return player;
    }

    private static MapDefinition LoadStandardMap([CallerFilePath] string sourceFilePath = "")
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFilePath)!, "..", "..", "..", "src", "Boxcars", "U21MAP.RB3"));
        return MapDefinition.Parse(File.ReadAllText(path)).Definition
            ?? throw new InvalidOperationException("The standard map could not be parsed.");
    }
}
