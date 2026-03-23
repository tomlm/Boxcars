using Boxcars.Data;
using Boxcars.Engine.Domain;
using Boxcars.Engine.Persistence;

namespace Boxcars.Services;

public sealed class GameSettingsResolver
{
    public GameSettings Normalize(GameSettings? candidate)
    {
        var defaults = GameSettings.Default;
        var settings = candidate ?? defaults;

        if (settings.StartingCash <= 0)
        {
            throw new InvalidOperationException("Starting cash must be greater than zero.");
        }

        if (settings.AnnouncingCash <= 0)
        {
            throw new InvalidOperationException("Announcing cash must be greater than zero.");
        }

        if (settings.WinningCash <= 0)
        {
            throw new InvalidOperationException("Winning cash must be greater than zero.");
        }

        if (settings.WinningCash < settings.AnnouncingCash)
        {
            throw new InvalidOperationException("Winning cash must be greater than or equal to announcing cash.");
        }

        if (settings.RoverCash <= 0)
        {
            throw new InvalidOperationException("Rover cash must be greater than zero.");
        }

        if (settings.PublicFee <= 0 || settings.PrivateFee <= 0 || settings.UnfriendlyFee1 <= 0 || settings.UnfriendlyFee2 <= 0)
        {
            throw new InvalidOperationException("All fee settings must be greater than zero.");
        }

        if (settings.SuperchiefPrice <= 0 || settings.ExpressPrice <= 0)
        {
            throw new InvalidOperationException("Engine upgrade prices must be greater than zero.");
        }

        if (!Enum.IsDefined(settings.StartEngine))
        {
            throw new InvalidOperationException("Start engine must be Freight, Express, or Superchief.");
        }

        return settings with
        {
            SchemaVersion = settings.SchemaVersion > 0 ? settings.SchemaVersion : defaults.SchemaVersion
        };
    }

    public ResolvedGameSettings Resolve(GameEntity gameEntity)
    {
        ArgumentNullException.ThrowIfNull(gameEntity);

        var defaults = GameSettings.Default;
        var warnings = new List<string>();
        var missingValueCount = 0;

        var startEngine = defaults.StartEngine;
        if (string.IsNullOrWhiteSpace(gameEntity.StartEngine))
        {
            missingValueCount++;
        }
        else if (!Enum.TryParse<LocomotiveType>(gameEntity.StartEngine, ignoreCase: true, out startEngine))
        {
            warnings.Add($"Unknown persisted start engine '{gameEntity.StartEngine}'. Using default.");
            startEngine = defaults.StartEngine;
        }

        int ResolveInt(int? value, int defaultValue, string name)
        {
            if (!value.HasValue)
            {
                missingValueCount++;
                return defaultValue;
            }

            if (value.Value <= 0)
            {
                warnings.Add($"Persisted setting '{name}' had invalid value '{value.Value}'. Using default.");
                return defaultValue;
            }

            return value.Value;
        }

        bool ResolveBool(bool? value, bool defaultValue)
        {
            if (!value.HasValue)
            {
                missingValueCount++;
                return defaultValue;
            }

            return value.Value;
        }

        var resolvedSettings = Normalize(new GameSettings
        {
            StartingCash = ResolveInt(gameEntity.StartingCash, defaults.StartingCash, nameof(GameEntity.StartingCash)),
            AnnouncingCash = ResolveInt(gameEntity.AnnouncingCash, defaults.AnnouncingCash, nameof(GameEntity.AnnouncingCash)),
            WinningCash = ResolveInt(gameEntity.WinningCash, defaults.WinningCash, nameof(GameEntity.WinningCash)),
            RoverCash = ResolveInt(gameEntity.RoverCash, defaults.RoverCash, nameof(GameEntity.RoverCash)),
            PublicFee = ResolveInt(gameEntity.PublicFee, defaults.PublicFee, nameof(GameEntity.PublicFee)),
            PrivateFee = ResolveInt(gameEntity.PrivateFee, defaults.PrivateFee, nameof(GameEntity.PrivateFee)),
            UnfriendlyFee1 = ResolveInt(gameEntity.UnfriendlyFee1, defaults.UnfriendlyFee1, nameof(GameEntity.UnfriendlyFee1)),
            UnfriendlyFee2 = ResolveInt(gameEntity.UnfriendlyFee2, defaults.UnfriendlyFee2, nameof(GameEntity.UnfriendlyFee2)),
            HomeSwapping = ResolveBool(gameEntity.HomeSwapping, defaults.HomeSwapping),
            HomeCityChoice = ResolveBool(gameEntity.HomeCityChoice, defaults.HomeCityChoice),
            KeepCashSecret = ResolveBool(gameEntity.KeepCashSecret, defaults.KeepCashSecret),
            StartEngine = startEngine,
            SuperchiefPrice = ResolveInt(gameEntity.SuperchiefPrice, defaults.SuperchiefPrice, nameof(GameEntity.SuperchiefPrice)),
            ExpressPrice = ResolveInt(gameEntity.ExpressPrice, defaults.ExpressPrice, nameof(GameEntity.ExpressPrice)),
            SchemaVersion = gameEntity.SettingsSchemaVersion.GetValueOrDefault(defaults.SchemaVersion)
        });

        var totalSettingCount = 14;
        var source = missingValueCount switch
        {
            <= 0 => "Persisted",
            var count when count >= totalSettingCount => "LegacyDefaulted",
            _ => "PartiallyDefaulted"
        };

        return new ResolvedGameSettings(resolvedSettings, source, warnings);
    }

    public void Apply(GameEntity gameEntity, GameSettings settings)
    {
        ArgumentNullException.ThrowIfNull(gameEntity);

        var normalized = Normalize(settings);

        gameEntity.StartingCash = normalized.StartingCash;
        gameEntity.AnnouncingCash = normalized.AnnouncingCash;
        gameEntity.WinningCash = normalized.WinningCash;
        gameEntity.RoverCash = normalized.RoverCash;
        gameEntity.PublicFee = normalized.PublicFee;
        gameEntity.PrivateFee = normalized.PrivateFee;
        gameEntity.UnfriendlyFee1 = normalized.UnfriendlyFee1;
        gameEntity.UnfriendlyFee2 = normalized.UnfriendlyFee2;
        gameEntity.HomeSwapping = normalized.HomeSwapping;
        gameEntity.HomeCityChoice = normalized.HomeCityChoice;
        gameEntity.KeepCashSecret = normalized.KeepCashSecret;
        gameEntity.StartEngine = normalized.StartEngine.ToString();
        gameEntity.SuperchiefPrice = normalized.SuperchiefPrice;
        gameEntity.ExpressPrice = normalized.ExpressPrice;
        gameEntity.SettingsSchemaVersion = normalized.SchemaVersion;
    }
}