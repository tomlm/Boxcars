using System.Text;
using System.Text.RegularExpressions;
using Boxcars.Engine.Data.Maps;
using global::Boxcars.Data;
using global::Boxcars.Services;
using global::Boxcars.Services.Maps;
using global::Boxcars.Services.Reports;

namespace Boxcars.Engine.Tests.Unit;

public class RailroadReportTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 10, 6, 18, 22, 7, TimeSpan.Zero);

    [Fact]
    public void BuildReportLines_IncludesEachSectionOfTheReference()
    {
        var report = new MapAnalysisReport
        {
            MapName = "U21",
            RailroadRows =
            [
                new RailroadAnalysisRow { RailroadIndex = 0, RailroadCode = "PA", FullName = "Pennsylvania RR", PurchasePrice = 63_000, CitiesServedCount = 13, ServicePercentage = 26.84m, MonopolyPercentage = 0m, ConnectionCount = 17 },
                new RailroadAnalysisRow { RailroadIndex = 1, RailroadCode = "CRI&P", FullName = "Chicago, Rock Island, and Pacific", PurchasePrice = 29_500, CitiesServedCount = 10, ServicePercentage = 9.66m, MonopolyPercentage = 1.15m, ConnectionCount = 20 }
            ],
            CityAccessRows =
            [
                new CityAccessRow { RegionCode = "NC", CityName = "Columbus", GlobalAccessPercentage = 4.52m },
                new CityAccessRow { RegionCode = "SW", CityName = "San Francisco/Oakland", GlobalAccessPercentage = 4.25m },
                new CityAccessRow { RegionCode = "PL", CityName = "Omaha", GlobalAccessPercentage = 0.18m },
                new CityAccessRow { RegionCode = "NE", CityName = "Boston", GlobalAccessPercentage = 0.31m }
            ],
            RegionProbabilityRows =
            [
                new RegionProbabilityRow { RegionCode = "NE", RegionName = "NorthEast", ProbabilityPercentage = 7.63m }
            ],
            AverageTripLengthDots = 21m,
            AveragePayoff = 14_800m,
            AveragePayoffPerDot = 779m
        };

        var estimatedIncome = new Dictionary<int, double> { [0] = 16_852d, [1] = 7_924.4d };

        var lines = RailroadReportService.BuildReportLines(report, estimatedIncome, GeneratedAt);

        Assert.Equal("Boxcars City/Rail Information - Map U21 2026/10/06 18:22:07 UTC", lines[0]);
        Assert.Contains(lines, line => line.EndsWith("RRCON   INCOME", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("PA      Pennsylvania RR", StringComparison.Ordinal) && line.Contains("$63,000", StringComparison.Ordinal) && line.Contains("26.84", StringComparison.Ordinal) && line.EndsWith("  $16,852", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("2 RR Averages", StringComparison.Ordinal) && line.EndsWith("  $12,388", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("INCOME: estimated income from destinations served and routes carried.", StringComparison.Ordinal));
        // Long railroad names are truncated so the columns stay aligned.
        Assert.Contains(lines, line => line.StartsWith("CRI&P   Chicago, Rock Island, and Pac   $29,500", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("2 RR Averages", StringComparison.Ordinal) && line.Contains("$46,250", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("CITY ACCESS (DESTINATION FREQUENCY) PERCENTAGES -- 4 destinations", StringComparison.Ordinal));
        // Cities are sorted highest first and filled down each column.
        Assert.Contains(lines, line => line.StartsWith(" 4.52 NC Columbus", StringComparison.Ordinal) && line.Contains("0.31 NE Boston", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("NE NorthEast ....", StringComparison.Ordinal) && line.EndsWith(" 7.63", StringComparison.Ordinal));
        Assert.Equal("Average payoff per dot travelled $779", lines[^1]);
    }

    [Fact]
    public void BuildReportLines_ForTheU21Map_FitsOnOnePage()
    {
        var map = MapDefinition.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, GameService.DefaultMapFileName))).Definition!;
        var report = new MapAnalysisService(new MapRouteService()).BuildReport(map);

        var lines = RailroadReportService.BuildReportLines(report, MapRailroadPricingService.CalculateEstimatedIncome(map), GeneratedAt);

        Assert.InRange(lines.Count, 1, TextPdfWriter.LinesPerPage);
        Assert.Contains(lines, line => line.Contains($"{map.Railroads.Count} RR Averages", StringComparison.Ordinal));
    }

    [Fact]
    public void Write_ProducesAWellFormedPdf()
    {
        var pdf = TextPdfWriter.Write(["Hello (world) \\ test", "Second line"]);
        var text = Encoding.Latin1.GetString(pdf);

        Assert.StartsWith("%PDF-1.4", text, StringComparison.Ordinal);
        Assert.EndsWith("%%EOF\n", text, StringComparison.Ordinal);
        Assert.Contains("/BaseFont /Courier", text, StringComparison.Ordinal);
        Assert.Contains(@"(Hello \(world\) \\ test) Tj", text, StringComparison.Ordinal);

        // Every cross-reference entry must point at the start of its object.
        var startXref = int.Parse(Regex.Match(text, @"startxref\n(\d+)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.StartsWith("xref", text[startXref..], StringComparison.Ordinal);
        var offsets = Regex.Matches(text[startXref..], @"(\d{10}) 00000 n").Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        for (var index = 0; index < offsets.Count; index++)
        {
            Assert.StartsWith($"{index + 1} 0 obj", text[offsets[index]..], StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Write_ContinuesLongReportsOnAnotherPage()
    {
        var lines = Enumerable.Range(1, TextPdfWriter.LinesPerPage + 1).Select(line => $"Line {line}").ToList();

        var text = Encoding.Latin1.GetString(TextPdfWriter.Write(lines));

        Assert.Contains("/Count 2", text, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(text, @"/Type /Page /Parent").Count);
    }
}
