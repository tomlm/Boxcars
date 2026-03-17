using Azure;
using Azure.Data.Tables;
using Boxcars.Data;
using Boxcars.Identity;

namespace Boxcars.Services;

public sealed class BotDefinitionService
{
    private readonly TableClient _usersTable;

    public BotDefinitionService(TableServiceClient tableServiceClient)
    {
        _usersTable = tableServiceClient.GetTableClient(TableNames.UsersTable);
    }

    public async Task<IReadOnlyList<BotStrategyDefinitionEntity>> ListAsync(CancellationToken cancellationToken)
    {
        var bots = new List<BotStrategyDefinitionEntity>();

        await foreach (var entity in _usersTable.QueryAsync<ApplicationUser>(
                           user => user.PartitionKey == "USER" && user.IsBot,
                           cancellationToken: cancellationToken))
        {
            bots.Add(MapBot(entity, useGhostLabel: false));
        }

        return bots
            .OrderBy(bot => bot.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(bot => bot.BotDefinitionId, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<BotStrategyDefinitionEntity?> GetAsync(string botDefinitionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(botDefinitionId))
        {
            return null;
        }

        try
        {
            var response = await _usersTable.GetEntityAsync<ApplicationUser>(
                "USER",
                botDefinitionId,
                cancellationToken: cancellationToken);
            return MapBot(response.Value, useGhostLabel: !response.Value.IsBot);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<BotDefinitionWriteResult> CreateAsync(string actingUserId, string name, string strategyText, CancellationToken cancellationToken)
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
        var entity = new ApplicationUser
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
            StrategyText = PlayerProfileService.NormalizeStrategyText(strategyText),
            IsBot = true,
            CreatedByUserId = actingUserId,
            CreatedUtc = now,
            ModifiedByUserId = actingUserId,
            ModifiedUtc = now,
            SecurityStamp = Guid.NewGuid().ToString(),
            EmailConfirmed = true
        };

        await _usersTable.AddEntityAsync(entity, cancellationToken);
        return BotDefinitionWriteResult.Success(MapBot(entity, useGhostLabel: false));
    }

    public async Task<BotDefinitionWriteResult> UpdateAsync(
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

        if (string.IsNullOrWhiteSpace(botDefinitionId))
        {
            return BotDefinitionWriteResult.Invalid("Bot id is required.");
        }

        var trimmedName = name.Trim();
        if (trimmedName.Length == 0)
        {
            return BotDefinitionWriteResult.Invalid("Bot name is required.");
        }

        ApplicationUser existing;
        try
        {
            var response = await _usersTable.GetEntityAsync<ApplicationUser>("USER", botDefinitionId, cancellationToken: cancellationToken);
            existing = response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return BotDefinitionWriteResult.NotFound();
        }

        if (!existing.IsBot)
        {
            return BotDefinitionWriteResult.Invalid("Only bot users can be edited here.");
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
            StrategyText = PlayerProfileService.NormalizeStrategyText(strategyText),
            IsBot = true,
            PreferredColor = existing.PreferredColor,
            ThumbnailUrl = existing.ThumbnailUrl,
            CreatedByUserId = existing.CreatedByUserId,
            CreatedUtc = existing.CreatedUtc,
            ModifiedByUserId = actingUserId,
            ModifiedUtc = DateTimeOffset.UtcNow,
            PasswordHash = existing.PasswordHash,
            SecurityStamp = existing.SecurityStamp,
            EmailConfirmed = existing.EmailConfirmed,
            LockoutEnd = existing.LockoutEnd,
            LockoutEnabled = existing.LockoutEnabled,
            AccessFailedCount = existing.AccessFailedCount,
            ConcurrencyStamp = existing.ConcurrencyStamp,
            PhoneNumber = existing.PhoneNumber,
            PhoneNumberConfirmed = existing.PhoneNumberConfirmed,
            TwoFactorEnabled = existing.TwoFactorEnabled
        };

        try
        {
            await _usersTable.UpdateEntityAsync(updated, eTag, TableUpdateMode.Replace, cancellationToken);
            updated.ETag = eTag;
            return BotDefinitionWriteResult.Success(MapBot(updated, useGhostLabel: false));
        }
        catch (RequestFailedException ex) when (ex.Status == 412)
        {
            return BotDefinitionWriteResult.Conflict();
        }
    }

    public async Task<BotDefinitionDeleteResult> DeleteAsync(string botDefinitionId, ETag eTag, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(botDefinitionId))
        {
            return BotDefinitionDeleteResult.Invalid;
        }

        try
        {
            var response = await _usersTable.GetEntityAsync<ApplicationUser>("USER", botDefinitionId, cancellationToken: cancellationToken);
            if (!response.Value.IsBot)
            {
                return BotDefinitionDeleteResult.Invalid;
            }

            await _usersTable.DeleteEntityAsync("USER", botDefinitionId, eTag, cancellationToken);
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

    private static BotStrategyDefinitionEntity MapBot(ApplicationUser user, bool useGhostLabel)
    {
        var displayName = useGhostLabel
            ? "GHOST"
            : string.IsNullOrWhiteSpace(user.Nickname)
                ? user.Name
                : user.Nickname;

        return new BotStrategyDefinitionEntity
        {
            PartitionKey = user.PartitionKey,
            RowKey = user.RowKey,
            Timestamp = user.Timestamp,
            ETag = user.ETag,
            BotDefinitionId = user.RowKey,
            Name = displayName,
            StrategyText = PlayerProfileService.NormalizeStrategyText(user.StrategyText),
            IsBotUser = user.IsBot,
            CreatedByUserId = user.CreatedByUserId,
            CreatedUtc = user.CreatedUtc,
            ModifiedByUserId = user.ModifiedByUserId,
            ModifiedUtc = user.ModifiedUtc
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