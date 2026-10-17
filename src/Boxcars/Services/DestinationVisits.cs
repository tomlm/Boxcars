using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Persistence;

namespace Boxcars.Services;

/// <summary>How often each city has been drawn as a destination so far, next to how often it should be.</summary>
public sealed record DestinationVisits(IReadOnlyList<DestinationVisitRow> Rows, int TotalDraws)
{
    public static DestinationVisits Empty { get; } = new([], 0);

    /// <summary>
    /// Counts destination draws in saved game states, in the order they happened: each time a player gets a new
    /// destination or a new alternate destination. A declared player heading home isn't a draw.
    /// </summary>
    public static DestinationVisits Build(IEnumerable<GameState> statesInOrder, MapDefinition mapDefinition)
    {
        ArgumentNullException.ThrowIfNull(statesInOrder);
        ArgumentNullException.ThrowIfNull(mapDefinition);

        var draws = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var previousDestinations = new Dictionary<int, string?>();
        var previousAlternates = new Dictionary<int, string?>();
        void CountIfNew(Dictionary<int, string?> previous, int playerIndex, string? cityName)
        {
            if (!string.IsNullOrWhiteSpace(cityName)
                && !string.Equals(previous.GetValueOrDefault(playerIndex), cityName, StringComparison.OrdinalIgnoreCase))
            {
                draws[cityName] = draws.GetValueOrDefault(cityName) + 1;
            }

            previous[playerIndex] = cityName;
        }

        foreach (var state in statesInOrder)
        {
            for (var playerIndex = 0; playerIndex < state.Players.Count; playerIndex++)
            {
                var player = state.Players[playerIndex];
                CountIfNew(previousDestinations, playerIndex, player.HasDeclared ? null : player.DestinationCityName);
                CountIfNew(previousAlternates, playerIndex, player.AlternateDestinationCityName);
            }
        }

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
