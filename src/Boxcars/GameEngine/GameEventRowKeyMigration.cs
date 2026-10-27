using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;

namespace Boxcars.GameEngine;

/// <summary>
/// Rewrites event rows saved with oldest-first keys (raw ticks) to the newest-first keys <see cref="GameEventRows"/>
/// writes now. Runs at startup before any request is served. Each batch adds the new rows and deletes the old ones
/// atomically, so an interrupted run just picks up where it stopped. A marker row records when every game is done;
/// delete it to sweep again (for example after a deploy where an old instance kept writing for a while).
/// </summary>
public static partial class GameEventRowKeyMigration
{
    private const string MarkerPartitionKey = "MIGRATION";
    private const string MarkerRowKey = "EventRowKeysNewestFirst";
    private const int MaxBatchOperations = 100;

    // Table batches cap out at 4 MiB; events carry whole game snapshots, so batches are cut by size as well as count.
    private const int MaxBatchPayloadChars = 1_500_000;

    private static readonly HashSet<string> SystemProperties = new(StringComparer.Ordinal)
    {
        "PartitionKey", "RowKey", "Timestamp", "odata.etag"
    };

    public static async Task RunAsync(TableClient gamesTable, ILogger logger, CancellationToken cancellationToken = default)
    {
        var marker = await gamesTable.GetEntityIfExistsAsync<TableEntity>(MarkerPartitionKey, MarkerRowKey, cancellationToken: cancellationToken);
        if (marker.HasValue)
        {
            return;
        }

        var gameIds = new List<string>();
        await foreach (var game in gamesTable.QueryAsync<TableEntity>(
                           filter: TableClient.CreateQueryFilter($"RowKey eq {"GAME"}"),
                           select: ["PartitionKey"],
                           cancellationToken: cancellationToken))
        {
            gameIds.Add(game.PartitionKey);
        }

        var migratedRows = 0;
        var failedGames = 0;
        foreach (var gameId in gameIds)
        {
            try
            {
                migratedRows += await MigrateGameAsync(gamesTable, gameId, cancellationToken);
            }
            catch (Exception ex) when (ex is RequestFailedException or TableTransactionFailedException)
            {
                failedGames++;
                LogGameFailed(logger, ex, gameId);
            }
        }

        LogMigrated(logger, migratedRows, gameIds.Count);
        if (failedGames == 0)
        {
            await gamesTable.UpsertEntityAsync(new TableEntity(MarkerPartitionKey, MarkerRowKey), cancellationToken: cancellationToken);
        }
    }

    private static async Task<int> MigrateGameAsync(TableClient gamesTable, string gameId, CancellationToken cancellationToken)
    {
        var filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {gameId} and RowKey ge {GameEventRows.RowKeyPrefix} and RowKey lt {GameEventRows.LegacyRowKeyExclusiveUpperBound}");
        var batch = new List<TableTransactionAction>();
        var batchChars = 0;
        var migrated = 0;

        await foreach (var legacy in gamesTable.QueryAsync<TableEntity>(filter: filter, cancellationToken: cancellationToken))
        {
            if (!TryConvertLegacyRowKey(legacy.RowKey, out var rowKey))
            {
                continue;
            }

            var replacement = new TableEntity(legacy.PartitionKey, rowKey);
            var rowChars = 0;
            foreach (var (name, value) in legacy)
            {
                if (!SystemProperties.Contains(name))
                {
                    replacement[name] = value;
                    rowChars += value is string text ? text.Length : 16;
                }
            }

            if (batch.Count + 2 > MaxBatchOperations || (batch.Count > 0 && batchChars + rowChars > MaxBatchPayloadChars))
            {
                await gamesTable.SubmitTransactionAsync(batch, cancellationToken);
                batch.Clear();
                batchChars = 0;
            }

            batch.Add(new TableTransactionAction(TableTransactionActionType.Add, replacement));
            batch.Add(new TableTransactionAction(TableTransactionActionType.Delete, legacy, ETag.All));
            batchChars += rowChars;
            migrated++;
        }

        if (batch.Count > 0)
        {
            await gamesTable.SubmitTransactionAsync(batch, cancellationToken);
        }

        return migrated;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not move the event rows of game {GameId} to newest-first keys; will retry next startup.")]
    private static partial void LogGameFailed(ILogger logger, Exception exception, string gameId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Moved {RowCount} event rows in {GameCount} games to newest-first keys.")]
    private static partial void LogMigrated(ILogger logger, int rowCount, int gameCount);

    /// <summary>
    /// "Event_{ticks:D20}_{suffix}" to the newest-first form. A numeric suffix is the engine's sequence and is
    /// reversed with the ticks; a GUID suffix is kept.
    /// </summary>
    public static bool TryConvertLegacyRowKey(string legacyRowKey, out string rowKey)
    {
        rowKey = string.Empty;
        var prefixLength = GameEventRows.RowKeyPrefix.Length;
        if (legacyRowKey.Length < prefixLength + 22
            || legacyRowKey[prefixLength + 20] != '_'
            || !long.TryParse(legacyRowKey.AsSpan(prefixLength, 20), out var ticks)
            || ticks < 0
            || ticks > DateTime.MaxValue.Ticks)
        {
            return false;
        }

        var suffix = legacyRowKey[(prefixLength + 21)..];
        var utc = new DateTime(ticks, DateTimeKind.Utc);
        rowKey = suffix.Length == 8 && long.TryParse(suffix, out var sequence)
            ? GameEventRows.CreateRowKey(utc, sequence)
            : $"{GameEventRows.CreateRowKey(utc)[..(prefixLength + 21)]}{suffix}";
        return true;
    }
}
