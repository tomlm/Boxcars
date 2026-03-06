using Azure;
using Azure.Data.Tables;

namespace Boxcars.Data;

public sealed class GameEventEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string GameId { get; set; } = string.Empty;
    public string EventKind { get; set; } = string.Empty;
    public string EventData { get; set; } = "{}";
    public string SerializedGameState { get; set; } = string.Empty;
    public DateTimeOffset OccurredUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ChangeSummary { get; set; } = string.Empty;
}
