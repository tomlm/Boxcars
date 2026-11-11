using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Persistence;
using global::Boxcars.Services;

namespace Boxcars.Engine.Tests.Unit;

public class DestinationVisitsTests
{
    [Fact]
    public void Build_ShowsTheGamesDraws_AndComparesWithTheOdds()
    {
        var state = new GameState
        {
            Statistics = new GameStatisticsState { DestinationDraws = new() { ["Boston"] = 2, ["Miami"] = 1 } }
        };

        var visits = DestinationVisits.Build(state, CreateMap());

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
    public void Build_GameSavedWithoutStatistics_HasNoDraws()
    {
        var visits = DestinationVisits.Build(new GameState(), CreateMap());

        Assert.Equal(0, visits.TotalDraws);
        Assert.All(visits.Rows, row => Assert.Equal(0, row.DrawCount));
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
