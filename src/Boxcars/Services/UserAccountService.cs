using Azure;
using Boxcars.Data;

namespace Boxcars.Services;

/// <summary>
/// The admin's view of every account, people and bots alike: changing a name, color or strategy, and deleting.
/// A new name or color reaches the account's unfinished games the same way saving it in Settings does.
/// </summary>
public sealed class UserAccountService(UserDirectoryService userDirectory, GameService gameService, GamePresenceService gamePresenceService)
{
    /// <summary>Every account: people first, then bots, each by name.</summary>
    public async Task<IReadOnlyList<ApplicationUser>> ListAccountsAsync(CancellationToken cancellationToken)
    {
        var users = await userDirectory.ListUsersAsync(cancellationToken);
        return users
            .OrderBy(user => user.IsBot)
            .ThenBy(DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The name an account goes by.</summary>
    public static string DisplayName(ApplicationUser user)
    {
        return !string.IsNullOrWhiteSpace(user.Nickname) ? user.Nickname
            : !string.IsNullOrWhiteSpace(user.Name) ? user.Name
            : user.RowKey;
    }

    /// <summary>How the account signs in: Bot, Google, Microsoft or Email.</summary>
    public static string SignInKind(ApplicationUser user)
    {
        return user.IsBot ? "Bot"
            : string.IsNullOrWhiteSpace(user.ExternalLoginProvider) ? "Unknown"
            : user.ExternalLoginProvider;
    }

    /// <summary>
    /// Saves an account's name, color and strategy. The name must be unique; a bot's name is also its bot name.
    /// <paramref name="eTag"/> is the version that was edited, so someone else's change in between isn't overwritten.
    /// </summary>
    public async Task<UserAccountResult> SaveAsync(
        string actingUserId,
        string userId,
        ETag eTag,
        string? name,
        string? color,
        string? strategyText,
        CancellationToken cancellationToken)
    {
        var user = await userDirectory.GetUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return UserAccountResult.Failed("That account no longer exists.");
        }

        var nickname = name?.Trim() ?? string.Empty;
        if (nickname.Length == 0)
        {
            return UserAccountResult.Failed("Name is required.");
        }

        var normalizedNickname = nickname.ToUpperInvariant();
        var namesake = await userDirectory.FindByNormalizedNicknameAsync(normalizedNickname, cancellationToken);
        if (namesake is not null && !string.Equals(namesake.RowKey, user.RowKey, StringComparison.OrdinalIgnoreCase))
        {
            return UserAccountResult.Failed($"{DisplayName(namesake)} already uses that name.");
        }

        var preferredColor = PlayerColorOptions.IsSupported(color) ? PlayerColorOptions.NormalizeOrDefault(color) : string.Empty;
        var nameChanged = !string.Equals(user.Nickname, nickname, StringComparison.Ordinal);
        var colorChanged = !string.Equals(user.PreferredColor, preferredColor, StringComparison.OrdinalIgnoreCase);

        user.Nickname = nickname;
        user.NormalizedNickname = normalizedNickname;
        if (user.IsBot)
        {
            user.Name = nickname;
        }

        user.PreferredColor = preferredColor;
        user.StrategyText = strategyText?.Trim() ?? string.Empty;
        user.ModifiedByUserId = actingUserId;
        user.ModifiedUtc = DateTimeOffset.UtcNow;

        try
        {
            await userDirectory.UpdateUserAsync(user, eTag, cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status == 412)
        {
            return UserAccountResult.Failed("That account changed since you opened it. Refresh and try again.");
        }

        if (nameChanged || (colorChanged && preferredColor.Length > 0))
        {
            var changedGameIds = await gameService.ApplyProfileToSeatsAsync(
                user.RowKey,
                nameChanged ? nickname : null,
                colorChanged && preferredColor.Length > 0 ? preferredColor : null,
                cancellationToken);
            foreach (var gameId in changedGameIds)
            {
                gamePresenceService.NotifyGameMetadataChanged(gameId);
            }
        }

        return UserAccountResult.Succeeded;
    }

    /// <summary>
    /// Deletes an account, unless it's the admin's own or it has a seat in a game that isn't finished. Finished games
    /// keep the name and color they were played with.
    /// </summary>
    public async Task<UserAccountResult> DeleteAsync(string actingUserId, string userId, ETag eTag, CancellationToken cancellationToken)
    {
        if (string.Equals(actingUserId, userId, StringComparison.OrdinalIgnoreCase))
        {
            return UserAccountResult.Failed("You can't delete your own account.");
        }

        var user = await userDirectory.GetUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return UserAccountResult.Failed("That account no longer exists.");
        }

        var unfinishedGames = await gameService.ListUnfinishedGamesForPlayerAsync(user.RowKey, cancellationToken);
        if (unfinishedGames.Count > 0)
        {
            var gameNames = string.Join(", ", unfinishedGames.Select(game => string.IsNullOrWhiteSpace(game.Name) ? game.GameId : game.Name));
            return UserAccountResult.Failed($"{DisplayName(user)} is still playing in {gameNames}. Finish or end those games first.");
        }

        try
        {
            await userDirectory.DeleteUserAsync(user.RowKey, eTag, cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status == 412)
        {
            return UserAccountResult.Failed("That account changed since you opened it. Refresh and try again.");
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return UserAccountResult.Failed("That account no longer exists.");
        }

        return UserAccountResult.Succeeded;
    }
}

public sealed record UserAccountResult(bool Success, string? Error)
{
    public static UserAccountResult Succeeded { get; } = new(true, null);

    public static UserAccountResult Failed(string error) => new(false, error);
}
