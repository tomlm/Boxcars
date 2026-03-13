namespace Boxcars.Engine.Domain;

/// <summary>
/// Observable turn state entity.
/// </summary>
public sealed class Turn : ObservableBase
{
    private Player _activePlayer = null!;
    private int _turnNumber;
    private TurnPhase _phase;
    private DiceResult? _diceResult;
    private int _movementAllowance;
    private int _movementRemaining;
    private bool _bonusRollAvailable;
    private ArrivalResolution? _arrivalResolution;
    private int _pendingFeeAmount;
    private int? _selectedRailroadForSaleIndex;
    private ForcedSaleState? _forcedSaleState;
    private AuctionState? _auctionState;

    /// <summary>Whose turn it is.</summary>
    public Player ActivePlayer
    {
        get => _activePlayer;
        internal set => SetField(ref _activePlayer, value);
    }

    /// <summary>Current turn number (1-based).</summary>
    public int TurnNumber
    {
        get => _turnNumber;
        internal set => SetField(ref _turnNumber, value);
    }

    /// <summary>Current phase of the turn.</summary>
    public TurnPhase Phase
    {
        get => _phase;
        internal set => SetField(ref _phase, value);
    }

    /// <summary>Result of last dice roll.</summary>
    public DiceResult? DiceResult
    {
        get => _diceResult;
        internal set => SetField(ref _diceResult, value);
    }

    /// <summary>Total movement available for the current move window.</summary>
    public int MovementAllowance
    {
        get => _movementAllowance;
        internal set => SetField(ref _movementAllowance, value);
    }

    /// <summary>Mileposts left to move this roll.</summary>
    public int MovementRemaining
    {
        get => _movementRemaining;
        internal set => SetField(ref _movementRemaining, value);
    }

    /// <summary>Whether a bonus roll can/must be taken.</summary>
    public bool BonusRollAvailable
    {
        get => _bonusRollAvailable;
        internal set => SetField(ref _bonusRollAvailable, value);
    }

    /// <summary>Resolved arrival details that remain visible until the turn is completed.</summary>
    public ArrivalResolution? ArrivalResolution
    {
        get => _arrivalResolution;
        internal set => SetField(ref _arrivalResolution, value);
    }

    /// <summary>Authoritative fee amount still owed when UseFees is active.</summary>
    public int PendingFeeAmount
    {
        get => _pendingFeeAmount;
        internal set => SetField(ref _pendingFeeAmount, value);
    }

    /// <summary>The currently selected railroad index for forced sale, if any.</summary>
    public int? SelectedRailroadForSaleIndex
    {
        get => _selectedRailroadForSaleIndex;
        internal set => SetField(ref _selectedRailroadForSaleIndex, value);
    }

    /// <summary>Current forced-sale state while resolving insufficient funds.</summary>
    public ForcedSaleState? ForcedSaleState
    {
        get => _forcedSaleState;
        internal set => SetField(ref _forcedSaleState, value);
    }

    /// <summary>Current auction state, if a railroad auction is in progress.</summary>
    public AuctionState? AuctionState
    {
        get => _auctionState;
        internal set => SetField(ref _auctionState, value);
    }

    /// <summary>Railroad indices used this turn (for use fee calculation).</summary>
    internal HashSet<int> RailroadsRiddenThisTurn { get; } = new();

    public Turn()
    {
        _turnNumber = 1;
        _phase = TurnPhase.DrawDestination;
    }
}

internal static class TurnBonusExtensions
{
    public static bool WhiteDiceAreCleared(this Turn turn)
    {
        return turn.DiceResult?.WhiteDice is { Length: >= 2 } whiteDice
            && whiteDice.All(value => value == 0);
    }
}

public sealed class ArrivalResolution
{
    public int PlayerIndex { get; init; } = -1;
    public string DestinationCityName { get; init; } = string.Empty;
    public int PayoutAmount { get; init; }
    public int CashAfterPayout { get; init; }
    public bool PurchaseOpportunityAvailable { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class ForcedSaleState
{
    public int AmountOwed { get; init; }
    public int CashBeforeFees { get; init; }
    public int CashAfterLastSale { get; init; }
    public int SalesCompletedCount { get; init; }
    public bool CanPayNow { get; init; }
    public bool EliminationTriggered { get; init; }
}

public enum AuctionParticipantAction
{
    None,
    Bid,
    Pass,
    DropOut,
    AutoDropOut
}

public enum AuctionStatus
{
    Open,
    Awarded,
    BankFallback
}

public sealed class AuctionParticipant
{
    public int PlayerIndex { get; init; } = -1;
    public string PlayerName { get; init; } = string.Empty;
    public int CashOnHand { get; init; }
    public int? LastBidAmount { get; init; }
    public bool IsEligible { get; init; }
    public bool HasDroppedOut { get; init; }
    public bool HasPassedThisRound { get; init; }
    public AuctionParticipantAction LastAction { get; init; }
}

public sealed class AuctionState
{
    public int RailroadIndex { get; init; } = -1;
    public string RailroadName { get; init; } = string.Empty;
    public int SellerPlayerIndex { get; init; } = -1;
    public string SellerPlayerName { get; init; } = string.Empty;
    public int StartingPrice { get; init; }
    public int CurrentBid { get; init; }
    public int? LastBidderPlayerIndex { get; init; }
    public int? CurrentBidderPlayerIndex { get; init; }
    public int RoundNumber { get; init; } = 1;
    public int ConsecutiveNoBidTurnCount { get; init; }
    public AuctionStatus Status { get; init; } = AuctionStatus.Open;
    public IReadOnlyList<AuctionParticipant> Participants { get; init; } = [];
}
