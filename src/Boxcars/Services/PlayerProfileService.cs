using Azure;
using System.Collections.Concurrent;
using Boxcars.Data;

namespace Boxcars.Services;

public class PlayerProfileService
{
    public const string DefaultStrategyText = "Select the best balance of access, monopoly and network building";

    private readonly UserDirectoryService _userDirectoryService;
    private readonly ConcurrentDictionary<string, CachedProfile> _profileCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Task<CachedProfile>>> _profileLoads = new(StringComparer.OrdinalIgnoreCase);

    public PlayerProfileService(UserDirectoryService userDirectoryService)
    {
        _userDirectoryService = userDirectoryService;
    }

    public async Task<ApplicationUser?> GetProfileAsync(string userId, CancellationToken cancellationToken)
    {
        var normalizedUserId = NormalizeUserId(userId);
        if (string.IsNullOrWhiteSpace(normalizedUserId))
        {
            return null;
        }

        if (_profileCache.TryGetValue(normalizedUserId, out var cachedProfile))
        {
            return CloneProfile(cachedProfile.Profile);
        }

        var load = _profileLoads.GetOrAdd(
            normalizedUserId,
            normalizedId => new Lazy<Task<CachedProfile>>(
                () => LoadProfileAsync(normalizedId),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            var loadedProfile = await load.Value.WaitAsync(cancellationToken);
            _profileCache[normalizedUserId] = loadedProfile;
            return CloneProfile(loadedProfile.Profile);
        }
        finally
        {
            _profileLoads.TryRemove(normalizedUserId, out _);
        }

    }

    private async Task<CachedProfile> LoadProfileAsync(string normalizedId)
    {
        var user = await _userDirectoryService.GetUserAsync(normalizedId, CancellationToken.None);
        return new CachedProfile(user is null ? null : MapProfile(user));
    }

    public async Task<List<ApplicationUser>> GetSelectableProfilesAsync(CancellationToken cancellationToken)
    {
        var profiles = new List<ApplicationUser>();
        var users = await _userDirectoryService.ListUsersAsync(cancellationToken);

        foreach (var user in users)
        {
            if (!string.IsNullOrWhiteSpace(user.Nickname))
            {
                var mappedProfile = MapProfile(user);
                _profileCache[NormalizeUserId(mappedProfile.RowKey)] = new CachedProfile(mappedProfile);
                profiles.Add(CloneProfile(mappedProfile)!);
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
        var user = await _userDirectoryService.GetUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return NicknameResult.UserNotFound;
        }

        var existing = await _userDirectoryService.FindByNormalizedNicknameAsync(normalizedNickname, cancellationToken);
        if (existing is not null
            && !string.Equals(existing.RowKey, user.RowKey, StringComparison.OrdinalIgnoreCase))
        {
            return NicknameResult.Conflict;
        }

        // Update user entity
        user.Nickname = newNickname.Trim();
        user.NormalizedNickname = normalizedNickname;

        try
        {
            await _userDirectoryService.UpdateUserAsync(user, user.ETag, cancellationToken);
            UpdateCachedProfile(user);
        }
        catch (RequestFailedException)
        {
            return NicknameResult.UpdateFailed;
        }

        return NicknameResult.Success;
    }

    public async Task<bool> UpdateThumbnailUrlAsync(string userId, string newUrl, CancellationToken cancellationToken)
    {
        var user = await _userDirectoryService.GetUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return false;
        }

        try
        {
            user.ThumbnailUrl = newUrl;
            await _userDirectoryService.UpdateUserAsync(user, user.ETag, cancellationToken);
            UpdateCachedProfile(user);
            return true;
        }
        catch (RequestFailedException)
        {
            return false;
        }
    }

    public async Task<bool> UpdatePreferredColorAsync(string userId, string preferredColor, CancellationToken cancellationToken)
    {
        var user = await _userDirectoryService.GetUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return false;
        }

        try
        {
            user.PreferredColor = preferredColor;
            await _userDirectoryService.UpdateUserAsync(user, user.ETag, cancellationToken);
            UpdateCachedProfile(user);
            return true;
        }
        catch (RequestFailedException)
        {
            return false;
        }
    }

