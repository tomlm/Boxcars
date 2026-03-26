namespace Boxcars.Data;

public sealed class ForcedSalePhaseModel
{
    public int PlayerIndex { get; init; } = -1;

    public string PlayerName { get; init; } = string.Empty;

    public int AmountOwed { get; init; }

    public int CashOnHand { get; init; }

    public int FeeShortfall { get; init; }

    public IReadOnlyList<SaleCandidateModel> SaleCandidates { get; init; } = [];

    public int? SelectedRailroadIndex { get; init; }

    public NetworkCoverageSnapshot? CurrentNetwork { get; init; }

    public NetworkCoverageSnapshot? ProjectedNetworkAfterSale { get; init; }

    public NetworkTabModel NetworkTab { get; init; } = new();

    public ForcedSaleAuctionStateModel? AuctionState { get; init; }

    public bool CanSellToBank { get; init; }

    public bool CanStartAuction { get; init; }

    public bool CanResolveFees { get; init; }
}

public sealed class SaleCandidateModel
{
    public int RailroadIndex { get; init; } = -1;

    public string RailroadName { get; init; } = string.Empty;

    public string ShortName { get; init; } = string.Empty;

    public int OriginalPurchasePrice { get; init; }

    public int BankSalePrice { get; init; }

    public bool IsSelected { get; init; }
}

public sealed class NetworkTabModel
{
    public string PlayerName { get; init; } = string.Empty;

    public decimal CurrentAccessPercent { get; init; }

    public decimal CurrentMonopolyPercent { get; init; }

    public IReadOnlyList<NetworkRailroadSummaryModel> RailroadSummaries { get; init; } = [];

    public RailroadOverlayInfo? SelectedRailroadImpact { get; init; }
}

public sealed class NetworkRailroadSummaryModel
{
    public int RailroadIndex { get; init; } = -1;

    public string RailroadName { get; init; } = string.Empty;

    public int OriginalPurchasePrice { get; init; }

    public int BankSalePrice { get; init; }

    public decimal AccessPercentAfterSale { get; init; }

    public decimal MonopolyPercentAfterSale { get; init; }

    public decimal AccessDeltaPercentAfterSale { get; init; }

    public decimal MonopolyDeltaPercentAfterSale { get; init; }
}

public enum AuctionParticipantActionModel
{
    None,
    Bid,
    Pass,
    DropOut,
    AutoDropOut
}

public enum ForcedSaleAuctionStatusModel
{
    Open,
    Awarded,
    BankFallback
}

public sealed class ForcedSaleAuctionStateModel
{
    public int RailroadIndex { get; init; } = -1;

    public string RailroadName { get; init; } = string.Empty;

    public int SellerPlayerIndex { get; init; } = -1;

    public string SellerPlayerName { get; init; } = string.Empty;

    public int StartingPrice { get; init; }

    public int CurrentBid { get; init; }

    public int? LastBidderPlayerIndex { get; init; }

    public int? CurrentBidderPlayerIndex { get; init; }

    public string CurrentBidderPlayerName { get; init; } = string.Empty;

    public int MinimumBid { get; init; }

    public int RoundNumber { get; init; } = 1;

    public int ConsecutiveNoBidTurnCount { get; init; }

    public ForcedSaleAuctionStatusModel Status { get; init; } = ForcedSaleAuctionStatusModel.Open;

    public bool CanCurrentUserAct { get; init; }

    public IReadOnlyList<AuctionParticipantModel> Participants { get; init; } = [];
}

public sealed class AuctionParticipantModel
{
    public int PlayerIndex { get; init; } = -1;

    public string PlayerName { get; init; } = string.Empty;

    public int CashOnHand { get; init; }

    public int? LastBidAmount { get; init; }

    public bool IsEligible { get; init; }

    public bool HasDroppedOut { get; init; }

    public bool HasPassedThisRound { get; init; }

    public AuctionParticipantActionModel LastAction { get; init; }
}
