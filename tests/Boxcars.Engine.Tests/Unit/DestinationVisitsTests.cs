using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Persistence;
using global::Boxcars.Services;

namespace Boxcars.Engine.Tests.Unit;

public class DestinationVisitsTests
{
    [Fact]
    public void Build_CountsEachNewDestinationOnce_AndComparesWithTheOdds()
    {
        var visits = DestinationVisits.Build(
        [
            CreateState(aliceDestination: "Boston", bobDestination: null),
            // The same destination across many saved states is one draw.
            CreateState(aliceDestination: "Boston", bobDestination: "Miami"),
            CreateState(aliceDestination: "Boston", bobDestination: "Miami"),
            // Arriving clears it; the next one is a new draw, even for the same city.
            CreateState(aliceDestination: null, bobDestination: "Miami"),
            CreateState(aliceDestination: "Boston", bobDestination: "Miami")
        ], CreateMap());

        Assert.Equal(3, visits.TotalDraws);
        var boston = visits.Rows.Single(row => row.CityName == "Boston");
        Assert.Equal(2, boston.DrawCount);
        Assert.Equal(200d / 3d, boston.ActualPercentage, precision: 6);
        // 75% region × 40% of the region.
        Assert.Equal(30d, boston.ExpectedPercentage, precision: 6);
        Assert.Equal(0, visits.Rows.Single(row => row.CityName == "Albany").DrawCount);
        Assert.Equal(["Albany", "Boston", "Miami"], visits.Rows.Select(row => row.CityName).ToArray());
    }

    [Fact]
    public void Build_DeclaredPlayerHeadingHome_IsNotADraw_ButTheAlternateIs()
    {
        var declared = CreateState(aliceDestination: "Albany", bobDestination: null);
        declared.Players[0].HasDeclared = true;
        declared.Players[0].AlternateDestinationCityName = "Miami";

        var visits = DestinationVisits.Build([declared], CreateMap());

        Assert.Equal(1, visits.TotalDraws);
        Assert.Equal(1, visits.Rows.Single(row => row.CityName == "Miami").DrawCount);
    }

    private static GameState CreateState(string? aliceDestination, string? bobDestination)
    {
        return new GameState
        {
            Players =
            [
                new PlayerState { Name = "Alice", DestinationCityName = aliceDestination },
                new PlayerState { Name = "Bob", DestinationCityName = bobDestination }
            ]
        };
    }

    private static MapDefinition CreateMap()
    {
        var map = new MapDefinition { Name = "Visits Test", Version = "1.0" };
        map.Regions.Add(new RegionDefinition { Index = 1, Name = "North East", Code = "NE", Probability = 75d });
        map.Regions.Add(new RegionDefinition { Index = 2, Name = "South East", Code = "SE", Probability = 25d });
        map.Cities.Add(new CityDefinition { Name = "Boston", RegionCode = "NE", Probability = 40d });
        map.Cities.Add(new CityDefinition { Name = "Albany", RegionCode = "NE", Probability = 60d });
        map.Cities.Add(new CityDefinition { Name = "Miami", RegionCode = "SE", Probability = 100d });
        return map;
    }
}
