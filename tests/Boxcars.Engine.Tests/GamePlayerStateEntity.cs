using Azure;
using Azure.Data.Tables;

namespace Boxcars.Data;

// Test-only compatibility shim for legacy seat-state tests.
public sealed class GamePlayerStateEntity : GameSeatState, ITableEntity
{
    public const string RowKeyPrefix = "PLAYER_";
    public const string RowKeyExclusiveUpperBound = "PLAYER`";

    public string BotDefinitionId { get; set; } = string.Empty;
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
            AuctionPlanTurnNumber = playerState.AuctionPlanTurnNumber,
            AuctionPlanRailroadIndex = playerState.AuctionPlanRailroadIndex,
            AuctionPlanStartingPrice = playerState.AuctionPlanStartingPrice,
            AuctionPlanMaximumBid = playerState.AuctionPlanMaximumBid,
            BotControlActivatedUtc = playerState.BotControlActivatedUtc,
            BotControlClearedUtc = playerState.BotControlClearedUtc,
            BotControlStatus = playerState.BotControlStatus,
            BotControlClearReason = playerState.BotControlClearReason,
            BotDefinitionId = playerState.BotDefinitionId,
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
}
