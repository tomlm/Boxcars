using System;
using System.IO;
using System.Linq;
using Boxcars.Engine.Data.Maps;

var mapPath = Path.Combine("src","Boxcars","U21MAP.RB3");
using var stream = File.OpenRead(mapPath);
var result = await MapDefinition.LoadAsync(Path.GetFileName(mapPath), stream, default);
if (!result.Succeeded)
{
    Console.WriteLine(string.Join(Environment.NewLine, result.Errors));
    return;
}
var map = result.Definition;
var mismatches = map.Cities
    .Where(c => c.MapDotIndex.HasValue)
    .Select(c => new {
        c.Name,
        c.RegionCode,
        c.MapDotIndex,
        RegionListIndex = map.Regions.FindIndex(r => string.Equals(r.Code, c.RegionCode, StringComparison.OrdinalIgnoreCase)),
        TrainDotRegionIndex = map.TrainDots.FirstOrDefault(d => d.DotIndex == c.MapDotIndex.Value && string.Equals(d.Label, c.Name, StringComparison.OrdinalIgnoreCase))?.RegionIndex
    })
    .Where(x => x.TrainDotRegionIndex.HasValue && x.RegionListIndex != x.TrainDotRegionIndex.Value)
    .Take(20)
    .ToList();
Console.WriteLine($"Mismatch count: {mismatches.Count}");
foreach (var item in mismatches)
{
    Console.WriteLine($"{item.Name}: regionList={item.RegionListIndex}, trainDotRegion={item.TrainDotRegionIndex}, dot={item.MapDotIndex}");
}
