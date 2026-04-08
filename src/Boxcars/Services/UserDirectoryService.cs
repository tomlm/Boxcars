using Azure;
using Azure.Data.Tables;
using Boxcars.Data;
using Boxcars.Identity;
// (Identity namespace retained for TableNames only)

namespace Boxcars.Services;

public sealed class UserDirectoryService
{
    private readonly TableClient _usersTable;
    private readonly Dictionary<string, BotStrategyDefinitionEntity?> _botCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _botCacheLock = new();

    public UserDirectoryService(TableServiceClient tableServiceClient)
    {
        _usersTable = tableServiceClient.GetTableClient(TableNames.UsersTable);
    }

    public UserDirectoryService(TableClient usersTable)
    {
        _usersTable = usersTable;
    }

    public async Task<ApplicationUser?> GetUserAsync(string userId, CancellationToken cancellationToken)
    {
        var normalizedUserId = NormalizeUserId(userId);
        if (string.IsNullOrWhiteSpace(normalizedUserId))
        {
            return null;
        }

        try
        {
            var response = await _usersTable.GetEntityAsync<ApplicationUser>("USER", normalizedUserId, cancellationToken: cancellationToken);
            return CloneUser(response.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<ApplicationUser>> ListUsersAsync(CancellationToken cancellationToken)
    {
        var users = new List<ApplicationUser>();

        await foreach (var user in _usersTable.QueryAsync<ApplicationUser>(
                           entity => entity.PartitionKey == "USER",
                           cancellationToken: cancellationToken))
        {
            users.Add(CloneUser(user));
        }

        return users;
    }

    public async Task<ApplicationUser?> FindByNormalizedNicknameAsync(string normalizedNickname, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(normalizedNickname))
        {
            return null;
        }

        await foreach (var existing in _usersTable.QueryAsync<ApplicationUser>(
                           entity => entity.PartitionKey == "USER" && entity.NormalizedNickname == normalizedNickname,
                           cancellationToken: cancellationToken))
        {
            return CloneUser(existing);
        }

        return null;
    }

    public Task AddUserAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        user.PartitionKey = "USER";
        user.RowKey = NormalizeUserId(user.RowKey);
        InvalidateBotCache(user.RowKey);
        return _usersTable.AddEntityAsync(user, cancellationToken);
    }

    public async Task UpdateUserAsync(ApplicationUser user, ETag eTag, CancellationToken cancellationToken)
    {
        user.PartitionKey = "USER";
        user.RowKey = NormalizeUserId(user.RowKey);
        await _usersTable.UpdateEntityAsync(user, eTag, TableUpdateMode.Replace, cancellationToken);
        InvalidateBotCache(user.RowKey);
    }

    public async Task DeleteUserAsync(string userId, ETag eTag, CancellationToken cancellationToken)
    {
        var normalizedUserId = NormalizeUserId(userId);
        await _usersTable.DeleteEntityAsync("USER", normalizedUserId, eTag, cancellationToken);
        InvalidateBotCache(normalizedUserId);
    }

    public async Task<IReadOnlyList<BotStrategyDefinitionEntity>> ListBotDefinitionsAsync(CancellationToken cancellationToken)
    {
        var bots = new List<BotStrategyDefinitionEntity>();

        await foreach (var user in _usersTable.QueryAsync<ApplicationUser>(
                           candidate => candidate.PartitionKey == "USER" && candidate.IsBot,
                           cancellationToken: cancellationToken))
        {
            var mappedBot = MapBotDefinition(user);
            bots.Add(mappedBot);

            lock (_botCacheLock)
            {
                _botCache[mappedBot.BotDefinitionId] = CloneBotDefinition(mappedBot);
            }
        }

        return bots
            .OrderBy(bot => bot.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(bot => bot.BotDefinitionId, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<BotStrategyDefinitionEntity?> GetBotDefinitionAsync(string botDefinitionId, CancellationToken cancellationToken)
    {
        var normalizedBotDefinitionId = NormalizeUserId(botDefinitionId);
        if (string.IsNullOrWhiteSpace(normalizedBotDefinitionId))
        {
            return null;
        }

        lock (_botCacheLock)
        {
            if (_botCache.TryGetValue(normalizedBotDefinitionId, out var cachedBot))
            {
                return CloneBotDefinition(cachedBot);
            }
        }

        var user = await GetUserAsync(normalizedBotDefinitionId, cancellationToken);
        var bot = user is not null && user.IsBot
            ? MapBotDefinition(user)
            : null;

        lock (_botCacheLock)
        {
            _botCache[normalizedBotDefinitionId] = CloneBotDefinition(bot);
        }

        return CloneBotDefinition(bot);
    }

    public async Task<BotStrategyDefinitionEntity?> GetAutomationProfileAsync(string userId, CancellationToken cancellationToken)
    {
        var normalizedUserId = NormalizeUserId(userId);
        if (string.IsNullOrWhiteSpace(normalizedUserId))
        {
            return null;
        }

        var user = await GetUserAsync(normalizedUserId, cancellationToken);
        return user is null
            ? null
            : MapAutomationProfile(user);
    }

    public async Task<BotDefinitionWriteResult> CreateBotDefinitionAsync(string actingUserId, string name, string strategyText, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actingUserId))
        {
            return BotDefinitionWriteResult.Invalid("An authenticated user is required.");
        }

        var trimmedName = name.Trim();
        if (trimmedName.Length == 0)
        {
            return BotDefinitionWriteResult.Invalid("Bot name is required.");
        }

        var now = DateTimeOffset.UtcNow;
        var botId = $"bot-{Guid.NewGuid():N}@boxcars.bot";
        var user = new ApplicationUser
        {
            PartitionKey = "USER",
            RowKey = botId,
            Email = botId,
            NormalizedEmail = botId.ToUpperInvariant(),
            UserName = botId,
            NormalizedUserName = botId.ToUpperInvariant(),
            Name = trimmedName,
            Nickname = trimmedName,
            NormalizedNickname = trimmedName.ToUpperInvariant(),
            StrategyText = NormalizeStrategyText(strategyText),
            IsBot = true,
            CreatedByUserId = actingUserId,
            CreatedUtc = now,
            ModifiedByUserId = actingUserId,
            ModifiedUtc = now
        };

        await AddUserAsync(user, cancellationToken);
        var createdBot = MapBotDefinition(user);

        lock (_botCacheLock)
        {
            _botCache[createdBot.BotDefinitionId] = CloneBotDefinition(createdBot);
        }

        return BotDefinitionWriteResult.Success(createdBot);
    }

    public async Task<BotDefinitionWriteResult> UpdateBotDefinitionAsync(
        string actingUserId,
        string botDefinitionId,
        ETag eTag,
        string name,
        string strategyText,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actingUserId))
        {
            return BotDefinitionWriteResult.Invalid("An authenticated user is required.");
        }

        var normalizedBotDefinitionId = NormalizeUserId(botDefinitionId);
        if (string.IsNullOrWhiteSpace(normalizedBotDefinitionId))
        {
            return BotDefinitionWriteResult.Invalid("Bot id is required.");
        }

        var trimmedName = name.Trim();
        if (trimmedName.Length == 0)
        {
            return BotDefinitionWriteResult.Invalid("Bot name is required.");
        }

        var existing = await GetUserAsync(normalizedBotDefinitionId, cancellationToken);
        if (existing is null || !existing.IsBot)
        {
            return BotDefinitionWriteResult.NotFound();
        }

        var updated = new ApplicationUser
        {
            PartitionKey = existing.PartitionKey,
            RowKey = existing.RowKey,
            Timestamp = existing.Timestamp,
            ETag = existing.ETag,
            Email = existing.Email,
            NormalizedEmail = existing.NormalizedEmail,
            UserName = existing.UserName,
            NormalizedUserName = existing.NormalizedUserName,
            Name = trimmedName,
            Nickname = trimmedName,
            NormalizedNickname = trimmedName.ToUpperInvariant(),
            StrategyText = NormalizeStrategyText(strategyText),
            IsBot = true,
            CreatedByUserId = existing.CreatedByUserId,
            CreatedUtc = existing.CreatedUtc,
            ModifiedByUserId = actingUserId,
            ModifiedUtc = DateTimeOffset.UtcNow,
            PreferredColor = existing.PreferredColor,
            ThumbnailUrl = existing.ThumbnailUrl,
            ExternalLoginProvider = existing.ExternalLoginProvider,
            ExternalLoginKey = existing.ExternalLoginKey
        };

        try
        {
            await UpdateUserAsync(updated, eTag, cancellationToken);
            var updatedBot = MapBotDefinition(updated);

            lock (_botCacheLock)
            {
                _botCache[updatedBot.BotDefinitionId] = CloneBotDefinition(updatedBot);
            }

            return BotDefinitionWriteResult.Success(updatedBot);
        }
        catch (RequestFailedException ex) when (ex.Status == 412)
        {
            return BotDefinitionWriteResult.Conflict();
        }
    }

