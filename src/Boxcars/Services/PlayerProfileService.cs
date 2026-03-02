using Azure;
using Azure.Data.Tables;
using Boxcars.Data;
using Boxcars.Identity;

namespace Boxcars.Services;

public class PlayerProfileService
{
    private readonly TableClient _usersTable;
    private readonly TableClient _nicknameIndexTable;

    public PlayerProfileService(TableServiceClient tableServiceClient)
    {
        _usersTable = tableServiceClient.GetTableClient(TableNames.UsersTable);
        _nicknameIndexTable = tableServiceClient.GetTableClient(TableNames.NicknameIndexTable);
    }

    public async Task<ApplicationUser?> GetProfileAsync(string userId, CancellationToken cancellationToken)
    {
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

    public async Task<NicknameResult> UpdateNicknameAsync(string userId, string newNickname, CancellationToken cancellationToken)
    {
        var normalizedNickname = newNickname.ToUpperInvariant();

        // Load current user
        ApplicationUser user;
        try
        {
            var response = await _usersTable.GetEntityAsync<ApplicationUser>("USER", userId, cancellationToken: cancellationToken);
            user = response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return NicknameResult.UserNotFound;
        }

        var oldNormalizedNickname = user.NormalizedNickname;

        // Try to claim new nickname index
        try
        {
            await _nicknameIndexTable.AddEntityAsync(new IndexEntity
            {
                PartitionKey = "NICKNAME_INDEX",
                RowKey = normalizedNickname,
                UserId = userId
            }, cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            return NicknameResult.Conflict;
        }

        // Update user entity
        user.Nickname = newNickname;
        user.NormalizedNickname = normalizedNickname;

        try
        {
            await _usersTable.UpdateEntityAsync(user, user.ETag, TableUpdateMode.Replace, cancellationToken);
        }
        catch (RequestFailedException)
        {
            // Rollback: delete new nickname index
            try { await _nicknameIndexTable.DeleteEntityAsync("NICKNAME_INDEX", normalizedNickname, cancellationToken: cancellationToken); }
            catch (RequestFailedException) { /* best-effort rollback */ }

            return NicknameResult.UpdateFailed;
        }

        // Delete old nickname index entry
        if (!string.IsNullOrEmpty(oldNormalizedNickname))
        {
            try { await _nicknameIndexTable.DeleteEntityAsync("NICKNAME_INDEX", oldNormalizedNickname, cancellationToken: cancellationToken); }
            catch (RequestFailedException) { /* best-effort cleanup */ }
        }

        return NicknameResult.Success;
    }

    public async Task<bool> UpdateThumbnailUrlAsync(string userId, string newUrl, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _usersTable.GetEntityAsync<ApplicationUser>("USER", userId, cancellationToken: cancellationToken);
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
}

public enum NicknameResult
{
    Success,
    Conflict,
    UserNotFound,
    UpdateFailed
}
