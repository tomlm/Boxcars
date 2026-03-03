using Azure;
using Azure.Data.Tables;

namespace Boxcars.Data;

public class GameEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "ACTIVE";
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string CreatorId { get; set; } = string.Empty;
    public string MapFileName { get; set; } = "U21MAP.RB3";
    public int MaxPlayers { get; set; } = 6;
    public int CurrentPlayerCount { get; set; }
    public DateTime CreatedAt { get; set; }
}
