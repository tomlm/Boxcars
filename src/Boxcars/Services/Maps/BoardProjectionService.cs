using Boxcars.Data.Maps;

namespace Boxcars.Services.Maps;

public sealed class BoardProjectionService
{
    public BoardProjectionResult Project(MapDefinition mapDefinition)
    {
        var warnings = new List<string>();
        var cities = new List<CityRenderItem>();

        var regionCodeToIndex = mapDefinition.Regions
            .Select((region, index) => new { region.Code, RegionIndex = index + 1 })
            .ToDictionary(entry => entry.Code, entry => entry.RegionIndex, StringComparer.OrdinalIgnoreCase);

        foreach (var city in mapDefinition.Cities)
        {
            if (!regionCodeToIndex.TryGetValue(city.RegionCode, out var regionIndex))
            {
                warnings.Add($"City '{city.Name}' has unknown region code '{city.RegionCode}'.");
                continue;
            }

            var cityDot = mapDefinition.TrainDots.FirstOrDefault(dot =>
                dot.RegionIndex == regionIndex &&
                city.MapDotIndex.HasValue &&
                dot.DotIndex == city.MapDotIndex.Value);

            if (cityDot is null)
            {
                warnings.Add($"City '{city.Name}' references missing map dot '{city.MapDotIndex}' in region '{city.RegionCode}'.");
                continue;
            }

            cities.Add(new CityRenderItem
            {
                Name = city.Name,
                RegionCode = city.RegionCode,
                X = cityDot.X,
                Y = cityDot.Y
            });
        }

        var model = new BoardRenderModel
        {
            Cities = cities,
            TrainDots = mapDefinition.TrainDots,
            MapLines = mapDefinition.MapLines,
            Separators = mapDefinition.Separators
        };

        return new BoardProjectionResult
        {
            Model = model,
            Warnings = warnings
        };
    }
}