    public async Task<bool> UpdateStrategyTextAsync(string userId, string strategyText, CancellationToken cancellationToken)
    {
        var user = await _userDirectoryService.GetUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return false;
        }

        try
        {
            user.StrategyText = NormalizeStrategyText(strategyText);
            user.ModifiedUtc = DateTimeOffset.UtcNow;
            await _userDirectoryService.UpdateUserAsync(user, user.ETag, cancellationToken);
            UpdateCachedProfile(user);
            return true;
        }
        catch (RequestFailedException)
        {
            return false;
        }
    }

    private static ApplicationUser MapProfile(ApplicationUser user)
    {
        user.RowKey = NormalizeUserId(user.RowKey);
        user.ThumbnailUrl = ResolveThumbnailUrl(user.Email, user.ThumbnailUrl);
        user.StrategyText = NormalizeStrategyText(user.StrategyText);
        return user;
    }

    private void UpdateCachedProfile(ApplicationUser user)
    {
        var normalizedUserId = NormalizeUserId(user.RowKey);
        if (string.IsNullOrWhiteSpace(normalizedUserId))
        {
            return;
        }

        _profileCache[normalizedUserId] = new CachedProfile(MapProfile(CloneProfile(user)!));
        _profileLoads.TryRemove(normalizedUserId, out _);
    }

    private static string NormalizeUserId(string? userId)
    {
        return userId?.Trim().ToLowerInvariant() ?? string.Empty;
    }

    private static ApplicationUser? CloneProfile(ApplicationUser? user)
    {
        if (user is null)
        {
            return null;
        }

        return new ApplicationUser
        {
            PartitionKey = user.PartitionKey,
            RowKey = user.RowKey,
            Timestamp = user.Timestamp,
            ETag = user.ETag,
            Name = user.Name,
            Email = user.Email,
            NormalizedEmail = user.NormalizedEmail,
            UserName = user.UserName,
            NormalizedUserName = user.NormalizedUserName,
            PasswordHash = user.PasswordHash,
            SecurityStamp = user.SecurityStamp,
            EmailConfirmed = user.EmailConfirmed,
            LockoutEnd = user.LockoutEnd,
            LockoutEnabled = user.LockoutEnabled,
            AccessFailedCount = user.AccessFailedCount,
            Nickname = user.Nickname,
            NormalizedNickname = user.NormalizedNickname,
            ThumbnailUrl = user.ThumbnailUrl,
            PreferredColor = user.PreferredColor,
            StrategyText = user.StrategyText,
            IsBot = user.IsBot,
            CreatedByUserId = user.CreatedByUserId,
            CreatedUtc = user.CreatedUtc,
            ModifiedByUserId = user.ModifiedByUserId,
            ModifiedUtc = user.ModifiedUtc,
            ConcurrencyStamp = user.ConcurrencyStamp,
            PhoneNumber = user.PhoneNumber,
            PhoneNumberConfirmed = user.PhoneNumberConfirmed,
            TwoFactorEnabled = user.TwoFactorEnabled
        };
    }

    public static string NormalizeStrategyText(string? strategyText)
    {
        return strategyText?.Trim() ?? string.Empty;
    }

    public static string ResolveStrategyTextOrDefault(string? strategyText)
    {
        var normalized = NormalizeStrategyText(strategyText);
        return string.IsNullOrWhiteSpace(normalized)
            ? DefaultStrategyText
            : normalized;
    }

    public static bool HasRequiredStrategyText(string? strategyText)
    {
        return string.IsNullOrWhiteSpace(NormalizeStrategyText(strategyText)) is false;
    }

    private static string ResolveThumbnailUrl(string email, string? thumbnailUrl)
    {
        if (!string.IsNullOrWhiteSpace(thumbnailUrl) && !thumbnailUrl.Contains("placeholder"))
        {
            return thumbnailUrl;
        }

        return string.IsNullOrWhiteSpace(email)
            ? string.Empty
            : $"https://robohash.org/{Uri.EscapeDataString(email)}";
    }

    private sealed record CachedProfile(ApplicationUser? Profile);
}

public enum NicknameResult
{
    Success,
    Conflict,
    UserNotFound,
    UpdateFailed
}
