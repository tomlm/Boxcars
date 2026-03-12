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