    public async Task<BotDefinitionDeleteResult> DeleteBotDefinitionAsync(string botDefinitionId, ETag eTag, CancellationToken cancellationToken)
    {
        var normalizedBotDefinitionId = NormalizeUserId(botDefinitionId);
        if (string.IsNullOrWhiteSpace(normalizedBotDefinitionId))
        {
            return BotDefinitionDeleteResult.Invalid;
        }

        var existing = await GetUserAsync(normalizedBotDefinitionId, cancellationToken);
        if (existing is null || !existing.IsBot)
        {
            return BotDefinitionDeleteResult.NotFound;
        }

        try
        {
            await DeleteUserAsync(normalizedBotDefinitionId, eTag, cancellationToken);
            return BotDefinitionDeleteResult.Success;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return BotDefinitionDeleteResult.NotFound;
        }
        catch (RequestFailedException ex) when (ex.Status == 412)
        {
            return BotDefinitionDeleteResult.Conflict;
        }
    }

    private void InvalidateBotCache(string userId)
    {
        lock (_botCacheLock)
        {
            _botCache.Remove(userId);
        }
    }

    private static string NormalizeUserId(string? userId)
    {
        return userId?.Trim().ToLowerInvariant() ?? string.Empty;
    }

    private static string NormalizeStrategyText(string? strategyText)
    {
        return strategyText?.Trim() ?? string.Empty;
    }

