using System.Text.Json;
using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Persistence;
using Boxcars.Engine.Tests.TestDoubles;
using Xunit.Abstractions;
namespace Boxcars.Engine.Tests.Unit;
public class ZzStuckProbe(ITestOutputHelper output)
{
    [Fact]
    public void Probe()
    {
        var map = MapDefinition.Parse(File.ReadAllText(@"S:\github\Boxcars\src\Boxcars\U21MAP.RB3")).Definition!;
        var state = JsonSerializer.Deserialize<GameState>(File.ReadAllText(@"C:\Users\therm\AppData\Local\Temp\claude\s--github-Boxcars\37f0c1ca-a95d-47a4-900c-98a95fcd42ee\scratchpad\bumpus.json"))!;
        var engine = Boxcars.Engine.Domain.GameEngine.FromSnapshot(state, map, new FixedRandomProvider(), GameSettings.Default);
        var p = engine.CurrentTurn.ActivePlayer;
        output.WriteLine($"{p.Name} at {p.CurrentNodeId} -> {p.Destination?.Name} used={string.Join(",", p.UsedSegments)}");
        var route = engine.SuggestRoute();
        output.WriteLine($"route nodes={string.Join(",", route.NodeIds)} segs={route.Segments.Count} cost={route.TotalCost}");
    }
}
