using Azure;
using Azure.Data.Tables;

namespace Boxcars.Data;

public class GameEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = "GAME";
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string GameId { get; set; } = string.Empty;
    public string CreatorId { get; set; } = string.Empty;
    public string MapFileName { get; set; } = "U21MAP.RB3";
    public int MaxPlayers { get; set; } = 6;
    public int CurrentPlayerCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string SettingsJson { get; set; } = string.Empty;
    public string PlayersJson { get; set; } = "[]";
}
