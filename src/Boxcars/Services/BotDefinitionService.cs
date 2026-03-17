using Azure;
using Azure.Data.Tables;
using Boxcars.Data;
using Boxcars.Identity;

namespace Boxcars.Services;

public sealed class BotDefinitionService
{
    private const string BotPartitionKey = "BOT";

    private readonly TableClient _botsTable;

    public BotDefinitionService(TableServiceClient tableServiceClient)
    {
        _botsTable = tableServiceClient.GetTableClient(TableNames.BotsTable);
    }

    public async Task<IReadOnlyList<BotStrategyDefinitionEntity>> ListAsync(CancellationToken cancellationToken)
    {
        var bots = new List<BotStrategyDefinitionEntity>();

        await foreach (var entity in _botsTable.QueryAsync<BotStrategyDefinitionEntity>(
                           bot => bot.PartitionKey == BotPartitionKey,
                           cancellationToken: cancellationToken))
        {
            bots.Add(entity);
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
                BotPartitionKey,
                botDefinitionId,
                cancellationToken: cancellationToken);
            return response.Value;
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
        var entity = new BotStrategyDefinitionEntity
        {
            BotDefinitionId = Guid.NewGuid().ToString("N"),
            Name = trimmedName,
            StrategyText = strategyText.Trim(),
            CreatedByUserId = actingUserId,
            CreatedUtc = now,
            ModifiedByUserId = actingUserId,
            ModifiedUtc = now
        };

        await _botsTable.AddEntityAsync(entity, cancellationToken);
        return BotDefinitionWriteResult.Success(entity);
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

        var existing = await GetAsync(botDefinitionId, cancellationToken);
        if (existing is null)
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
            StrategyText = strategyText.Trim(),
            CreatedByUserId = existing.CreatedByUserId,
            CreatedUtc = existing.CreatedUtc,
            ModifiedByUserId = actingUserId,
            ModifiedUtc = DateTimeOffset.UtcNow
        };

        try
        {
            await _botsTable.UpdateEntityAsync(updated, eTag, TableUpdateMode.Replace, cancellationToken);
            updated.ETag = eTag;
            return BotDefinitionWriteResult.Success(updated);
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
            await _botsTable.DeleteEntityAsync(BotPartitionKey, botDefinitionId, eTag, cancellationToken);
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