using Azure;
using Azure.Data.Tables;

namespace Boxcars.Data;

public sealed class GamePlayerStateEntity : ITableEntity
{
    private const char DestinationLogSeparator = '|';

    public const string RowKeyPrefix = "PLAYER_";
    public const string RowKeyExclusiveUpperBound = "PLAYER`";

    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string GameId { get; set; } = string.Empty;
    public int SeatIndex { get; set; }
    public string PlayerUserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;

    public string ControllerMode { get; set; } = string.Empty;
    public string ControllerUserId { get; set; } = string.Empty;
    public string BotDefinitionId { get; set; } = string.Empty;
    public int? AuctionPlanTurnNumber { get; set; }
    public int? AuctionPlanRailroadIndex { get; set; }
    public int? AuctionPlanStartingPrice { get; set; }
    public int? AuctionPlanMaximumBid { get; set; }
    public DateTimeOffset? BotControlActivatedUtc { get; set; }
    public DateTimeOffset? BotControlClearedUtc { get; set; }
    public string BotControlStatus { get; set; } = string.Empty;
    public string BotControlClearReason { get; set; } = string.Empty;

    public int TurnsTaken { get; set; }
    public int FreightTurnCount { get; set; }
    public int FreightRollTotal { get; set; }
    public int ExpressTurnCount { get; set; }
    public int ExpressRollTotal { get; set; }
    public int SuperchiefTurnCount { get; set; }
    public int SuperchiefRollTotal { get; set; }
    public int BonusRollCount { get; set; }
    public int BonusRollTotal { get; set; }
    public int TotalPayoffsCollected { get; set; }
    public int TotalFeesPaid { get; set; }
    public int TotalFeesCollected { get; set; }
    public int TotalRailroadFaceValuePurchased { get; set; }
    public int TotalRailroadAmountPaid { get; set; }
    public int AuctionWins { get; set; }
    public int AuctionBidsPlaced { get; set; }
    public int RailroadsPurchasedCount { get; set; }
    public int RailroadsAuctionedCount { get; set; }
    public int RailroadsSoldToBankCount { get; set; }
    public int DestinationCount { get; set; }
    public int UnfriendlyDestinationCount { get; set; }
    public string DestinationLog { get; set; } = string.Empty;

    public IReadOnlyList<string> GetDestinationLogEntries()
    {
        return string.IsNullOrWhiteSpace(DestinationLog)
            ? []
            : DestinationLog
                .Split(DestinationLogSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();
    }

    public void SetDestinationLogEntries(IEnumerable<string> entries)
    {
        DestinationLog = string.Join(
            DestinationLogSeparator,
            entries.Where(entry => !string.IsNullOrWhiteSpace(entry)).Select(entry => entry.Trim()));
    }

    public void AppendDestinationLogEntry(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(DestinationLog))
        {
            DestinationLog = entry.Trim();
            return;
        }

        DestinationLog = string.Concat(DestinationLog, DestinationLogSeparator, entry.Trim());
    }

    public static string BuildRowKey(int seatIndex)
    {
        return $"{RowKeyPrefix}{seatIndex:D2}";
    }

    public static GamePlayerStateEntity Create(string gameId, int seatIndex, GamePlayerSelection selection)
    {
        return new GamePlayerStateEntity
        {
            PartitionKey = gameId,
            RowKey = BuildRowKey(seatIndex),
            GameId = gameId,
            SeatIndex = seatIndex,
            PlayerUserId = selection.UserId,
            DisplayName = selection.DisplayName,
            Color = selection.Color
        };
    }
}

public static class GamePlayerStateProjection
{
    public static IReadOnlyList<GamePlayerSelection> BuildPlayerSelections(IReadOnlyList<GamePlayerStateEntity> playerStates)
    {
        return playerStates
            .OrderBy(playerState => playerState.SeatIndex)
            .Select(playerState => new GamePlayerSelection
            {
                UserId = playerState.PlayerUserId,
                DisplayName = playerState.DisplayName,
                Color = playerState.Color
            })
            .ToList();
    }

