using Azure.Data.Tables;
using Boxcars.Data;

namespace Boxcars.GameEngine;

/// <summary>
/// Row keys and reads for a game's event rows. Keys hold the time reversed (max ticks minus now), so the table, which
/// only reads in ascending key order, returns the newest event first and the current state is a single-row read.
/// </summary>
public static class GameEventRows
{
    public const string RowKeyPrefix = "Event_";
    public const string RowKeyExclusiveUpperBound = "Event`";

    /// <summary>
    /// Keys from before the reversal held the raw ticks, which pad to "Event_00..."; reversed ticks start "Event_02..."
    /// for any date this side of the year 6000.
    /// </summary>
    public const string LegacyRowKeyExclusiveUpperBound = "Event_01";

    private const long MaxSequence = 99_999_999;

    /// <summary>A key for an event the engine numbers itself, so events written in the same tick keep their order.</summary>
    public static string CreateRowKey(DateTime utcNow, long sequence)
    {
        return $"{RowKeyPrefix}{DateTime.MaxValue.Ticks - utcNow.Ticks:D20}_{MaxSequence - (sequence % MaxSequence):D8}";
    }

    public static string CreateRowKey(DateTime utcNow)
    {
        return $"{RowKeyPrefix}{DateTime.MaxValue.Ticks - utcNow.Ticks:D20}_{Guid.NewGuid():N}";
    }

    // Reads start past the legacy range, so an oldest-first row written by an instance still running the old code
    // (mid-deploy) can't pass itself off as the newest event; the next startup's migration moves it.
    public static string CreateFilter(string gameId)
    {
        return TableClient.CreateQueryFilter(
            $"PartitionKey eq {gameId} and RowKey ge {LegacyRowKeyExclusiveUpperBound} and RowKey lt {RowKeyExclusiveUpperBound}");
    }

    /// <summary>Events newer than <paramref name="rowKey"/>, plus that event itself.</summary>
    public static string CreateFilterSince(string gameId, string rowKey)
    {
        return TableClient.CreateQueryFilter(
            $"PartitionKey eq {gameId} and RowKey ge {LegacyRowKeyExclusiveUpperBound} and RowKey le {rowKey}");
    }

    /// <summary>
    /// The game's events, newest first, read a page at a time: stop enumerating and the rest is never fetched. With
    /// <paramref name="sinceRowKey"/>, only that event and the ones after it.
    /// </summary>
    public static IAsyncEnumerable<GameEventEntity> ReadNewestFirst(
        this TableClient gamesTable,
        string gameId,
        string? sinceRowKey = null,
        int? pageSize = null,
        IEnumerable<string>? select = null,
        CancellationToken cancellationToken = default)
    {
        return gamesTable.QueryAsync<GameEventEntity>(
            filter: sinceRowKey is null ? CreateFilter(gameId) : CreateFilterSince(gameId, sinceRowKey),
            maxPerPage: pageSize,
            select: select,
            cancellationToken: cancellationToken);
    }

    public static async Task<GameEventEntity?> GetLatestEventAsync(this TableClient gamesTable, string gameId, CancellationToken cancellationToken = default)
    {
        return (await gamesTable.GetLatestEventsAsync(gameId, 1, cancellationToken)).FirstOrDefault();
    }

    /// <summary>The game's newest <paramref name="count"/> events, newest first.</summary>
    public static async Task<List<GameEventEntity>> GetLatestEventsAsync(this TableClient gamesTable, string gameId, int count, CancellationToken cancellationToken = default)
    {
        return await gamesTable.ReadNewestFirst(gameId, pageSize: count, cancellationToken: cancellationToken)
            .Take(count)
            .ToListAsync(cancellationToken);
    }
}
