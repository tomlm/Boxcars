using Boxcars.Engine.Domain;
using Boxcars.Data;

namespace Boxcars.GameEngine;

public enum PlayerActionKind
{
    ChooseHomeCity,
    ResolveHomeSwap,
    PickDestination,
    Declare,
    ChooseDestinationRegion,
    RollDice,
    ChooseRoute,
    Move,
    PurchaseRailroad,
    StartAuction,
    Bid,
    AuctionPass,
    AuctionDropOut,
    SellRailroad,
    BuyEngine,
    DeclinePurchase,
    EndTurn
}

public abstract record PlayerAction
{
    public required string PlayerId { get; init; }
    public string ActorUserId { get; init; } = string.Empty;
    public int? PlayerIndex { get; init; }
    public BotRecordedActionMetadata? BotMetadata { get; init; }
    public bool IsServerAuthoredAiAction => BotMetadata is not null
        && string.Equals(ActorUserId, BotOptions.DefaultServerActorUserId, StringComparison.OrdinalIgnoreCase);
    public abstract PlayerActionKind Kind { get; }
    public DateTimeOffset EnqueuedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record PickDestinationAction : PlayerAction
{
    public override PlayerActionKind Kind => PlayerActionKind.PickDestination;
}

public sealed record ChooseHomeCityAction : PlayerAction
{
    public required string SelectedCityName { get; init; }

    public override PlayerActionKind Kind => PlayerActionKind.ChooseHomeCity;
}

public sealed record ResolveHomeSwapAction : PlayerAction
{
    public required bool SwapHomeAndDestination { get; init; }

    public override PlayerActionKind Kind => PlayerActionKind.ResolveHomeSwap;
}

public sealed record DeclareAction : PlayerAction
{
    public override PlayerActionKind Kind => PlayerActionKind.Declare;
}

public sealed record ChooseDestinationRegionAction : PlayerAction
{
    public required string SelectedRegionCode { get; init; }

    public override PlayerActionKind Kind => PlayerActionKind.ChooseDestinationRegion;
}

public sealed record RollDiceAction : PlayerAction
{
    public required int WhiteDieOne { get; init; }
    public required int WhiteDieTwo { get; init; }
    public int? RedDie { get; init; }
    public override PlayerActionKind Kind => PlayerActionKind.RollDice;
}

public sealed record ChooseRouteAction : PlayerAction
{
    public IReadOnlyList<string> RouteNodeIds { get; init; } = [];
    public IReadOnlyList<string> RouteSegmentKeys { get; init; } = [];
    public override PlayerActionKind Kind => PlayerActionKind.ChooseRoute;
}

public sealed record MoveAction : PlayerAction
{
    public IReadOnlyList<string> PointsTaken { get; init; } = [];
    public IReadOnlyList<string> SelectedSegmentKeys { get; init; } = [];
    public override PlayerActionKind Kind => PlayerActionKind.Move;
}

public sealed record PurchaseRailroadAction : PlayerAction
{
    public required int RailroadIndex { get; init; }
    public required int AmountPaid { get; init; }
    public override PlayerActionKind Kind => PlayerActionKind.PurchaseRailroad;
}

public sealed record StartAuctionAction : PlayerAction
{
    public required int RailroadIndex { get; init; }
    public override PlayerActionKind Kind => PlayerActionKind.StartAuction;
}

public sealed record BidAction : PlayerAction
{
    public required int RailroadIndex { get; init; }
    public required int AmountBid { get; init; }
    public override PlayerActionKind Kind => PlayerActionKind.Bid;
}

public sealed record AuctionPassAction : PlayerAction
{
    public required int RailroadIndex { get; init; }
    public override PlayerActionKind Kind => PlayerActionKind.AuctionPass;
}

public sealed record AuctionDropOutAction : PlayerAction
{
    public required int RailroadIndex { get; init; }
    public override PlayerActionKind Kind => PlayerActionKind.AuctionDropOut;
}

public sealed record SellRailroadAction : PlayerAction
{
    public required int RailroadIndex { get; init; }
    public int AmountReceived { get; init; }
    public override PlayerActionKind Kind => PlayerActionKind.SellRailroad;
}

public sealed record BuyEngineAction : PlayerAction
{
    public required LocomotiveType EngineType { get; init; }
    public required int AmountPaid { get; init; }
    public override PlayerActionKind Kind => PlayerActionKind.BuyEngine;
}

public sealed record DeclinePurchaseAction : PlayerAction
{
    public override PlayerActionKind Kind => PlayerActionKind.DeclinePurchase;
}

public sealed record EndTurnAction : PlayerAction
{
    public override PlayerActionKind Kind => PlayerActionKind.EndTurn;
}