    public static GamePlayerStateEntity ResetStatistics(GamePlayerStateEntity playerState)
    {
        var updatedPlayerState = Clone(playerState);
        updatedPlayerState.TurnsTaken = 0;
        updatedPlayerState.FreightTurnCount = 0;
        updatedPlayerState.FreightRollTotal = 0;
        updatedPlayerState.ExpressTurnCount = 0;
        updatedPlayerState.ExpressRollTotal = 0;
        updatedPlayerState.SuperchiefTurnCount = 0;
        updatedPlayerState.SuperchiefRollTotal = 0;
        updatedPlayerState.BonusRollCount = 0;
        updatedPlayerState.BonusRollTotal = 0;
        updatedPlayerState.TotalPayoffsCollected = 0;
        updatedPlayerState.TotalFeesPaid = 0;
        updatedPlayerState.TotalFeesCollected = 0;
        updatedPlayerState.TotalRailroadFaceValuePurchased = 0;
        updatedPlayerState.TotalRailroadAmountPaid = 0;
        updatedPlayerState.AuctionWins = 0;
        updatedPlayerState.AuctionBidsPlaced = 0;
        updatedPlayerState.RailroadsPurchasedCount = 0;
        updatedPlayerState.RailroadsAuctionedCount = 0;
        updatedPlayerState.RailroadsSoldToBankCount = 0;
        updatedPlayerState.DestinationCount = 0;
        updatedPlayerState.UnfriendlyDestinationCount = 0;
        updatedPlayerState.DestinationLog = string.Empty;
        return updatedPlayerState;
    }

    public static GamePlayerStateEntity Clone(GamePlayerStateEntity playerState)
    {
        return new GamePlayerStateEntity
        {
            PartitionKey = playerState.PartitionKey,
            RowKey = playerState.RowKey,
            Timestamp = playerState.Timestamp,
            ETag = playerState.ETag,
            GameId = playerState.GameId,
            SeatIndex = playerState.SeatIndex,
            PlayerUserId = playerState.PlayerUserId,
            DisplayName = playerState.DisplayName,
            Color = playerState.Color,
            ControllerMode = playerState.ControllerMode,
            ControllerUserId = playerState.ControllerUserId,
            BotDefinitionId = playerState.BotDefinitionId,
            AuctionPlanTurnNumber = playerState.AuctionPlanTurnNumber,
            AuctionPlanRailroadIndex = playerState.AuctionPlanRailroadIndex,
            AuctionPlanStartingPrice = playerState.AuctionPlanStartingPrice,
            AuctionPlanMaximumBid = playerState.AuctionPlanMaximumBid,
            BotControlActivatedUtc = playerState.BotControlActivatedUtc,
            BotControlClearedUtc = playerState.BotControlClearedUtc,
            BotControlStatus = playerState.BotControlStatus,
            BotControlClearReason = playerState.BotControlClearReason,
            TurnsTaken = playerState.TurnsTaken,
            FreightTurnCount = playerState.FreightTurnCount,
            FreightRollTotal = playerState.FreightRollTotal,
            ExpressTurnCount = playerState.ExpressTurnCount,
            ExpressRollTotal = playerState.ExpressRollTotal,
            SuperchiefTurnCount = playerState.SuperchiefTurnCount,
            SuperchiefRollTotal = playerState.SuperchiefRollTotal,
            BonusRollCount = playerState.BonusRollCount,
            BonusRollTotal = playerState.BonusRollTotal,
            TotalPayoffsCollected = playerState.TotalPayoffsCollected,
            TotalFeesPaid = playerState.TotalFeesPaid,
            TotalFeesCollected = playerState.TotalFeesCollected,
            TotalRailroadFaceValuePurchased = playerState.TotalRailroadFaceValuePurchased,
            TotalRailroadAmountPaid = playerState.TotalRailroadAmountPaid,
            AuctionWins = playerState.AuctionWins,
            AuctionBidsPlaced = playerState.AuctionBidsPlaced,
            RailroadsPurchasedCount = playerState.RailroadsPurchasedCount,
            RailroadsAuctionedCount = playerState.RailroadsAuctionedCount,
            RailroadsSoldToBankCount = playerState.RailroadsSoldToBankCount,
            DestinationCount = playerState.DestinationCount,
            UnfriendlyDestinationCount = playerState.UnfriendlyDestinationCount,
            DestinationLog = playerState.DestinationLog
        };
    }
}