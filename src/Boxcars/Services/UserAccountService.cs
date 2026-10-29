using Azure;
using Boxcars.Auth;
using Boxcars.Data;

namespace Boxcars.Services;

/// <summary>
/// The admin's view of every account, people and bots alike: adding, changing a name, color, picture or strategy,
/// and deleting.
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

    /// <summary>The kind of account; older accounts with no provider recorded are Auth.</summary>
    public static AccountType GetAccountType(ApplicationUser user)
    {
        return user.IsBot ? AccountType.Bot
            : EmailLoginService.IsEmailOnly(user) ? AccountType.Email
            : AccountType.Auth;
    }

    /// <summary>
    /// Adds an account. A bot gets a made-up id; a person is keyed by their email (see <see cref="AccountType"/>).
    /// </summary>
    public async Task<UserAccountResult> CreateAsync(
        string actingUserId,
        AccountType accountType,
        string? email,
        string? name,
        string? color,
        string? thumbnailUrl,
        string? strategyText,
        CancellationToken cancellationToken)
    {
        var nickname = name?.Trim() ?? string.Empty;
        if (nickname.Length == 0)
        {
            return UserAccountResult.Failed("Name is required.");
        }

        var isBot = accountType == AccountType.Bot;
        if (!IsValidThumbnailUrl(thumbnailUrl?.Trim() ?? string.Empty))
        {
            return UserAccountResult.Failed("Picture must be a web address (https://...) or left blank.");
        }

        var userId = string.Empty;
        if (!isBot)
        {
            userId = email?.Trim().ToLowerInvariant() ?? string.Empty;
            if (!EmailLoginService.IsPlausibleEmail(userId))
            {
                return UserAccountResult.Failed("Enter the person's email address, like name@example.com.");
            }

            if (await userDirectory.GetUserAsync(userId, cancellationToken) is { } existing)
            {
                return UserAccountResult.Failed($"{DisplayName(existing)} already has that email.");
            }
        }

        if (await userDirectory.FindByNormalizedNicknameAsync(nickname.ToUpperInvariant(), cancellationToken) is { } namesake)
        {
            return UserAccountResult.Failed($"{DisplayName(namesake)} already uses that name.");
        }

        if (isBot)
        {
            var created = await userDirectory.CreateBotDefinitionAsync(actingUserId, nickname, strategyText ?? string.Empty, cancellationToken);
            if (!created.Succeeded || created.Bot is null)
            {
                return UserAccountResult.Failed(created.ErrorMessage ?? "Unable to add the bot.");
            }

            userId = created.Bot.BotDefinitionId;
        }
        else
        {
            var now = DateTimeOffset.UtcNow;
            try
            {
                await userDirectory.AddUserAsync(new ApplicationUser
                {
                    PartitionKey = "USER",
                    RowKey = userId,
                    Email = userId,
                    NormalizedEmail = userId.ToUpperInvariant(),
                    UserName = userId,
                    NormalizedUserName = userId.ToUpperInvariant(),
                    Name = nickname,
                    Nickname = nickname,
                    NormalizedNickname = nickname.ToUpperInvariant(),
                    // Left blank for Auth: the first Google or Microsoft sign-in records which one.
                    ExternalLoginProvider = accountType == AccountType.Email ? EmailLoginService.EmailProvider : string.Empty,
                    CreatedByUserId = actingUserId,
                    CreatedUtc = now,
                    ModifiedByUserId = actingUserId,
                    ModifiedUtc = now
                }, cancellationToken);
            }
            catch (RequestFailedException exception) when (exception.Status == 409)
            {
                return UserAccountResult.Failed("Someone already has that email.");
            }
        }

        // The color, picture and strategy go through the same checks as an edit.
        var saved = await SaveAsync(actingUserId, userId, ETag.All, nickname, color, thumbnailUrl, strategyText, cancellationToken);
        if (!saved.Success)
        {
            await userDirectory.DeleteUserAsync(userId, ETag.All, cancellationToken);
        }

        return saved;
    }

    /// <summary>
    /// Saves an account's name, color, picture and strategy. The name must be unique; a bot's name is also its bot name.
    /// <paramref name="eTag"/> is the version that was edited, so someone else's change in between isn't overwritten.
    /// </summary>
    public async Task<UserAccountResult> SaveAsync(
        string actingUserId,
        string userId,
        ETag eTag,
        string? name,
        string? color,
        string? thumbnailUrl,
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

        var thumbnail = thumbnailUrl?.Trim() ?? string.Empty;
        if (!IsValidThumbnailUrl(thumbnail))
        {
            return UserAccountResult.Failed("Picture must be a web address (https://...) or left blank.");
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
        user.ThumbnailUrl = thumbnail;
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

        return UserAccountResult.SucceededFor(user.RowKey);
    }

    /// <summary>Blank, a web address, or a path on this site (uploaded photos live under one).</summary>
    private static bool IsValidThumbnailUrl(string url)
    {
        if (url.Length == 0 || (url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal)))
        {
            return true;
        }

        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
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

/// <summary>How an account signs in.</summary>
public enum AccountType
{
    /// <summary>With Google or Microsoft, as the account's email.</summary>
    Auth,

    /// <summary>By email alone, with no password (see <see cref="EmailLoginService"/>). Signing in with Google or
    /// Microsoft as that email turns it into Auth.</summary>
    Email,

    /// <summary>An AI player; never signs in.</summary>
    Bot
}

/// <summary>Whether an account change worked: the error if not, and the account's id once it's added or saved.</summary>
public sealed record UserAccountResult(bool Success, string? Error, string? UserId = null)
{
    public static UserAccountResult Succeeded { get; } = new(true, null);

    public static UserAccountResult SucceededFor(string userId) => new(true, null, userId);

    public static UserAccountResult Failed(string error) => new(false, error);
}
