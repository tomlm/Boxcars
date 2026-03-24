using System.Text.Json.Serialization;

namespace Boxcars.Engine.Persistence;

/// <summary>
/// Serialization snapshot DTO capturing all mutable game state.
/// No interfaces, no base classes, no events — pure data.
/// </summary>
public sealed class GameState
{
    public string GameStatus { get; set; } = "NotStarted";
    public int TurnNumber { get; set; }
    public int ActivePlayerIndex { get; set; }
    public bool AllRailroadsSold { get; set; }
    public int? WinnerIndex { get; set; }
    public List<PlayerState> Players { get; set; } = new();
    public Dictionary<int, int?> RailroadOwnership { get; set; } = new();
    public TurnState Turn { get; set; } = new();
}

public sealed class PlayerState
{
    public string Name { get; set; } = string.Empty;
    public int Cash { get; set; }
    public PlayerControlState Control { get; set; } = new();
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
    public List<FeePaidToPlayerState> FeesPaidToPlayers { get; set; } = new();
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
    public List<string> DestinationLogEntries { get; set; } = new();
    public string HomeCityName { get; set; } = string.Empty;
    public string CurrentCityName { get; set; } = string.Empty;
    public string? TripStartCityName { get; set; }
    public string? DestinationCityName { get; set; }
    public string? AlternateDestinationCityName { get; set; }
    public string LocomotiveType { get; set; } = "Freight";
    public bool IsActive { get; set; }
    public bool IsBankrupt { get; set; }
    public bool HasDeclared { get; set; }
    public bool HasResolvedHomeCityChoice { get; set; }
    public bool HasResolvedHomeSwap { get; set; }
    public bool PendingImmediateArrival { get; set; }
    public List<int> OwnedRailroadIndices { get; set; } = new();
    public RouteState? ActiveRoute { get; set; }
    public List<string> SelectedRouteNodeIds { get; set; } = new();
    public List<string> SelectedRouteSegmentKeys { get; set; } = new();
    public List<string> UsedSegments { get; set; } = new();
    public List<int> GrandfatheredRailroadIndices { get; set; } = new();
    public string? CurrentNodeId { get; set; }
    public int RouteProgressIndex { get; set; }
}

public sealed class FeePaidToPlayerState
{
    public int PlayerIndex { get; set; }
    public int Amount { get; set; }
}

public sealed class PlayerControlState
{
    public string ControllerMode { get; set; } = string.Empty;
    public int? AuctionPlanTurnNumber { get; set; }
    public int? AuctionPlanRailroadIndex { get; set; }
    public int? AuctionPlanStartingPrice { get; set; }
    public int? AuctionPlanMaximumBid { get; set; }
    public DateTimeOffset? BotControlActivatedUtc { get; set; }
    public DateTimeOffset? BotControlClearedUtc { get; set; }
    public string BotControlStatus { get; set; } = string.Empty;
    public string BotControlClearReason { get; set; } = string.Empty;
}

public sealed class RouteState
{
    public List<string> NodeIds { get; set; } = new();
    public List<RouteSegmentState> Segments { get; set; } = new();
    public int TotalCost { get; set; }
}

public sealed class RouteSegmentState
{
    public string FromNodeId { get; set; } = string.Empty;
    public string ToNodeId { get; set; } = string.Empty;
    public int RailroadIndex { get; set; }
}

public sealed class TurnState
{
    public string Phase { get; set; } = "DrawDestination";
    public DiceResultState? DiceResult { get; set; }
    public int MovementAllowance { get; set; }
    public int MovementRemaining { get; set; }
    public bool BonusRollAvailable { get; set; }
    public int PendingFeeAmount { get; set; }
    public int? SelectedRailroadForSaleIndex { get; set; }
    public List<int> RailroadsRiddenThisTurn { get; set; } = new();
    public List<int> RailroadsRequiringFullOwnerRateThisTurn { get; set; } = new();
    public ArrivalResolutionState? ArrivalResolution { get; set; }
    public ForcedSaleTurnState? ForcedSale { get; set; }
    public AuctionTurnState? Auction { get; set; }
    public PendingRegionChoiceTurnState? PendingRegionChoice { get; set; }
    public PendingHomeCityChoiceTurnState? PendingHomeCityChoice { get; set; }
    public PendingHomeSwapTurnState? PendingHomeSwap { get; set; }
}

public sealed class PendingRegionChoiceTurnState
{
    public int PlayerIndex { get; set; } = -1;
    public string CurrentCityName { get; set; } = string.Empty;
    public string CurrentRegionCode { get; set; } = string.Empty;
    public string TriggeredByInitialRegionCode { get; set; } = string.Empty;
    public string AssignmentKind { get; set; } = nameof(global::Boxcars.Engine.Domain.PendingDestinationAssignmentKind.NormalDestination);
    public List<string> EligibleRegionCodes { get; set; } = new();
    public Dictionary<string, int> EligibleCityCountsByRegion { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class PendingHomeCityChoiceTurnState
{
    public int PlayerIndex { get; set; } = -1;
    public string RegionCode { get; set; } = string.Empty;
    public string RegionName { get; set; } = string.Empty;
    public string CurrentHomeCityName { get; set; } = string.Empty;
    public List<string> EligibleCityNames { get; set; } = new();
}

public sealed class PendingHomeSwapTurnState
{
    public int PlayerIndex { get; set; } = -1;
    public string CurrentHomeCityName { get; set; } = string.Empty;
    public string FirstDestinationCityName { get; set; } = string.Empty;
}

public sealed class ForcedSaleTurnState
{
    public int AmountOwed { get; set; }
    public int CashBeforeFees { get; set; }
    public int CashAfterLastSale { get; set; }
    public int SalesCompletedCount { get; set; }
    public bool CanPayNow { get; set; }
    public bool EliminationTriggered { get; set; }
}

public sealed class AuctionTurnState
{
    public int RailroadIndex { get; set; } = -1;
    public string RailroadName { get; set; } = string.Empty;
    public int SellerPlayerIndex { get; set; } = -1;
    public string SellerPlayerName { get; set; } = string.Empty;
    public int StartingPrice { get; set; }
    public int CurrentBid { get; set; }
    public int? LastBidderPlayerIndex { get; set; }
    public int? CurrentBidderPlayerIndex { get; set; }
    public int RoundNumber { get; set; } = 1;
    public int ConsecutiveNoBidTurnCount { get; set; }
    public string Status { get; set; } = "Open";
    public List<AuctionParticipantTurnState> Participants { get; set; } = new();
}

public sealed class AuctionParticipantTurnState
{
    public int PlayerIndex { get; set; } = -1;
    public string PlayerName { get; set; } = string.Empty;
    public int CashOnHand { get; set; }
    public int? LastBidAmount { get; set; }
    public bool IsEligible { get; set; }
    public bool HasDroppedOut { get; set; }
    public bool HasPassedThisRound { get; set; }
    public string LastAction { get; set; } = "None";
}

public sealed class ArrivalResolutionState
{
    public int PlayerIndex { get; set; } = -1;
    public string DestinationCityName { get; set; } = string.Empty;
    public int PayoutAmount { get; set; }
    public int CashAfterPayout { get; set; }
    public bool PurchaseOpportunityAvailable { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class DiceResultState
{
    public int[] WhiteDice { get; set; } = Array.Empty<int>();
    public int? RedDie { get; set; }
}
