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

    /// <summary>Railroad indices used this turn (for use fee calculation).</summary>
    internal HashSet<int> RailroadsRiddenThisTurn { get; } = new();

    public Turn()
    {
        _turnNumber = 1;
        _phase = TurnPhase.DrawDestination;
    }
}
