using Azure;
using Azure.Data.Tables;

namespace Boxcars.Data;

public class ApplicationUser : ITableEntity
{
    public string PartitionKey { get; set; } = "USER";
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    // Computed alias used by services that historically referred to "Id"
    public string Id => RowKey;

    // Identity / profile properties
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string NormalizedUserName { get; set; } = string.Empty;

    // Profile properties
    public string Nickname { get; set; } = string.Empty;
    public string NormalizedNickname { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public string PreferredColor { get; set; } = string.Empty;
    public string StrategyText { get; set; } = string.Empty;
    public bool IsBot { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
    public string ModifiedByUserId { get; set; } = string.Empty;
    public DateTimeOffset ModifiedUtc { get; set; }

    // External login provenance (set on first sign-in)
    public string ExternalLoginProvider { get; set; } = string.Empty;
    public string ExternalLoginKey { get; set; } = string.Empty;
}
