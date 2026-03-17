using Azure;
using Azure.Data.Tables;

namespace Boxcars.Data;

public sealed class BotStrategyDefinitionEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "BOT";
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string BotDefinitionId
    {
        get => RowKey;
        set => RowKey = value;
    }

    public string Name { get; set; } = string.Empty;
    public string StrategyText { get; set; } = string.Empty;
    public bool IsBotUser { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
    public string ModifiedByUserId { get; set; } = string.Empty;
    public DateTimeOffset ModifiedUtc { get; set; }
}