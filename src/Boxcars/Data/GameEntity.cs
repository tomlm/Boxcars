using Azure;
using Azure.Data.Tables;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Boxcars.Data;

public class GameEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = "GAME";
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string GameId { get; set; } = string.Empty;
    public string CreatorId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset? GameDate { get; set; }
    public string State { get; set; } = PersistedGameStates.Lobby;
    public string MapFileName { get; set; } = "U21MAP.RB3";
    public int MaxPlayers { get; set; } = 6;
    public int CurrentPlayerCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public int? StartingCash { get; set; }
    public int? AnnouncingCash { get; set; }
    public int? WinningCash { get; set; }
    public int? RoverCash { get; set; }
    public int? PublicFee { get; set; }
    public int? PrivateFee { get; set; }
    public int? UnfriendlyFee1 { get; set; }
    public int? UnfriendlyFee2 { get; set; }
    public bool? HomeSwapping { get; set; }
    public bool? HomeCityChoice { get; set; }
    public bool? KeepCashSecret { get; set; }
    public string? StartEngine { get; set; }
    public int? SuperchiefPrice { get; set; }
    public int? ExpressPrice { get; set; }
    public int? SettingsSchemaVersion { get; set; }
    public string SeatsJson { get; set; } = "[]";
    public string CityProbabilityOverridesJson { get; set; } = "[]";
    public string RailroadPriceOverridesJson { get; set; } = "[]";

    [IgnoreDataMember]
    [JsonIgnore]
    public IReadOnlyList<GameSeatDefinition> Seats
    {
        get => GameSeatDefinitionSerialization.Deserialize(SeatsJson);
        set => SeatsJson = GameSeatDefinitionSerialization.Serialize(value);
    }

    [IgnoreDataMember]
    [JsonIgnore]
    public IReadOnlyList<CityProbabilityOverride> CityProbabilityOverrides
    {
        get => CityProbabilityOverrideSerialization.Deserialize(CityProbabilityOverridesJson);
        set => CityProbabilityOverridesJson = CityProbabilityOverrideSerialization.Serialize(value);
    }

    [IgnoreDataMember]
    [JsonIgnore]
    public IReadOnlyList<RailroadPriceOverride> RailroadPriceOverrides
    {
        get => RailroadPriceOverrideSerialization.Deserialize(RailroadPriceOverridesJson);
        set => RailroadPriceOverridesJson = RailroadPriceOverrideSerialization.Serialize(value);
    }
}
