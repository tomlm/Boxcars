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

        var income = new RailroadIncomeEstimate(new Dictionary<int, decimal> { [0] = 0.12m, [1] = 0.03m }, 5_000, 10_000);

        var lines = RailroadReportService.BuildReportLines(report, income, GeneratedAt);

        Assert.Equal("Boxcars City/Rail Information - Map U21 2026/10/06 18:22:07 UTC", lines[0]);
        Assert.Contains(lines, line => line.EndsWith("RRCON  PAYS     INCOME", StringComparison.Ordinal));
        // 0.12 payments per turn over an 80-turn game is 9.6 payments, at an average fee of $7,500.
        Assert.Contains(lines, line => line.StartsWith("PA      Pennsylvania RR", StringComparison.Ordinal) && line.Contains("$63,000", StringComparison.Ordinal) && line.Contains("26.84", StringComparison.Ordinal) && line.EndsWith("   9.6    $72,000", StringComparison.Ordinal));
        Assert.Contains(lines, line => line == "PAYS: expected fee payments in an average 80-turn game (one per rider turn on the RR).");
        Assert.Contains(lines, line => line == "INCOME: PAYS x $7,500 (half the game at $5,000, half at $10,000 after all RRs sell).");
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
        var analysis = new MapAnalysisService(new MapRouteService());
        var report = analysis.BuildReport(map);
        var payments = analysis.BuildExpectedFeePaymentsPerTurn(map);

        var lines = RailroadReportService.BuildReportLines(report, new RailroadIncomeEstimate(payments, 5_000, 10_000), GeneratedAt);

        Assert.InRange(lines.Count, 1, TextPdfWriter.LinesPerPage);
        Assert.Contains(lines, line => line.Contains($"{map.Railroads.Count} RR Averages", StringComparison.Ordinal));
        // Every turn of travel rides at least one railroad, so payments per turn add up to at least one.
        Assert.True(payments.Values.Sum() >= 1m);
    }

    [Fact]
    public void BuildExpectedFeePaymentsPerTurn_CountsOnePaymentPerTurnOnEachRailroad()
    {
        var map = CreateTwoRailroadLineMap();

        var payments = new MapAnalysisService(new MapRouteService()).BuildExpectedFeePaymentsPerTurn(map, dotsPerTurn: 7);

        // Each trip is 15 dots, so 3 turns (dots 0-6, 7-13, 14).
        // West→East: RR1 (dots 0-9) is ridden in turns 1-2 and RR2 (dots 10-14) in turns 2-3.
        // East→West: RR2 is only ridden in turn 1 and RR1 in all three turns.
        // Over the 6 turns of the two equally likely trips: RR1 is paid 5 times, RR2 3 times.
        Assert.Equal(5m / 6m, payments[1], 10);
        Assert.Equal(0.5m, payments[2], 10);
    }

    private static MapDefinition CreateTwoRailroadLineMap()
    {
        // West (dot 0) to East (dot 15) in a straight line: RR1 for the first 10 dots, RR2 for the last 5.
        var map = new MapDefinition { Name = "Two Railroad Line", Version = "1.0" };
        map.Regions.Add(new RegionDefinition { Index = 0, Name = "Region", Code = "RG", Probability = 100d });
        map.Cities.Add(new CityDefinition { Name = "West", RegionCode = "RG", Probability = 50d, PayoutIndex = 1, MapDotIndex = 0 });
        map.Cities.Add(new CityDefinition { Name = "East", RegionCode = "RG", Probability = 50d, PayoutIndex = 2, MapDotIndex = 15 });
        map.Railroads.Add(new RailroadDefinition { Index = 1, Name = "First", ShortName = "R1" });
        map.Railroads.Add(new RailroadDefinition { Index = 2, Name = "Second", ShortName = "R2" });

        for (var dot = 0; dot <= 15; dot++)
        {
            map.TrainDots.Add(new TrainDot { Id = $"0:{dot}", RegionIndex = 0, DotIndex = dot, X = dot * 10, Y = 0 });
        }

        for (var dot = 0; dot < 15; dot++)
        {
            map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition
            {
                RailroadIndex = dot < 10 ? 1 : 2,
                StartRegionIndex = 0,
                StartDotIndex = dot,
                EndRegionIndex = 0,
                EndDotIndex = dot + 1
            });
        }

        return map;
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
