using Azure;
using Azure.Data.Tables;

namespace Boxcars.Data;

public class ApplicationUser : ITableEntity
{
    public string PartitionKey { get; set; } = "USER";
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    // Computed property for Identity compatibility
    public string Id => RowKey;

    // Identity properties
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string NormalizedUserName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string SecurityStamp { get; set; } = string.Empty;
    public bool EmailConfirmed { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public bool LockoutEnabled { get; set; }
    public int AccessFailedCount { get; set; }

    // Profile properties
    public string Nickname { get; set; } = string.Empty;
    public string NormalizedNickname { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;

    // Scaffold-compatibility properties (not actively used but required by some Identity pages)
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString();
    public string? PhoneNumber { get; set; }
    public bool PhoneNumberConfirmed { get; set; }
    public bool TwoFactorEnabled { get; set; }
}

