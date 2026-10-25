using System.Security.Cryptography;
using System.Text;
using Boxcars.Data;
using Boxcars.Services;

namespace Boxcars.Auth;

/// <summary>
/// Signing in with just an email address, for players who find Google or Microsoft sign-in too hard. There's no
/// password: anyone can sign in as any email-only player, so an account that has ever signed in with Google or
/// Microsoft (or a bot) can't be used this way. Typing an email that isn't in use yet creates the account; signing in
/// with Google or Microsoft as that email later turns it into a normal account.
/// </summary>
public sealed class EmailLoginService(UserDirectoryService userDirectory)
{
    /// <summary>What <see cref="ApplicationUser.ExternalLoginProvider"/> is set to for an account made here.</summary>
    public const string EmailProvider = "Email";

    /// <summary>
    /// An account anyone can sign in as by its email: a person whose account was made here and has never signed in
    /// with Google or Microsoft. Older accounts with no provider recorded came from Google or Microsoft, so they don't count.
    /// </summary>
    public static bool IsEmailOnly(ApplicationUser user)
    {
        return !user.IsBot && string.Equals(user.ExternalLoginProvider, EmailProvider, StringComparison.Ordinal);
    }

    /// <summary>Everyone who plays (not bots), by name, for the sign-in page's "Who are you?" list.</summary>
    public async Task<IReadOnlyList<ApplicationUser>> ListPeopleAsync(CancellationToken cancellationToken)
    {
        var users = await userDirectory.ListUsersAsync(cancellationToken);
        return users
            .Where(user => !user.IsBot)
            .OrderBy(DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Identifies a person on the sign-in page without putting their email address in the page, which anyone can see.
    /// </summary>
    public static string PlayerKey(ApplicationUser user)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(user.RowKey)))[..16];
    }

    /// <summary>The person a <see cref="PlayerKey"/> stands for, or null when no one (or only a bot) has it.</summary>
    public async Task<ApplicationUser?> FindByPlayerKeyAsync(string? playerKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(playerKey))
        {
            return null;
        }

        var people = await ListPeopleAsync(cancellationToken);
        return people.FirstOrDefault(user => string.Equals(PlayerKey(user), playerKey, StringComparison.Ordinal));
    }

    /// <summary>Signs in as the email-only person picked on the sign-in page.</summary>
    public async Task<EmailLoginResult> ResolvePlayerAsync(string? playerKey, CancellationToken cancellationToken)
    {
        var user = await FindByPlayerKeyAsync(playerKey, cancellationToken);
        return user is null
            ? EmailLoginResult.Failed("That player wasn't found. Pick your name again.")
            : IsEmailOnly(user)
                ? EmailLoginResult.SignIn(user)
                : EmailLoginResult.Failed("That account signs in with Google or Microsoft.");
    }

    /// <summary>The name a player is listed under.</summary>
    public static string DisplayName(ApplicationUser user)
    {
        return !string.IsNullOrWhiteSpace(user.Nickname) ? user.Nickname
            : !string.IsNullOrWhiteSpace(user.Name) ? user.Name
            : user.RowKey;
    }

    /// <summary>The account to sign in as for <paramref name="email"/>, creating it when the email isn't in use.</summary>
    public async Task<EmailLoginResult> ResolveAsync(string? email, CancellationToken cancellationToken)
    {
        var normalizedEmail = email?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!IsPlausibleEmail(normalizedEmail))
        {
            return EmailLoginResult.Failed("Enter an email address, like name@example.com.");
        }

        var user = await userDirectory.GetUserAsync(normalizedEmail, cancellationToken);
        if (user is not null)
        {
            return IsEmailOnly(user)
                ? EmailLoginResult.SignIn(user)
                : EmailLoginResult.Failed(user.IsBot
                    ? "That email belongs to a bot."
                    : string.IsNullOrWhiteSpace(user.ExternalLoginProvider)
                        ? "That account signs in with Google or Microsoft. Use one of those buttons instead."
                        : $"That account signs in with {user.ExternalLoginProvider}. Use the {user.ExternalLoginProvider} button instead.");
        }

        var nickname = normalizedEmail.Split('@')[0];
        var now = DateTimeOffset.UtcNow;
        user = new ApplicationUser
        {
            PartitionKey = "USER",
            RowKey = normalizedEmail,
            Email = normalizedEmail,
            NormalizedEmail = normalizedEmail.ToUpperInvariant(),
            UserName = normalizedEmail,
            NormalizedUserName = normalizedEmail.ToUpperInvariant(),
            Name = nickname,
            Nickname = nickname,
            NormalizedNickname = nickname.ToUpperInvariant(),
            ExternalLoginProvider = EmailProvider,
            CreatedByUserId = normalizedEmail,
            CreatedUtc = now,
            ModifiedByUserId = normalizedEmail,
            ModifiedUtc = now
        };

        try
        {
            await userDirectory.AddUserAsync(user, cancellationToken);
        }
        catch (Azure.RequestFailedException exception) when (exception.Status == 409)
        {
            // Created at the same moment by someone else: sign in to that one if it's still email-only.
            return await ResolveAsync(normalizedEmail, cancellationToken);
        }

        return EmailLoginResult.SignIn(user);
    }

    /// <summary>Whether a lowercased, trimmed address looks like a real person's email (not a bot's).</summary>
    public static bool IsPlausibleEmail(string email)
    {
        var at = email.IndexOf('@');
        return email.Length <= 254
            && at > 0
            && at == email.LastIndexOf('@')
            && at < email.Length - 1
            && !email.Any(char.IsWhiteSpace)
            && !email.EndsWith("@boxcars.bot", StringComparison.Ordinal);
    }
}

public sealed record EmailLoginResult(ApplicationUser? User, string? Error)
{
    public static EmailLoginResult SignIn(ApplicationUser user) => new(user, null);

    public static EmailLoginResult Failed(string error) => new(null, error);
}
