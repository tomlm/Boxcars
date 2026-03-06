using Azure;
using Azure.Data.Tables;

namespace Boxcars.Data;

public sealed class GameSnapshotEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string ActionType { get; set; } = string.Empty;
    public string ChangeSummary { get; set; } = string.Empty;
    public string SnapshotJson { get; set; } = string.Empty;
    public DateTime AppliedAtUtc { get; set; }
}
