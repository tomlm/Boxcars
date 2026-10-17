using Boxcars.Engine.Data.Maps;
using Boxcars.Services.Maps;

namespace Boxcars.Engine.Tests.Unit;

public class BoardProjectionServiceTests
{
    [Fact]
    public void Project_RailroadsSharingAStretch_AreDrawnApartEvenWhenListedInOppositeDirections()
    {
        var map = MapDefinition.Parse(File.ReadAllText(FindStandardMapPath())).Definition;

        var segments = new BoardProjectionService().Project(map).Model.RailroadSegments;
        var sharedStretches = segments
            .GroupBy(segment => EdgeKey(segment.StartRegionIndex, segment.StartDotIndex, segment.EndRegionIndex, segment.EndDotIndex))
            .Where(group => group.Count() > 1)
            .ToList();

        Assert.NotEmpty(sharedStretches);
        foreach (var stretch in sharedStretches)
        {
            var midpoints = stretch.Select(segment => ((segment.X1 + segment.X2) / 2, (segment.Y1 + segment.Y2) / 2)).ToList();
            for (var i = 0; i < midpoints.Count; i++)
            {
                for (var j = i + 1; j < midpoints.Count; j++)
                {
                    var distance = Math.Sqrt(Math.Pow(midpoints[i].Item1 - midpoints[j].Item1, 2) + Math.Pow(midpoints[i].Item2 - midpoints[j].Item2, 2));

                    // Owned rails are drawn 0.95 wide, so anything closer would touch.
                    Assert.True(distance > 1.0, $"Railroads on stretch {stretch.Key} are only {distance:F2} apart.");
                }
            }
        }
    }

    private static string EdgeKey(int startRegion, int startDot, int endRegion, int endDot)
    {
        var start = (startRegion, startDot);
        var end = (endRegion, endDot);
        return start.CompareTo(end) <= 0 ? $"{start}-{end}" : $"{end}-{start}";
    }

    private static string FindStandardMapPath([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
    {
        // tests/Boxcars.Engine.Tests/Unit/<this file> -> src/Boxcars/U21MAP.RB3
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFilePath)!, "..", "..", "..", "src", "Boxcars", "U21MAP.RB3"));
    }
}