    private static ApplicationUser CloneUser(ApplicationUser user)
    {
        return new ApplicationUser
        {
            PartitionKey = user.PartitionKey,
            RowKey = NormalizeUserId(user.RowKey),
            Timestamp = user.Timestamp,
            ETag = user.ETag,
            Name = user.Name,
            Email = user.Email,
            NormalizedEmail = user.NormalizedEmail,
            UserName = user.UserName,
            NormalizedUserName = user.NormalizedUserName,
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
            ExternalLoginProvider = user.ExternalLoginProvider,
            ExternalLoginKey = user.ExternalLoginKey
        };
    }

    private static BotStrategyDefinitionEntity MapBotDefinition(ApplicationUser user)
    {
        return new BotStrategyDefinitionEntity
        {
            PartitionKey = user.PartitionKey,
            RowKey = NormalizeUserId(user.RowKey),
            Timestamp = user.Timestamp,
            ETag = user.ETag,
            BotDefinitionId = NormalizeUserId(user.RowKey),
            Name = string.IsNullOrWhiteSpace(user.Nickname)
                ? user.Name
                : user.Nickname,
            StrategyText = NormalizeStrategyText(user.StrategyText),
            IsBotUser = user.IsBot,
            CreatedByUserId = user.CreatedByUserId,
            CreatedUtc = user.CreatedUtc,
            ModifiedByUserId = user.ModifiedByUserId,
            ModifiedUtc = user.ModifiedUtc
        };
    }

    private static BotStrategyDefinitionEntity MapAutomationProfile(ApplicationUser user)
    {
        return new BotStrategyDefinitionEntity
        {
            PartitionKey = user.PartitionKey,
            RowKey = NormalizeUserId(user.RowKey),
            Timestamp = user.Timestamp,
            ETag = user.ETag,
            BotDefinitionId = NormalizeUserId(user.RowKey),
            Name = string.IsNullOrWhiteSpace(user.Nickname)
                ? user.Name
                : user.Nickname,
            StrategyText = PlayerProfileService.ResolveStrategyTextOrDefault(user.StrategyText),
            IsBotUser = user.IsBot,
            CreatedByUserId = user.CreatedByUserId,
            CreatedUtc = user.CreatedUtc,
            ModifiedByUserId = user.ModifiedByUserId,
            ModifiedUtc = user.ModifiedUtc
        };
    }

    private static BotStrategyDefinitionEntity? CloneBotDefinition(BotStrategyDefinitionEntity? bot)
    {
        if (bot is null)
        {
            return null;
        }

        return new BotStrategyDefinitionEntity
        {
            PartitionKey = bot.PartitionKey,
            RowKey = bot.RowKey,
            Timestamp = bot.Timestamp,
            ETag = bot.ETag,
            BotDefinitionId = bot.BotDefinitionId,
            Name = bot.Name,
            StrategyText = bot.StrategyText,
            IsBotUser = bot.IsBotUser,
            CreatedByUserId = bot.CreatedByUserId,
            CreatedUtc = bot.CreatedUtc,
            ModifiedByUserId = bot.ModifiedByUserId,
            ModifiedUtc = bot.ModifiedUtc
        };
    }
}

public sealed record BotDefinitionWriteResult
{
    public bool Succeeded { get; init; }
    public bool IsConflict { get; init; }
    public bool IsNotFound { get; init; }
    public string? ErrorMessage { get; init; }
    public BotStrategyDefinitionEntity? Bot { get; init; }

    public static BotDefinitionWriteResult Success(BotStrategyDefinitionEntity entity) => new()
    {
        Succeeded = true,
        Bot = entity
    };

    public static BotDefinitionWriteResult Conflict() => new()
    {
        IsConflict = true,
        ErrorMessage = "The bot definition was modified by another user. Refresh and try again."
    };

    public static BotDefinitionWriteResult NotFound() => new()
    {
        IsNotFound = true,
        ErrorMessage = "The bot definition no longer exists."
    };

    public static BotDefinitionWriteResult Invalid(string message) => new()
    {
        ErrorMessage = message
    };
}

public enum BotDefinitionDeleteResult
{
    Success,
    Conflict,
    NotFound,
    Invalid
}