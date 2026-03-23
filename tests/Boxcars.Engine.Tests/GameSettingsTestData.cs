using Boxcars.Data;
using Boxcars.Engine.Domain;
using Boxcars.Engine.Persistence;

namespace Boxcars.Engine.Tests;

public static class GameSettingsTestData
{
    public static GameSettings Create(
        int? startingCash = null,
        int? announcingCash = null,
        int? winningCash = null,
        int? roverCash = null,
        int? publicFee = null,
        int? privateFee = null,
        int? unfriendlyFee1 = null,
        int? unfriendlyFee2 = null,
        bool? homeSwapping = null,
        bool? homeCityChoice = null,
        bool? keepCashSecret = null,
        LocomotiveType? startEngine = null,
        int? superchiefPrice = null,
        int? expressPrice = null,
        int? schemaVersion = null)
    {
        var defaults = GameSettings.Default;

        return defaults with
        {
            StartingCash = startingCash ?? defaults.StartingCash,
            AnnouncingCash = announcingCash ?? defaults.AnnouncingCash,
            WinningCash = winningCash ?? defaults.WinningCash,
            RoverCash = roverCash ?? defaults.RoverCash,
            PublicFee = publicFee ?? defaults.PublicFee,
            PrivateFee = privateFee ?? defaults.PrivateFee,
            UnfriendlyFee1 = unfriendlyFee1 ?? defaults.UnfriendlyFee1,
            UnfriendlyFee2 = unfriendlyFee2 ?? defaults.UnfriendlyFee2,
            HomeSwapping = homeSwapping ?? defaults.HomeSwapping,
            HomeCityChoice = homeCityChoice ?? defaults.HomeCityChoice,
            KeepCashSecret = keepCashSecret ?? defaults.KeepCashSecret,
            StartEngine = startEngine ?? defaults.StartEngine,
            SuperchiefPrice = superchiefPrice ?? defaults.SuperchiefPrice,
            ExpressPrice = expressPrice ?? defaults.ExpressPrice,
            SchemaVersion = schemaVersion ?? defaults.SchemaVersion
        };
    }

    public static GameEntity CreatePersistedGameEntity(string gameId = "game-1", GameSettings? settings = null)
    {
        var resolvedSettings = settings ?? GameSettings.Default;

        return new GameEntity
        {
            PartitionKey = gameId,
            RowKey = "GAME",
            GameId = gameId,
            CreatorId = "creator-1",
            MapFileName = "U21MAP.RB3",
            MaxPlayers = 2,
            CurrentPlayerCount = 2,
            CreatedAt = DateTimeOffset.UtcNow,
            StartingCash = resolvedSettings.StartingCash,
            AnnouncingCash = resolvedSettings.AnnouncingCash,
            WinningCash = resolvedSettings.WinningCash,
            RoverCash = resolvedSettings.RoverCash,
            PublicFee = resolvedSettings.PublicFee,
            PrivateFee = resolvedSettings.PrivateFee,
            UnfriendlyFee1 = resolvedSettings.UnfriendlyFee1,
            UnfriendlyFee2 = resolvedSettings.UnfriendlyFee2,
            HomeSwapping = resolvedSettings.HomeSwapping,
            HomeCityChoice = resolvedSettings.HomeCityChoice,
            KeepCashSecret = resolvedSettings.KeepCashSecret,
            StartEngine = resolvedSettings.StartEngine.ToString(),
            SuperchiefPrice = resolvedSettings.SuperchiefPrice,
            ExpressPrice = resolvedSettings.ExpressPrice,
            SettingsSchemaVersion = resolvedSettings.SchemaVersion
        };
    }
}