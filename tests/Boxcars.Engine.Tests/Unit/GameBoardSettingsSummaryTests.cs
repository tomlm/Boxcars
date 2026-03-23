using Boxcars.Data;
using Boxcars.Engine.Domain;
using Boxcars.Engine.Tests;
using Boxcars.Services;
using Boxcars.Services.Maps;

namespace Boxcars.Engine.Tests.Unit;

public class GameBoardSettingsSummaryTests
{
    [Fact]
    public void BuildGameSettingsSummary_CustomPersistedSettings_ProjectsReadOnlySections()
    {
        var mapper = CreateMapper();
        var gameEntity = GameSettingsTestData.CreatePersistedGameEntity(settings: GameSettingsTestData.Create(
            startingCash: 35_000,
            announcingCash: 150_000,
            winningCash: 325_000,
            roverCash: 60_000,
            publicFee: 1_500,
            privateFee: 500,
            unfriendlyFee1: 6_000,
            unfriendlyFee2: 12_000,
            homeSwapping: false,
            homeCityChoice: false,
            keepCashSecret: false,
            startEngine: LocomotiveType.Express,
            superchiefPrice: 45_000,
            expressPrice: 6_000));

        var summary = mapper.BuildGameSettingsSummary(gameEntity);

        Assert.True(summary.HasContent);
        Assert.False(summary.UsesDefaultFallback);
        Assert.Equal("Saved rules for this match are read-only once play begins.", summary.SourceDescription);
        Assert.Equal("$35,000", GetValue(summary, "Cash Rules", "Starting Cash"));
        Assert.Equal("Always visible", GetValue(summary, "Cash Rules", "Cash Visibility"));
        Assert.Equal("$1,500", GetValue(summary, "Fees", "Public Railroad Fee"));
        Assert.Equal("Disabled", GetValue(summary, "Home Setup", "Home Swapping"));
        Assert.Equal("Random city assigned", GetValue(summary, "Home Setup", "Home City Choice"));
        Assert.Equal("Express", GetValue(summary, "Engines", "Starting Engine"));
        Assert.Equal("$45,000", GetValue(summary, "Engines", "Superchief Price"));
    }

    [Fact]
    public void BuildGameSettingsSummary_LegacyGame_ProjectsDefaultsWithLegacyNotice()
    {
        var mapper = CreateMapper();
        var legacyGameEntity = new GameEntity
        {
            PartitionKey = "game-legacy",
            RowKey = "GAME",
            GameId = "game-legacy",
            CreatorId = "creator@example.com",
            MapFileName = "U21MAP.RB3",
            MaxPlayers = 2,
            CurrentPlayerCount = 2,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var summary = mapper.BuildGameSettingsSummary(legacyGameEntity);

        Assert.True(summary.HasContent);
        Assert.True(summary.UsesDefaultFallback);
        Assert.Equal("Legacy game using documented default settings.", summary.SourceDescription);
        Assert.Equal("$20,000", GetValue(summary, "Cash Rules", "Starting Cash"));
        Assert.Equal("Hidden below announcing threshold", GetValue(summary, "Cash Rules", "Cash Visibility"));
        Assert.Equal("Player chooses city", GetValue(summary, "Home Setup", "Home City Choice"));
        Assert.Equal("Freight", GetValue(summary, "Engines", "Starting Engine"));
    }

    private static GameBoardStateMapper CreateMapper()
    {
        return new GameBoardStateMapper(
            new NetworkCoverageService(),
            new MapAnalysisService(new MapRouteService()),
            new PurchaseRecommendationService(),
            new GameSettingsResolver());
    }

    private static string GetValue(GameSettingsSummaryModel summary, string sectionTitle, string itemLabel)
    {
        var section = Assert.Single(summary.Sections.Where(section => section.Title == sectionTitle));
        var item = Assert.Single(section.Items.Where(item => item.Label == itemLabel));
        return item.Value;
    }
}