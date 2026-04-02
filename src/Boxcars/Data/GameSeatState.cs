using Azure;
using Boxcars.Engine.Persistence;

namespace Boxcars.Data;

public class GameSeatState
{
    public string GameId { get; set; } = string.Empty;
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public int SeatIndex { get; set; }
    public string PlayerUserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public PlayerControlState Control { get; set; } = new();

    public string ControllerMode
    {
        get => Control.ControllerMode;
        set => Control.ControllerMode = value;
    }

    public string ControllerUserId
    {
        get => Control.ControllerUserId;
        set => Control.ControllerUserId = value;
    }

    public int? AuctionPlanTurnNumber
    {
        get => Control.AuctionPlanTurnNumber;
        set => Control.AuctionPlanTurnNumber = value;
    }

    public int? AuctionPlanRailroadIndex
    {
        get => Control.AuctionPlanRailroadIndex;
        set => Control.AuctionPlanRailroadIndex = value;
    }

    public int? AuctionPlanStartingPrice
    {
        get => Control.AuctionPlanStartingPrice;
        set => Control.AuctionPlanStartingPrice = value;
    }

    public int? AuctionPlanMaximumBid
    {
        get => Control.AuctionPlanMaximumBid;
        set => Control.AuctionPlanMaximumBid = value;
    }

    public DateTimeOffset? BotControlActivatedUtc
    {
        get => Control.BotControlActivatedUtc;
        set => Control.BotControlActivatedUtc = value;
    }

    public DateTimeOffset? BotControlClearedUtc
    {
        get => Control.BotControlClearedUtc;
        set => Control.BotControlClearedUtc = value;
    }

    public string BotControlStatus
    {
        get => Control.BotControlStatus;
        set => Control.BotControlStatus = value;
    }

    public string BotControlClearReason
    {
        get => Control.BotControlClearReason;
        set => Control.BotControlClearReason = value;
    }
}

public static class GameSeatStateProjection
{
    public static IReadOnlyList<GameSeatState> BuildStates(GameEntity? game, GameState? snapshot)
    {
        if (game is null || game.Seats.Count == 0)
        {
            return [];
        }

        return game.Seats
            .OrderBy(seat => seat.SeatIndex)
            .Select(seat => new GameSeatState
            {
                GameId = game.GameId,
                PartitionKey = game.GameId,
                RowKey = $"PLAYER_{seat.SeatIndex:D2}",
                SeatIndex = seat.SeatIndex,
                PlayerUserId = seat.PlayerUserId,
                DisplayName = seat.DisplayName,
                Color = seat.Color,
                Control = snapshot is not null && seat.SeatIndex >= 0 && seat.SeatIndex < snapshot.Players.Count
                    ? CloneControl(snapshot.Players[seat.SeatIndex].Control)
                    : new PlayerControlState()
            })
            .ToList();
    }

    public static IReadOnlyList<GameSeatState> BuildTransientStates(GameEntity? game, GameState? snapshot)
    {
        return BuildStates(game, snapshot);
    }

    public static IReadOnlyList<GamePlayerSelection> BuildSeatSelections(IReadOnlyList<GameSeatState> seatStates)
    {
        return seatStates
            .OrderBy(seatState => seatState.SeatIndex)
            .Select(seatState => new GamePlayerSelection
            {
                UserId = seatState.PlayerUserId,
                DisplayName = seatState.DisplayName,
                Color = seatState.Color
            })
            .ToList();
    }

    public static GameSeatState Clone(GameSeatState seatState)
    {
        return new GameSeatState
        {
            SeatIndex = seatState.SeatIndex,
            PlayerUserId = seatState.PlayerUserId,
            DisplayName = seatState.DisplayName,
            Color = seatState.Color,
            Control = CloneControl(seatState.Control)
        };
    }

    public static PlayerControlState CloneControl(PlayerControlState? controlState)
    {
        if (controlState is null)
        {
            return new PlayerControlState();
        }

        return new PlayerControlState
        {
            ControllerMode = controlState.ControllerMode,
            ControllerUserId = controlState.ControllerUserId,
            AuctionPlanTurnNumber = controlState.AuctionPlanTurnNumber,
            AuctionPlanRailroadIndex = controlState.AuctionPlanRailroadIndex,
            AuctionPlanStartingPrice = controlState.AuctionPlanStartingPrice,
            AuctionPlanMaximumBid = controlState.AuctionPlanMaximumBid,
            BotControlActivatedUtc = controlState.BotControlActivatedUtc,
            BotControlClearedUtc = controlState.BotControlClearedUtc,
            BotControlStatus = controlState.BotControlStatus,
            BotControlClearReason = controlState.BotControlClearReason
        };
    }

    public static PlayerControlState BuildControlState(GameSeatState? seatState)
    {
        return CloneControl(seatState?.Control);
    }
}
