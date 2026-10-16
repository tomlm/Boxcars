using System.Globalization;
using Boxcars.Data;
using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Persistence;
using Boxcars.Services.Maps;

namespace Boxcars.Services.Reports;

/// <summary>
/// Builds the one-page railroad and probability reference (railroad summary, city access and region
/// probabilities) for a game's map, or the default map when no game is given.
/// </summary>
public sealed class RailroadReportService(
    GameService gameService,
    MapAnalysisService mapAnalysisService,
    IWebHostEnvironment webHostEnvironment)
{
    private const int CityColumnCount = 3;
    private const int RailroadNameWidth = 30;
    private const int CityNameWidth = 17;
    private const int RegionLineWidth = 30;

    /// <summary>Returns the report as a PDF, or null when <paramref name="gameId"/> doesn't match a game.</summary>
    public async Task<byte[]?> CreatePdfAsync(string? gameId, CancellationToken cancellationToken)
    {
        var game = await LoadGameAsync(gameId, cancellationToken);
        if (game.NotFound)
        {
            return null;
        }

        var mapDefinition = await LoadMapDefinitionAsync(game.Entity, cancellationToken);
        var report = mapAnalysisService.BuildReport(mapDefinition);
        var income = new RailroadIncomeEstimate(
            mapAnalysisService.BuildExpectedFeePaymentsPerTurn(mapDefinition),
            game.Entity?.UnfriendlyFee1 ?? GameSettings.Default.UnfriendlyFee1,
            game.Entity?.UnfriendlyFee2 ?? GameSettings.Default.UnfriendlyFee2);
        return TextPdfWriter.Write(BuildReportLines(report, income, DateTimeOffset.UtcNow));
    }

    public static IReadOnlyList<string> BuildReportLines(
        MapAnalysisReport report,
        RailroadIncomeEstimate income,
        DateTimeOffset generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(income);

        var lines = new List<string>
        {
            FormattableString.Invariant($"Boxcars City/Rail Information - Map {report.MapName} {generatedAtUtc:yyyy/MM/dd HH:mm:ss} UTC"),
            string.Empty
        };

        AddRailroadSummary(lines, report.RailroadRows, income);
        lines.Add(string.Empty);
        AddCityAccess(lines, report.CityAccessRows);
        lines.Add(string.Empty);
        AddRegionProbabilities(lines, report.RegionProbabilityRows);
        lines.Add(string.Empty);

        lines.Add(FormattableString.Invariant($"Average trip length (dots) {report.AverageTripLengthDots:N0}"));
        lines.Add(FormattableString.Invariant($"Average payoff ${report.AveragePayoff:N0}"));
        lines.Add(FormattableString.Invariant($"Average payoff per dot travelled ${report.AveragePayoffPerDot:N0}"));

        return lines;
    }

    private static void AddRailroadSummary(
        List<string> lines,
        IReadOnlyList<RailroadAnalysisRow> railroads,
        RailroadIncomeEstimate income)
    {
        decimal Pays(RailroadAnalysisRow railroad) => income.ExpectedPaymentsPerTurn.GetValueOrDefault(railroad.RailroadIndex) * income.GameTurns;
        string Income(decimal payments) => FormatCurrency((int)Math.Round(payments * income.AverageFee));

        lines.Add(Center("HANDY REFERENCE/SUMMARY CHART"));
        lines.Add(FormattableString.Invariant($"{"RR",-8}{"FULL NAME",-RailroadNameWidth}{"PRICE",9}{"#CITY",6}{"SERV%",7}{"MONOP%",8}{"RRCON",7}{"PAYS",6}{"INCOME",11}"));

        foreach (var railroad in railroads)
        {
            var pays = Pays(railroad);
            lines.Add(FormattableString.Invariant(
                $"{Truncate(railroad.RailroadCode, 7),-8}{Truncate(railroad.FullName, RailroadNameWidth - 1),-RailroadNameWidth}{FormatCurrency(railroad.PurchasePrice),9}{railroad.CitiesServedCount,6}{railroad.ServicePercentage,7:N2}{railroad.MonopolyPercentage,8:N2}{railroad.ConnectionCount,7}{pays,6:N1}{Income(pays),11}"));
        }

        if (railroads.Count == 0)
        {
            return;
        }

        var averagePays = railroads.Average(Pays);
        lines.Add(FormattableString.Invariant(
            $"{string.Empty,-8}{$"{railroads.Count} RR Averages",-RailroadNameWidth}{FormatCurrency((int)Math.Round(railroads.Average(railroad => railroad.PurchasePrice))),9}{railroads.Average(railroad => railroad.CitiesServedCount),6:N1}{railroads.Average(railroad => railroad.ServicePercentage),7:N2}{railroads.Average(railroad => railroad.MonopolyPercentage),8:N2}{railroads.Average(railroad => railroad.ConnectionCount),7:N1}{averagePays,6:N1}{Income(averagePays),11}"));
        lines.Add(FormattableString.Invariant(
            $"PAYS: expected fee payments in an average {income.GameTurns}-turn game (one per rider turn on the RR)."));
        lines.Add(FormattableString.Invariant(
            $"INCOME: PAYS x {FormatCurrency(income.AverageFee)} (half the game at {FormatCurrency(income.FeeBeforeAllSold)}, half at {FormatCurrency(income.FeeAfterAllSold)} after all RRs sell)."));
    }

    private static void AddCityAccess(List<string> lines, IReadOnlyList<CityAccessRow> cities)
    {
        lines.Add(Center(FormattableString.Invariant($"CITY ACCESS (DESTINATION FREQUENCY) PERCENTAGES -- {cities.Count} destinations")));

        // Highest first, filled down each column like the printed reference.
        var sortedCities = cities
            .OrderByDescending(city => city.GlobalAccessPercentage)
            .ThenBy(city => city.CityName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var rowCount = (int)Math.Ceiling(sortedCities.Count / (double)CityColumnCount);

        for (var row = 0; row < rowCount; row++)
        {
            var cells = Enumerable.Range(0, CityColumnCount)
                .Select(column => (column * rowCount) + row)
                .Where(index => index < sortedCities.Count)
                .Select(index => sortedCities[index])
                .Select(city => FormattableString.Invariant(
                    $"{city.GlobalAccessPercentage,5:N2} {Truncate(city.RegionCode, 2),-2} {Truncate(city.CityName, CityNameWidth),-CityNameWidth}"));
            lines.Add(string.Join(' ', cells).TrimEnd());
        }
    }

    private static void AddRegionProbabilities(List<string> lines, IReadOnlyList<RegionProbabilityRow> regions)
    {
        lines.Add(Center(FormattableString.Invariant($"REGION PROBABILITIES -- {regions.Count} regions")));

        foreach (var region in regions)
        {
            var label = $"{region.RegionCode} {region.RegionName} ";
            lines.Add(FormattableString.Invariant($"{label.PadRight(RegionLineWidth, '.')} {region.ProbabilityPercentage,5:N2}"));
        }
    }

    /// <summary>No game id means the default map; an id that doesn't match a game is reported as not found.</summary>
    private async Task<(GameEntity? Entity, bool NotFound)> LoadGameAsync(string? gameId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return (null, false);
        }

        var game = await gameService.GetGameAsync(gameId, cancellationToken);
        return (game, game is null);
    }

    private async Task<MapDefinition> LoadMapDefinitionAsync(GameEntity? game, CancellationToken cancellationToken)
    {
        var mapFileName = string.IsNullOrWhiteSpace(game?.MapFileName) ? GameService.DefaultMapFileName : game.MapFileName;
        var cityProbabilityOverrides = game?.CityProbabilityOverrides ?? [];
        var railroadPriceOverrides = game?.RailroadPriceOverrides ?? [];

        var resolvedMapFileName = Path.GetFileName(mapFileName);
        var mapPath = Path.Combine(webHostEnvironment.ContentRootPath, resolvedMapFileName);
        if (!File.Exists(mapPath))
        {
            throw new InvalidOperationException($"Map file '{resolvedMapFileName}' was not found in '{webHostEnvironment.ContentRootPath}'.");
        }

        await using var stream = File.OpenRead(mapPath);
        var loadResult = await MapDefinition.LoadAsync(resolvedMapFileName, stream, cancellationToken);
        if (!loadResult.Succeeded || loadResult.Definition is null)
        {
            throw new InvalidOperationException($"Unable to load map '{resolvedMapFileName}': {string.Join("; ", loadResult.Errors)}");
        }

        return MapProbabilityService.ApplyCityProbabilities(loadResult.Definition, cityProbabilityOverrides, railroadPriceOverrides);
    }

    private static string FormatCurrency(int value)
    {
        return string.Concat("$", value.ToString("N0", CultureInfo.InvariantCulture));
    }


    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static string Center(string text)
    {
        const int reportWidth = 92;
        return text.Length >= reportWidth
            ? text
            : new string(' ', (reportWidth - text.Length) / 2) + text;
    }
}

/// <summary>
/// Potential fee income for a railroad's owner over a typical game: expected payments per turn of rider travel,
/// over <see cref="GameTurns"/> turns, with half the game at each fee.
/// </summary>
/// <param name="ExpectedPaymentsPerTurn">Expected fee payments to each railroad's owner per turn of travel, by railroad index.</param>
/// <param name="FeeBeforeAllSold">Fee per payment while railroads are still for sale.</param>
/// <param name="FeeAfterAllSold">Fee per payment once every railroad has been sold.</param>
public sealed record RailroadIncomeEstimate(
    IReadOnlyDictionary<int, decimal> ExpectedPaymentsPerTurn,
    int FeeBeforeAllSold,
    int FeeAfterAllSold)
{
    public const int AverageGameTurns = 80;

    public int GameTurns { get; init; } = AverageGameTurns;

    public int AverageFee => (FeeBeforeAllSold + FeeAfterAllSold) / 2;
}
