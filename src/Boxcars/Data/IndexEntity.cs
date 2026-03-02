using Azure;
using Azure.Data.Tables;

namespace Boxcars.Data;

public class IndexEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string UserId { get; set; } = string.Empty;
    public string? GameId { get; set; }
}
