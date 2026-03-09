using System.Text;
using Boxcars.Engine.Data.Maps;

namespace Boxcars.Engine.Tests.Unit;

/// <summary>
/// Tests for MapDefinition.LoadAsync / MapDefinition.Parse (.RB3 / .rbp map parsing).
/// </summary>
public class MapDefinitionParserTests
{
    private static Task<MapLoadResult> LoadAsync(string content, string fileName = "test.rb3")
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        return MapDefinition.LoadAsync(fileName, stream);
    }

    // ------------------------------------------------------------------
    // Input validation (LoadAsync)
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("test.txt")]
    [InlineData("test.csv")]
    [InlineData("test.json")]
    public async Task LoadAsync_UnsupportedFileExtension_ReturnsError(string fileName)
    {
        var result = await MapDefinition.LoadAsync(fileName, Stream.Null);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("Unsupported"));
    }

    [Theory]
    [InlineData("test.rb3")]
    [InlineData("test.RB3")]
    [InlineData("test.rbp")]
    [InlineData("test.RBP")]
    public async Task LoadAsync_SupportedFileExtension_DoesNotReturnUnsupportedError(string fileName)
    {
        var result = await LoadAsync(MinimalValidMap, fileName);

        Assert.DoesNotContain(result.Errors, e => e.Contains("Unsupported"));
    }

    [Fact]
    public async Task LoadAsync_NullStream_ReturnsError()
    {
        var result = await MapDefinition.LoadAsync("test.rb3", null!);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("unavailable"));
    }

    [Fact]
    public async Task LoadAsync_EmptyFile_ReturnsError()
    {
        var result = await LoadAsync("");

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("empty"));
    }

    // ------------------------------------------------------------------
    // Parse (string) – empty input
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_EmptyContent_ReturnsError()
    {
        var result = MapDefinition.Parse("");

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("empty"));
    }

    // ------------------------------------------------------------------
    // Header parsing
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_ValidHeader_SetsMapNameAndVersion()
    {
        var result = MapDefinition.Parse(MinimalValidMap);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Definition);
        Assert.Equal("Test Map", result.Definition.Name);
        Assert.Equal("1.0", result.Definition.Version);
    }

    [Fact]
    public void Parse_ValidHeader_SetsScaleBounds()
    {
        var result = MapDefinition.Parse(MinimalValidMap);

        Assert.True(result.Succeeded);
        var def = result.Definition!;
        Assert.Equal(10.0, def.ScaleLeft);
        Assert.Equal(20.0, def.ScaleTop);
        Assert.Equal(500.0, def.ScaleWidth);
        Assert.Equal(300.0, def.ScaleHeight);
    }

    [Fact]
    public void Parse_ValidHeader_SetsBackgroundKey()
    {
        var result = MapDefinition.Parse(MinimalValidMap);

        Assert.True(result.Succeeded);
        Assert.Equal("USA", result.Definition!.BackgroundKey);
    }

    [Fact]
    public void Parse_PaySection_BuildsSymmetricPayoutChart()
    {
        var content = MinimalValidMap + """

            '[pay]
            5.5,9
            4
            """;

        var result = MapDefinition.Parse(content);

        Assert.True(result.Succeeded);
        Assert.True(result.Definition!.TryGetPayout(1, 2, out var payout12));
        Assert.True(result.Definition.TryGetPayout(2, 1, out var payout21));
        Assert.True(result.Definition.TryGetPayout(2, 3, out var payout23));
        Assert.True(result.Definition.TryGetPayout(3, 3, out var sameCityPayout));

        Assert.Equal(5_500, payout12);
        Assert.Equal(5_500, payout21);
        Assert.Equal(4_000, payout23);
        Assert.Equal(0, sameCityPayout);
        Assert.Equal(3, result.Definition.MaxPayoutIndex);
    }

    [Fact]
    public void Parse_WithoutPaySection_FallsBackToLegacyPayoutTable()
    {
        var result = MapDefinition.Parse(MinimalValidMap);

        Assert.True(result.Succeeded);
        Assert.True(result.Definition!.TryGetPayout(0, 1, out var payout));
        Assert.Equal(5_500, payout);
    }

    [Fact]
    public void Parse_MissingScaleValues_ReturnsError()
    {
        var content = """
            '[header]
            name=Test
            '[city]
            TestCity,NE,10.0,1,1
            '[re01]
            s,100,200,1
            """;

        var result = MapDefinition.Parse(content);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("scalel") || e.Contains("scalew") || e.Contains("scaleh"));
    }

    // ------------------------------------------------------------------
    // Region parsing
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_RegionSection_ParsesRegions()
    {
        var result = MapDefinition.Parse(MinimalValidMap);

        Assert.True(result.Succeeded);
        var regions = result.Definition!.Regions;
        Assert.Equal(2, regions.Count);

        Assert.Equal("NorthEast", regions[0].Name);
        Assert.Equal("NE", regions[0].Code);
        Assert.Equal(20.833, regions[0].Probability);

        Assert.Equal("SouthEast", regions[1].Name);
        Assert.Equal("SE", regions[1].Code);
        Assert.Equal(12.5, regions[1].Probability);
    }

    [Fact]
    public void Parse_RegionSection_PreservesParsedRegionIndices()
    {
        var result = MapDefinition.Parse(MinimalValidMap);

        Assert.True(result.Succeeded);
        var regions = result.Definition!.Regions;

        Assert.Equal(1, regions[0].Index);
        Assert.Equal(2, regions[1].Index);
    }

    // ------------------------------------------------------------------
    // City parsing
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_CitySection_ParsesCities()
    {
        var result = MapDefinition.Parse(MinimalValidMap);

        Assert.True(result.Succeeded);
        var cities = result.Definition!.Cities;
        Assert.Equal(3, cities.Count);

        Assert.Equal("New York", cities[0].Name);
        Assert.Equal("NE", cities[0].RegionCode);
        Assert.Equal(19.444, cities[0].Probability);
        Assert.Equal(5, cities[0].PayoutIndex);
        Assert.Equal(1, cities[0].MapDotIndex);
    }

    [Fact]
    public void Parse_CitySection_PreservesPayoutIndex()
    {
        var result = MapDefinition.Parse(MinimalValidMap);

        Assert.True(result.Succeeded);
        var cities = result.Definition!.Cities;

        Assert.Equal(5, cities[0].PayoutIndex);
        Assert.Equal(3, cities[1].PayoutIndex);
        Assert.Equal(10, cities[2].PayoutIndex);
    }

    [Fact]
    public void Parse_CitySection_PreservesRegionCode()
    {
        var result = MapDefinition.Parse(MinimalValidMap);

        Assert.True(result.Succeeded);
        var cities = result.Definition!.Cities;

        Assert.Equal("NE", cities[0].RegionCode);
        Assert.Equal("NE", cities[1].RegionCode);
        Assert.Equal("SE", cities[2].RegionCode);
    }

    // ------------------------------------------------------------------
    // Railroad parsing
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_RailroadSection_ParsesRailroads()
    {
        var result = MapDefinition.Parse(MinimalValidMap);

        Assert.True(result.Succeeded);
        var railroads = result.Definition!.Railroads;
        Assert.Equal(2, railroads.Count);

        Assert.Equal("Boston & Maine", railroads[0].Name);
        Assert.Equal("B&M", railroads[0].ShortName);
        Assert.Equal(1, railroads[0].Index);

        Assert.Equal("Pennsylvania", railroads[1].Name);
        Assert.Equal("PA", railroads[1].ShortName);
        Assert.Equal(2, railroads[1].Index);
    }

    [Fact]
    public void Parse_RailroadSection_ParsesColorValues()
    {
        var result = MapDefinition.Parse(MinimalValidMap);

        Assert.True(result.Succeeded);
        var rr = result.Definition!.Railroads[0];
        Assert.Equal(1, rr.ColorIndex);
        Assert.Equal(255, rr.Red);
        Assert.Equal(128, rr.Green);
        Assert.Equal(64, rr.Blue);
    }

    // ------------------------------------------------------------------
    // Route parsing
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_RouteSection_ParsesRouteSegments()
    {
        var result = MapDefinition.Parse(MinimalValidMap);

        Assert.True(result.Succeeded);
        var segments = result.Definition!.RailroadRouteSegments;
        Assert.True(segments.Count > 0, "Expected route segments to be parsed");
    }

    [Fact]
    public void Parse_RouteSection_AssociatesSegmentsWithRailroad()
    {
        var result = MapDefinition.Parse(MinimalValidMap);

        Assert.True(result.Succeeded);
        var segments = result.Definition!.RailroadRouteSegments;

        var bmSegments = segments.Where(s => s.RailroadIndex == 1).ToList();
        Assert.True(bmSegments.Count > 0, "Expected B&M route segments");
        Assert.All(bmSegments, s => Assert.Equal(1, s.RailroadIndex));
    }

    [Fact]
    public void Parse_RouteSection_ParsesSegmentEndpoints()
    {
        var result = MapDefinition.Parse(MinimalValidMap);

        Assert.True(result.Succeeded);
        var segments = result.Definition!.RailroadRouteSegments;
        var first = segments[0];

        Assert.True(first.StartRegionIndex > 0);
        Assert.True(first.StartDotIndex > 0);
        Assert.True(first.EndRegionIndex > 0);
        Assert.True(first.EndDotIndex > 0);
    }

    // ------------------------------------------------------------------
    // Train dot parsing (re## sections)
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_RegionDotSection_ParsesTrainDots()
    {
        var result = MapDefinition.Parse(MinimalValidMap);

        Assert.True(result.Succeeded);
        var dots = result.Definition!.TrainDots;
        Assert.True(dots.Count >= 3, "Expected at least 3 train dots");
    }

    [Fact]
    public void Parse_RegionDotSection_SetsCorrectRegionIndex()
    {
        var result = MapDefinition.Parse(MinimalValidMap);

        Assert.True(result.Succeeded);
        var dots = result.Definition!.TrainDots;

        var region1Dots = dots.Where(d => d.RegionIndex == 1).ToList();
        var region2Dots = dots.Where(d => d.RegionIndex == 2).ToList();

        Assert.Equal(2, region1Dots.Count);
        Assert.Single(region2Dots);
    }

    [Fact]
    public void Parse_RegionDotSection_ParsesCoordinates()
    {
        var result = MapDefinition.Parse(MinimalValidMap);

        Assert.True(result.Succeeded);
        var dot = result.Definition!.TrainDots[0];

        Assert.Equal(100.5, dot.X);
        Assert.Equal(200.3, dot.Y);
    }

    [Fact]
    public void Parse_RegionDotSection_AssignsDotIndicesPerRegion()
    {
        var result = MapDefinition.Parse(MinimalValidMap);

        Assert.True(result.Succeeded);
        var dots = result.Definition!.TrainDots;

        var region1Dots = dots.Where(d => d.RegionIndex == 1).OrderBy(d => d.DotIndex).ToList();
        Assert.Equal(1, region1Dots[0].DotIndex);
        Assert.Equal(2, region1Dots[1].DotIndex);

        var region2Dots = dots.Where(d => d.RegionIndex == 2).ToList();
        Assert.Equal(1, region2Dots[0].DotIndex);
    }

    // ------------------------------------------------------------------
    // Line segment parsing (map / sep sections)
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_MapSection_ParsesLineSegments()
    {
        var result = MapDefinition.Parse(MinimalValidMap);

        Assert.True(result.Succeeded);
        var lines = result.Definition!.MapLines;
        Assert.Single(lines);

        Assert.Equal(10.0, lines[0].X1);
        Assert.Equal(20.0, lines[0].Y1);
        Assert.Equal(30.0, lines[0].X2);
        Assert.Equal(40.0, lines[0].Y2);
        Assert.Equal(1, lines[0].StyleIndex);
    }

    [Fact]
    public void Parse_SepSection_ParsesSeparators()
    {
        var result = MapDefinition.Parse(MinimalValidMap);

        Assert.True(result.Succeeded);
        var separators = result.Definition!.Separators;
        Assert.Single(separators);

        Assert.Equal(50.0, separators[0].X1);
        Assert.Equal(60.0, separators[0].Y1);
        Assert.Equal(70.0, separators[0].X2);
        Assert.Equal(80.0, separators[0].Y2);
    }

    // ------------------------------------------------------------------
    // Label parsing
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_LabelSection_ParsesRegionLabels()
    {
        var result = MapDefinition.Parse(MinimalValidMap);

        Assert.True(result.Succeeded);
        var labels = result.Definition!.RegionLabels;
        Assert.Single(labels);

        Assert.Equal("NorthEast", labels[0].Text);
        Assert.Equal(150.0, labels[0].X);
        Assert.Equal(75.0, labels[0].Y);
    }

    // ------------------------------------------------------------------
    // Validation
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_ZeroScaleWidth_ReturnsValidationError()
    {
        var content = """
            '[header]
            name=Test
            scalel=0
            scalet=0
            scalew=0
            scaleh=100
            sharebgnd=USA
            '[regn]
            East,E,50.0
            '[city]
            TestCity,E,10.0,1,1
            '[re01]
            s,100,200,1
            """;

        var result = MapDefinition.Parse(content);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("scale bounds"));
    }

    [Fact]
    public void Parse_NoCities_ReturnsValidationError()
    {
        var content = """
            '[header]
            name=Test
            scalel=0
            scalet=0
            scalew=500
            scaleh=300
            sharebgnd=USA
            '[regn]
            East,E,50.0
            '[re01]
            s,100,200,1
            """;

        var result = MapDefinition.Parse(content);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("city data"));
    }

    [Fact]
    public void Parse_NoTrainDots_ReturnsValidationError()
    {
        var content = """
            '[header]
            name=Test
            scalel=0
            scalet=0
            scalew=500
            scaleh=300
            sharebgnd=USA
            '[regn]
            East,E,50.0
            '[city]
            TestCity,E,10.0,1,1
            """;

        var result = MapDefinition.Parse(content);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("dot data"));
    }

    [Fact]
    public void Parse_NoBackgroundKey_ProducesWarning()
    {
        var content = """
            '[header]
            name=Test
            scalel=0
            scalet=0
            scalew=500
            scaleh=300
            '[regn]
            East,E,50.0
            '[city]
            TestCity,E,10.0,1,1
            '[re01]
            s,100,200,1
            """;

        var result = MapDefinition.Parse(content);

        Assert.True(result.Succeeded);
        Assert.Contains(result.Warnings, w => w.Contains("background key"));
    }

    // ------------------------------------------------------------------
    // Comment / blank line handling
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_CommentsAreIgnored()
    {
        var result = MapDefinition.Parse(MinimalValidMap);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Definition);
    }

    [Fact]
    public void Parse_BlankLinesAreIgnored()
    {
        var content = """
            '[header]
            name=Test

            scalel=0
            scalet=0

            scalew=500
            scaleh=300
            sharebgnd=USA

            '[regn]
            East,E,50.0

            '[city]
            TestCity,E,10.0,1,1

            '[re01]
            s,100,200,1
            """;

        var result = MapDefinition.Parse(content);

        Assert.True(result.Succeeded);
    }

    // ------------------------------------------------------------------
    // Minimal valid .RB3 content used across tests
    // ------------------------------------------------------------------

    private const string MinimalValidMap = """
        '[header]
        name=Test Map
        version=1.0
        scalel=10
        scalet=20
        scalew=500
        scaleh=300
        sharebgnd=USA

        '[regn]
        2
        NorthEast,NE,20.833
        SouthEast,SE,12.5

        '[city]
        3
        'NorthEast cities
        New York,NE,19.444,5,1
        Boston,NE,13.889,3,2
        'SouthEast cities
        Atlanta,SE,20.833,10,1

        '[rr]
        2
        Boston & Maine,BostonMaine,B&M,1,0,0,64,128,255
        Pennsylvania,Pennsylvania,PA,2,0,0,32,64,128

        '[re01]
        's dot data for region 1
        s,100.5,200.3,1
        s,150.0,250.0,2

        '[re02]
        s,300.0,400.0,1

        '[rout]
        'B&M
        1,1,2, 0,0

        'PA
        1,1, 2,1, 0,0

        '[map]
        l,10,20,30,40,1

        '[sep]
        l,50,60,70,80,2

        '[label]
        NorthEast,150.0,75.0
        """;
}
