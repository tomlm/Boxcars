using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Persistence;

namespace Boxcars.Services;

/// <summary>How often each city has been drawn as a destination so far, next to how often it should be.</summary>
public sealed record DestinationVisits(IReadOnlyList<DestinationVisitRow> Rows, int TotalDraws)
{
    public static DestinationVisits Empty { get; } = new([], 0);

    /// <summary>The draws the engine has counted in the game's statistics (see GameStatistics.DestinationDraws).</summary>
    public static DestinationVisits Build(GameState state, MapDefinition mapDefinition)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(mapDefinition);

        var draws = state.Statistics?.DestinationDraws ?? [];
        var totalDraws = draws.Values.Sum();
        var regionProbabilities = mapDefinition.Regions.ToDictionary(region => region.Code, region => region.Probability ?? 0d, StringComparer.OrdinalIgnoreCase);
        var rows = mapDefinition.Cities
            .Select(city =>
            {
                var drawCount = draws.GetValueOrDefault(city.Name);
                return new DestinationVisitRow(
                    city.Name,
                    city.RegionCode,
                    ExpectedPercentage: regionProbabilities.GetValueOrDefault(city.RegionCode) * (city.Probability ?? 0d) / 100d,
                    ActualPercentage: totalDraws > 0 ? drawCount * 100d / totalDraws : 0d,
                    drawCount);
            })
            .OrderBy(row => row.CityName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new DestinationVisits(rows, totalDraws);
    }
}

/// <param name="ExpectedPercentage">Chance of drawing the city on any one draw, from the game's odds.</param>
/// <param name="ActualPercentage">Share of this game's draws that were the city.</param>
public sealed record DestinationVisitRow(string CityName, string RegionCode, double ExpectedPercentage, double ActualPercentage, int DrawCount);
