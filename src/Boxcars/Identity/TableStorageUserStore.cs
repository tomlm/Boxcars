using Azure;
using Azure.Data.Tables;
using Boxcars.Data;
using Boxcars.Identity;
using Microsoft.AspNetCore.Identity;

namespace Boxcars.Identity;

public class TableStorageUserStore :
    IUserStore<ApplicationUser>,
    IUserEmailStore<ApplicationUser>,
    IUserPasswordStore<ApplicationUser>,
    IUserSecurityStampStore<ApplicationUser>,
    IUserLockoutStore<ApplicationUser>
{
    private readonly TableClient _usersTable;
    private readonly TableClient _emailIndexTable;
    private readonly TableClient _userNameIndexTable;
    private readonly TableClient _nicknameIndexTable;

    private const string DefaultThumbnailUrl = "https://via.placeholder.com/150?text=Player";

    public TableStorageUserStore(TableServiceClient tableServiceClient)
    {
        _usersTable = tableServiceClient.GetTableClient(TableNames.UsersTable);
        _emailIndexTable = tableServiceClient.GetTableClient(TableNames.UserEmailIndexTable);
        _userNameIndexTable = tableServiceClient.GetTableClient(TableNames.UserNameIndexTable);
        _nicknameIndexTable = tableServiceClient.GetTableClient(TableNames.NicknameIndexTable);
    }

    // IUserStore<ApplicationUser>

    public async Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        user.RowKey = Guid.NewGuid().ToString();
        user.PartitionKey = "USER";
        user.SecurityStamp = Guid.NewGuid().ToString();

        // Derive nickname from email prefix
        var emailPrefix = user.Email.Split('@')[0];
        var normalizedNickname = emailPrefix.ToUpperInvariant();

        // Try to claim the nickname; if taken, leave blank
        try
        {
            await _nicknameIndexTable.AddEntityAsync(new IndexEntity
            {
                PartitionKey = "NICKNAME_INDEX",
                RowKey = normalizedNickname,
                UserId = user.RowKey
            }, cancellationToken);

            user.Nickname = emailPrefix;
            user.NormalizedNickname = normalizedNickname;
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            // Nickname collision — leave blank, user must set manually
            user.Nickname = string.Empty;
            user.NormalizedNickname = string.Empty;
        }

        // Set default thumbnail
        if (string.IsNullOrEmpty(user.ThumbnailUrl))
        {
            user.ThumbnailUrl = DefaultThumbnailUrl;
        }

        // Insert user entity
        await _usersTable.AddEntityAsync(user, cancellationToken);

        // Insert email index
        await _emailIndexTable.AddEntityAsync(new IndexEntity
        {
            PartitionKey = "EMAIL_INDEX",
            RowKey = user.NormalizedEmail,
            UserId = user.RowKey
        }, cancellationToken);

        // Insert username index
        await _userNameIndexTable.AddEntityAsync(new IndexEntity
        {
            PartitionKey = "USERNAME_INDEX",
            RowKey = user.NormalizedUserName,
            UserId = user.RowKey
        }, cancellationToken);

        return IdentityResult.Success;
    }

    public async Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await _usersTable.UpdateEntityAsync(user, user.ETag, TableUpdateMode.Replace, cancellationToken);
            return IdentityResult.Success;
        }
        catch (RequestFailedException ex) when (ex.Status == 412)
        {
            return IdentityResult.Failed(new IdentityError
            {
                Code = "ConcurrencyFailure",
                Description = "The user record was modified by another process."
            });
        }
    }

    public async Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Delete index entries
        if (!string.IsNullOrEmpty(user.NormalizedEmail))
        {
            try { await _emailIndexTable.DeleteEntityAsync("EMAIL_INDEX", user.NormalizedEmail, cancellationToken: cancellationToken); }
            catch (RequestFailedException) { /* best-effort cleanup */ }
        }

        if (!string.IsNullOrEmpty(user.NormalizedUserName))
        {
            try { await _userNameIndexTable.DeleteEntityAsync("USERNAME_INDEX", user.NormalizedUserName, cancellationToken: cancellationToken); }
            catch (RequestFailedException) { /* best-effort cleanup */ }
        }

        if (!string.IsNullOrEmpty(user.NormalizedNickname))
        {
            try { await _nicknameIndexTable.DeleteEntityAsync("NICKNAME_INDEX", user.NormalizedNickname, cancellationToken: cancellationToken); }
            catch (RequestFailedException) { /* best-effort cleanup */ }
        }

        await _usersTable.DeleteEntityAsync(user.PartitionKey, user.RowKey, cancellationToken: cancellationToken);

        return IdentityResult.Success;
    }

    public async Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var response = await _usersTable.GetEntityAsync<ApplicationUser>("USER", userId, cancellationToken: cancellationToken);
            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var indexResponse = await _userNameIndexTable.GetEntityAsync<IndexEntity>("USERNAME_INDEX", normalizedUserName, cancellationToken: cancellationToken);
            return await FindByIdAsync(indexResponse.Value.UserId, cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.RowKey);

    public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(user.UserName);

    public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken)
    {
        user.UserName = userName ?? string.Empty;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(user.NormalizedUserName);

    public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken cancellationToken)
    {
        user.NormalizedUserName = normalizedName ?? string.Empty;
        return Task.CompletedTask;
    }

    // IUserEmailStore<ApplicationUser>

    public Task SetEmailAsync(ApplicationUser user, string? email, CancellationToken cancellationToken)
    {
        user.Email = email ?? string.Empty;
        return Task.CompletedTask;
    }

    public Task<string?> GetEmailAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(user.Email);

    public Task<bool> GetEmailConfirmedAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.EmailConfirmed);

    public Task SetEmailConfirmedAsync(ApplicationUser user, bool confirmed, CancellationToken cancellationToken)
    {
        user.EmailConfirmed = confirmed;
        return Task.CompletedTask;
    }

    public async Task<ApplicationUser?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var indexResponse = await _emailIndexTable.GetEntityAsync<IndexEntity>("EMAIL_INDEX", normalizedEmail, cancellationToken: cancellationToken);
            return await FindByIdAsync(indexResponse.Value.UserId, cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public Task<string?> GetNormalizedEmailAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(user.NormalizedEmail);

    public Task SetNormalizedEmailAsync(ApplicationUser user, string? normalizedEmail, CancellationToken cancellationToken)
    {
        user.NormalizedEmail = normalizedEmail ?? string.Empty;
        return Task.CompletedTask;
    }

    // IUserPasswordStore<ApplicationUser>

    public Task SetPasswordHashAsync(ApplicationUser user, string? passwordHash, CancellationToken cancellationToken)
    {
        user.PasswordHash = passwordHash ?? string.Empty;
        return Task.CompletedTask;
    }

    public Task<string?> GetPasswordHashAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(user.PasswordHash);

    public Task<bool> HasPasswordAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult(!string.IsNullOrEmpty(user.PasswordHash));

    // IUserSecurityStampStore<ApplicationUser>

    public Task SetSecurityStampAsync(ApplicationUser user, string stamp, CancellationToken cancellationToken)
    {
        user.SecurityStamp = stamp;
        return Task.CompletedTask;
    }

    public Task<string?> GetSecurityStampAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(user.SecurityStamp);

    // IUserLockoutStore<ApplicationUser>

    public Task<DateTimeOffset?> GetLockoutEndDateAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.LockoutEnd);

    public Task SetLockoutEndDateAsync(ApplicationUser user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken)
    {
        user.LockoutEnd = lockoutEnd;
        return Task.CompletedTask;
    }

    public Task<int> IncrementAccessFailedCountAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        user.AccessFailedCount++;
        return Task.FromResult(user.AccessFailedCount);
    }

    public Task ResetAccessFailedCountAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        user.AccessFailedCount = 0;
        return Task.CompletedTask;
    }

    public Task<int> GetAccessFailedCountAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.AccessFailedCount);

    public Task<bool> GetLockoutEnabledAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.LockoutEnabled);

    public Task SetLockoutEnabledAsync(ApplicationUser user, bool enabled, CancellationToken cancellationToken)
    {
        user.LockoutEnabled = enabled;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
