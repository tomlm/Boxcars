using Azure;
using Azure.Data.Tables;

namespace Boxcars.Data;

public class GamePlayerEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public DateTime JoinedAt { get; set; }
}
