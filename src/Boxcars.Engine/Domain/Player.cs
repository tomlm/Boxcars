using System.Collections.ObjectModel;
using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Persistence;

namespace Boxcars.Engine.Domain;

/// <summary>
/// Observable player entity with all mutable game state.
/// </summary>
public sealed class Player : ObservableBase
{
    private int _cash;
    private CityDefinition _currentCity = null!;
    private CityDefinition? _destination;
    private CityDefinition? _tripOriginCity;
    private Route? _activeRoute;
    private CityDefinition? _alternateDestination;
    private LocomotiveType _locomotiveType;
    private bool _isActive;
    private bool _isBankrupt;
    private bool _hasDeclared;
    private bool _hasResolvedHomeCityChoice;
    private bool _hasResolvedHomeSwap;
    private bool _pendingImmediateArrival;

    /// <summary>Player display name (immutable).</summary>
    public string Name { get; }

    /// <summary>Player index in the game (0-based, immutable).</summary>
    public int Index { get; }

    /// <summary>Current cash in dollars.</summary>
    public int Cash
    {
        get => _cash;
        internal set => SetField(ref _cash, value);
    }

    /// <summary>Randomly assigned starting city (immutable after init).</summary>
    public CityDefinition HomeCity { get; internal set; } = null!;

    /// <summary>Current location on the map.</summary>
    public CityDefinition CurrentCity
    {
        get => _currentCity;
        internal set => SetField(ref _currentCity, value);
    }

    /// <summary>Current destination (null when between destinations).</summary>
    public CityDefinition? Destination
    {
        get => _destination;
        internal set => SetField(ref _destination, value);
    }

    /// <summary>Starting city for the current trip, used for payout calculation.</summary>
    public CityDefinition? TripOriginCity
    {
        get => _tripOriginCity;
        internal set => SetField(ref _tripOriginCity, value);
    }

    /// <summary>Currently planned route to destination.</summary>
    public Route? ActiveRoute
    {
        get => _activeRoute;
        internal set => SetField(ref _activeRoute, value);
    }

    public CityDefinition? AlternateDestination
    {
        get => _alternateDestination;
        internal set => SetField(ref _alternateDestination, value);
    }

    /// <summary>Railroads owned by this player.</summary>
    public ObservableCollection<Railroad> OwnedRailroads { get; } = new();

    /// <summary>Current locomotive type.</summary>
    public LocomotiveType LocomotiveType
    {
        get => _locomotiveType;
        internal set => SetField(ref _locomotiveType, value);
    }

    /// <summary>True if still in the game (not bankrupt).</summary>
    public bool IsActive
    {
        get => _isActive;
        internal set => SetField(ref _isActive, value);
    }

    /// <summary>True if eliminated by bankruptcy.</summary>
    public bool IsBankrupt
    {
        get => _isBankrupt;
        internal set => SetField(ref _isBankrupt, value);
    }

    /// <summary>True if the player has declared (end-game mechanic).</summary>
    public bool HasDeclared
    {
        get => _hasDeclared;
        internal set => SetField(ref _hasDeclared, value);
    }

    public bool HasResolvedHomeCityChoice
    {
        get => _hasResolvedHomeCityChoice;
        internal set => SetField(ref _hasResolvedHomeCityChoice, value);
    }

    public bool HasResolvedHomeSwap
    {
        get => _hasResolvedHomeSwap;
        internal set => SetField(ref _hasResolvedHomeSwap, value);
    }

    public bool PendingImmediateArrival
    {
        get => _pendingImmediateArrival;
        internal set => SetField(ref _pendingImmediateArrival, value);
    }

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
    public List<string> DestinationLogEntries { get; } = new();

    /// <summary>Track segments used since last destination arrival (non-reuse rule).</summary>
    internal HashSet<SegmentKey> UsedSegments { get; } = new();

    /// <summary>Railroads the player may continue using at the public rate until they leave them.</summary>
    internal HashSet<int> GrandfatheredRailroadIndices { get; } = new();

    /// <summary>Current position as a node ID on the route graph (regionIndex:dotIndex).</summary>
    internal string? CurrentNodeId { get; set; }

    /// <summary>Current index along the active route's NodeIds list.</summary>
    internal int RouteProgressIndex { get; set; }

    public Player(string name, int index)
        : this(name, index, GameSettings.Default)
    {
    }

    public Player(string name, int index, GameSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Name = name;
        Index = index;
        _cash = settings.StartingCash;
        _locomotiveType = settings.StartEngine;
        _isActive = true;
        _isBankrupt = false;
        _hasDeclared = false;
        _hasResolvedHomeCityChoice = !settings.HomeCityChoice;
        _hasResolvedHomeSwap = !settings.HomeSwapping;
        _pendingImmediateArrival = false;
    }
}
