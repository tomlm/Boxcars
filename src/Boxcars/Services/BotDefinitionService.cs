using Azure;
using Azure.Data.Tables;
using Boxcars.Data;
using Boxcars.Identity;

namespace Boxcars.Services;

public sealed class BotDefinitionService
{
    private readonly TableClient _botsTable;
    private readonly TableClient _usersTable;

    public BotDefinitionService(TableServiceClient tableServiceClient)
    {
        _botsTable = tableServiceClient.GetTableClient(TableNames.BotsTable);
        _usersTable = tableServiceClient.GetTableClient(TableNames.UsersTable);
    }

    public BotDefinitionService(TableClient botsTable, TableClient usersTable)
    {
        _botsTable = botsTable;
        _usersTable = usersTable;
    }

    public async Task<IReadOnlyList<BotStrategyDefinitionEntity>> ListAsync(CancellationToken cancellationToken)
    {
        var bots = new List<BotStrategyDefinitionEntity>();

        await foreach (var entity in _botsTable.QueryAsync<BotStrategyDefinitionEntity>(
                           bot => bot.PartitionKey == "BOT",
                           cancellationToken: cancellationToken))
        {
            bots.Add(MapBot(entity));
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
            var response = await _botsTable.GetEntityAsync<BotStrategyDefinitionEntity>(
                "BOT",
                botDefinitionId,
                cancellationToken: cancellationToken);
            return MapBot(response.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return await GetLegacyBotAsync(botDefinitionId, cancellationToken);
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
        var entity = new BotStrategyDefinitionEntity
        {
            PartitionKey = "BOT",
            RowKey = botId,
            Name = trimmedName,
            StrategyText = PlayerProfileService.NormalizeStrategyText(strategyText),
            IsBotUser = true,
            CreatedByUserId = actingUserId,
            CreatedUtc = now,
            ModifiedByUserId = actingUserId,
            ModifiedUtc = now
        };

        await _botsTable.AddEntityAsync(entity, cancellationToken);
        return BotDefinitionWriteResult.Success(MapBot(entity));
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

        BotStrategyDefinitionEntity existing;
        try
        {
            var response = await _botsTable.GetEntityAsync<BotStrategyDefinitionEntity>("BOT", botDefinitionId, cancellationToken: cancellationToken);
            existing = response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return BotDefinitionWriteResult.NotFound();
        }

        var updated = new BotStrategyDefinitionEntity
        {
            PartitionKey = existing.PartitionKey,
            RowKey = existing.RowKey,
            Timestamp = existing.Timestamp,
            ETag = existing.ETag,
            Name = trimmedName,
            StrategyText = PlayerProfileService.NormalizeStrategyText(strategyText),
            IsBotUser = true,
            CreatedByUserId = existing.CreatedByUserId,
            CreatedUtc = existing.CreatedUtc,
            ModifiedByUserId = actingUserId,
            ModifiedUtc = DateTimeOffset.UtcNow
        };

        try
        {
            await _botsTable.UpdateEntityAsync(updated, eTag, TableUpdateMode.Replace, cancellationToken);
            return BotDefinitionWriteResult.Success(MapBot(updated));
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
            await _botsTable.DeleteEntityAsync("BOT", botDefinitionId, eTag, cancellationToken);
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

    private async Task<BotStrategyDefinitionEntity?> GetLegacyBotAsync(string botDefinitionId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _usersTable.GetEntityAsync<ApplicationUser>("USER", botDefinitionId, cancellationToken: cancellationToken);
            return MapLegacyBot(response.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private static BotStrategyDefinitionEntity MapBot(BotStrategyDefinitionEntity entity)
    {
        return new BotStrategyDefinitionEntity
        {
            PartitionKey = entity.PartitionKey,
            RowKey = entity.RowKey,
            Timestamp = entity.Timestamp,
            ETag = entity.ETag,
            BotDefinitionId = entity.BotDefinitionId,
            Name = entity.Name,
            StrategyText = PlayerProfileService.NormalizeStrategyText(entity.StrategyText),
            IsBotUser = true,
            CreatedByUserId = entity.CreatedByUserId,
            CreatedUtc = entity.CreatedUtc,
            ModifiedByUserId = entity.ModifiedByUserId,
            ModifiedUtc = entity.ModifiedUtc
        };
    }

    private static BotStrategyDefinitionEntity MapLegacyBot(ApplicationUser user)
    {
        return new BotStrategyDefinitionEntity
        {
            PartitionKey = user.PartitionKey,
            RowKey = user.RowKey,
            Timestamp = user.Timestamp,
            ETag = user.ETag,
            BotDefinitionId = user.RowKey,
            Name = user.IsBot
                ? string.IsNullOrWhiteSpace(user.Nickname)
                    ? user.Name
                    : user.Nickname
                : "GHOST",
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