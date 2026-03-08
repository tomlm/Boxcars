using Azure;
using Azure.Data.Tables;
using Boxcars.Data;
using Boxcars.Identity;

namespace Boxcars.Services;

public class PlayerProfileService
{
    private readonly TableClient _usersTable;

    public PlayerProfileService(TableServiceClient tableServiceClient)
    {
        _usersTable = tableServiceClient.GetTableClient(TableNames.UsersTable);
    }

    public async Task<ApplicationUser?> GetProfileAsync(string userId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _usersTable.GetEntityAsync<ApplicationUser>("USER", userId.ToLowerInvariant(), cancellationToken: cancellationToken);
            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<List<ApplicationUser>> GetSelectableProfilesAsync(CancellationToken cancellationToken)
    {
        var profiles = new List<ApplicationUser>();
        var query = _usersTable.QueryAsync<ApplicationUser>(
            user => user.PartitionKey == "USER",
            cancellationToken: cancellationToken);

        await foreach (var user in query)
        {
            if (!string.IsNullOrWhiteSpace(user.Nickname))
            {
                profiles.Add(user);
            }
        }

        return profiles
            .OrderBy(profile => profile.Nickname, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<NicknameResult> UpdateNicknameAsync(string userId, string newNickname, CancellationToken cancellationToken)
    {
        var normalizedNickname = newNickname.Trim().ToUpperInvariant();

        // Load current user
        ApplicationUser user;
        try
        {
            var response = await _usersTable.GetEntityAsync<ApplicationUser>("USER", userId.ToLowerInvariant(), cancellationToken: cancellationToken);
            user = response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return NicknameResult.UserNotFound;
        }

        await foreach (var existing in _usersTable.QueryAsync<ApplicationUser>(
                           entity => entity.PartitionKey == "USER" && entity.NormalizedNickname == normalizedNickname,
                           cancellationToken: cancellationToken))
        {
            if (!string.Equals(existing.RowKey, user.RowKey, StringComparison.OrdinalIgnoreCase))
            {
                return NicknameResult.Conflict;
            }
        }

        // Update user entity
        user.Nickname = newNickname.Trim();
        user.NormalizedNickname = normalizedNickname;

        try
        {
            await _usersTable.UpdateEntityAsync(user, user.ETag, TableUpdateMode.Replace, cancellationToken);
        }
        catch (RequestFailedException)
        {
            return NicknameResult.UpdateFailed;
        }

        return NicknameResult.Success;
    }

    public async Task<bool> UpdateThumbnailUrlAsync(string userId, string newUrl, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _usersTable.GetEntityAsync<ApplicationUser>("USER", userId.ToLowerInvariant(), cancellationToken: cancellationToken);
            var user = response.Value;
            user.ThumbnailUrl = newUrl;
            await _usersTable.UpdateEntityAsync(user, user.ETag, TableUpdateMode.Replace, cancellationToken);
            return true;
        }
        catch (RequestFailedException)
        {
            return false;
        }
    }

    public async Task<bool> UpdatePreferredColorAsync(string userId, string preferredColor, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _usersTable.GetEntityAsync<ApplicationUser>("USER", userId, cancellationToken: cancellationToken);
            var user = response.Value;
            user.PreferredColor = preferredColor;
            await _usersTable.UpdateEntityAsync(user, user.ETag, TableUpdateMode.Replace, cancellationToken);
            return true;
        }
        catch (RequestFailedException)
        {
            return false;
        }
    }
}

public enum NicknameResult
{
    Success,
    Conflict,
    UserNotFound,
    UpdateFailed
}
