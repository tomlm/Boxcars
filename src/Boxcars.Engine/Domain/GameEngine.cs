using System.Collections.ObjectModel;
using System.Text.Json;
using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Events;
using Boxcars.Engine.Persistence;

namespace Boxcars.Engine.Domain;

/// <summary>
/// Root observable game engine implementing the Rail Baron game rules.
/// All action methods are synchronous, validate preconditions, and fire notifications on the same call path.
/// </summary>
public sealed class GameEngine : ObservableBase
{
    private GameStatus _gameStatus;
    private Turn _currentTurn = null!;
    private bool _allRailroadsSold;
    private Player? _winner;

    // Map infrastructure
    private readonly IRandomProvider _randomProvider;
    private readonly Dictionary<string, TrainDot> _dotLookup;
    private readonly Dictionary<string, List<RouteGraphEdge>> _adjacency;
    private readonly Dictionary<string, CityDefinition> _cityByNodeId;
    private readonly Dictionary<string, CityDefinition> _cityByName;

    // Railroad purchase prices (based on official Rail Baron rules)
    private static readonly int[] RailroadPrices = new int[]
    {
        // Prices indexed by railroad index (0-based)
        // These are approximate standard Rail Baron prices
        4000, 4000, 8000, 8000, 10000, 10000, 12000, 12000,
        14000, 14000, 16000, 16000, 18000, 18000, 20000, 20000,
        22000, 22000, 24000, 24000, 25000, 25000, 30000, 30000,
        35000, 35000, 40000, 40000
    };

    // Public railroad indices (railroads that cannot be purchased)
    // Typically none in standard Rail Baron, but configurable
    private static readonly HashSet<int> PublicRailroadIndices = new();

    #region Observable Properties

    /// <summary>Overall game state.</summary>
    public GameStatus GameStatus
    {
        get => _gameStatus;
        private set => SetField(ref _gameStatus, value);
    }

    /// <summary>All players in turn order.</summary>
    public ObservableCollection<Player> Players { get; } = new();

    /// <summary>All railroads from map definition.</summary>
    public ObservableCollection<Railroad> Railroads { get; } = new();

    /// <summary>Current turn state.</summary>
    public Turn CurrentTurn
    {
        get => _currentTurn;
        private set => SetField(ref _currentTurn, value);
    }

    /// <summary>True when all non-public railroads are owned.</summary>
    public bool AllRailroadsSold
    {
        get => _allRailroadsSold;
        private set => SetField(ref _allRailroadsSold, value);
    }

    /// <summary>Set when game ends.</summary>
    public Player? Winner
    {
        get => _winner;
        private set => SetField(ref _winner, value);
    }

    /// <summary>Reference to the loaded map definition.</summary>
    public MapDefinition MapDefinition { get; }

    #endregion

    #region Domain Events

    public event EventHandler<DestinationAssignedEventArgs>? DestinationAssigned;
    public event EventHandler<DestinationReachedEventArgs>? DestinationReached;
    public event EventHandler<UsageFeeChargedEventArgs>? UsageFeeCharged;
    public event EventHandler<AuctionStartedEventArgs>? AuctionStarted;
    public event EventHandler<AuctionCompletedEventArgs>? AuctionCompleted;
    public event EventHandler<TurnStartedEventArgs>? TurnStarted;
    public event EventHandler<GameOverEventArgs>? GameOver;
    public event EventHandler<PlayerBankruptEventArgs>? PlayerBankrupt;
    public event EventHandler<LocomotiveUpgradedEventArgs>? LocomotiveUpgraded;

    #endregion

    #region Construction

    /// <summary>
    /// Creates a new game with the given map, players, and random provider.
    /// </summary>
    public GameEngine(MapDefinition mapDefinition, IReadOnlyList<string> playerNames, IRandomProvider randomProvider)
    {
        ArgumentNullException.ThrowIfNull(mapDefinition);
        ArgumentNullException.ThrowIfNull(playerNames);
        ArgumentNullException.ThrowIfNull(randomProvider);

        if (playerNames.Count < 2)
            throw new ArgumentException("At least 2 players are required.", nameof(playerNames));
        if (playerNames.Count > 6)
            throw new ArgumentException("At most 6 players are allowed.", nameof(playerNames));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < playerNames.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(playerNames[i]))
                throw new ArgumentException($"Player name at index {i} is null or empty.", nameof(playerNames));
            if (!seen.Add(playerNames[i]))
                throw new ArgumentException($"Duplicate player name: '{playerNames[i]}'.", nameof(playerNames));
        }

        MapDefinition = mapDefinition;
        _randomProvider = randomProvider;

        // Build map graph
        _dotLookup = mapDefinition.TrainDots.ToDictionary(
            d => NodeKey(d.RegionIndex, d.DotIndex),
            d => d,
            StringComparer.OrdinalIgnoreCase);

        _adjacency = BuildAdjacency(mapDefinition);

        _cityByNodeId = new Dictionary<string, CityDefinition>(StringComparer.OrdinalIgnoreCase);
        _cityByName = new Dictionary<string, CityDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var city in mapDefinition.Cities)
        {
            if (city.MapDotIndex.HasValue)
            {
                var region = mapDefinition.Regions.FirstOrDefault(r => r.Code == city.RegionCode);
                if (region != null)
                {
                    int regionIndex = mapDefinition.Regions.IndexOf(region);
                    var nodeId = NodeKey(regionIndex, city.MapDotIndex.Value);
                    _cityByNodeId[nodeId] = city;
                }
            }
            _cityByName[city.Name] = city;
        }

        // Initialize railroads
        foreach (var rd in mapDefinition.Railroads)
        {
            int price = rd.Index < RailroadPrices.Length ? RailroadPrices[rd.Index] : 10000;
            bool isPublic = PublicRailroadIndices.Contains(rd.Index);
            Railroads.Add(new Railroad(rd, price, isPublic));
        }

        // Initialize players with random home cities
        for (int i = 0; i < playerNames.Count; i++)
        {
            var player = new Player(playerNames[i], i);
            var homeCity = DrawRandomCity();
            player.HomeCity = homeCity;
            player.CurrentCity = homeCity;

            // Set the player's current node ID based on home city
            if (homeCity.MapDotIndex.HasValue)
            {
                var region = mapDefinition.Regions.FirstOrDefault(r => r.Code == homeCity.RegionCode);
                if (region != null)
                {
                    int regionIndex = mapDefinition.Regions.IndexOf(region);
                    player.CurrentNodeId = NodeKey(regionIndex, homeCity.MapDotIndex.Value);
                }
            }

            Players.Add(player);
        }

        // Initialize turn
        _currentTurn = new Turn
        {
            ActivePlayer = Players[0],
            TurnNumber = 1,
            Phase = TurnPhase.DrawDestination
        };

        GameStatus = GameStatus.InProgress;
    }

    /// <summary>
    /// Private constructor for snapshot restoration.
    /// </summary>
    private GameEngine(MapDefinition mapDefinition, IRandomProvider randomProvider)
    {
        MapDefinition = mapDefinition;
        _randomProvider = randomProvider;

        _dotLookup = mapDefinition.TrainDots.ToDictionary(
            d => NodeKey(d.RegionIndex, d.DotIndex),
            d => d,
            StringComparer.OrdinalIgnoreCase);

        _adjacency = BuildAdjacency(mapDefinition);

        _cityByNodeId = new Dictionary<string, CityDefinition>(StringComparer.OrdinalIgnoreCase);
        _cityByName = new Dictionary<string, CityDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var city in mapDefinition.Cities)
        {
            if (city.MapDotIndex.HasValue)
            {
                var region = mapDefinition.Regions.FirstOrDefault(r => r.Code == city.RegionCode);
                if (region != null)
                {
                    int regionIndex = mapDefinition.Regions.IndexOf(region);
                    _cityByNodeId[NodeKey(regionIndex, city.MapDotIndex.Value)] = city;
                }
            }
            _cityByName[city.Name] = city;
        }
    }

    #endregion

    #region Action Methods

    /// <summary>
    /// Draws a destination for the active player using region/city lookup tables.
    /// </summary>
    public CityDefinition DrawDestination()
    {
        EnsureInProgress();
        var player = CurrentTurn.ActivePlayer;

        if (CurrentTurn.Phase != TurnPhase.DrawDestination)
            throw new InvalidOperationException("Not in DrawDestination phase.");
        if (player.Destination != null)
            throw new InvalidOperationException("Player already has a destination.");

        var city = DrawRandomCity();

        // Re-draw if same as current city
        int maxRetries = 20;
        while (string.Equals(city.Name, player.CurrentCity.Name, StringComparison.OrdinalIgnoreCase) && maxRetries-- > 0)
        {
            city = DrawRandomCity();
        }

        player.Destination = city;
        DestinationAssigned?.Invoke(this, new DestinationAssignedEventArgs(player, city));

        // Advance to Roll phase
        CurrentTurn.Phase = TurnPhase.Roll;

        return city;
    }

    /// <summary>
    /// Rolls dice for the active player based on their locomotive type.
    /// </summary>
    public DiceResult RollDice()
    {
        EnsureInProgress();

        if (CurrentTurn.Phase != TurnPhase.Roll)
            throw new InvalidOperationException("Not in Roll phase.");

        var player = CurrentTurn.ActivePlayer;
        DiceResult result;

        if (player.LocomotiveType == LocomotiveType.Superchief)
        {
            // 3d6 — white dice + red die rolled together
            var dice = _randomProvider.RollDiceIndividual(3);
            result = new DiceResult(new[] { dice[0], dice[1] }, dice[2]);
        }
        else
        {
            // 2d6
            var dice = _randomProvider.RollDiceIndividual(2);
            result = new DiceResult(dice);
        }

        CurrentTurn.DiceResult = result;
        CurrentTurn.MovementAllowance = result.Total;
        CurrentTurn.MovementRemaining = result.Total;

        // Check bonus roll eligibility
        bool bonusAvailable = false;
        if (player.LocomotiveType == LocomotiveType.Freight && result.IsDoubles && result.WhiteDice[0] == 6)
        {
            bonusAvailable = true;
        }
        else if (player.LocomotiveType == LocomotiveType.Express && result.IsDoubles)
        {
            bonusAvailable = true;
        }
        // Superchief: no separate bonus (red die already included)

        CurrentTurn.BonusRollAvailable = bonusAvailable;

        // Advance to Move phase
        CurrentTurn.Phase = TurnPhase.Move;

        return result;
    }

    /// <summary>
    /// Moves the active player along their route by the given number of steps.
    /// </summary>
    public void MoveAlongRoute(int steps)
    {
        EnsureInProgress();

        if (CurrentTurn.Phase != TurnPhase.Move)
            throw new InvalidOperationException("Not in Move phase.");
        if (steps <= 0)
            throw new ArgumentException("Steps must be positive.", nameof(steps));
        if (steps > CurrentTurn.MovementRemaining)
            throw new InvalidOperationException("Exceeds movement remaining.");

        var player = CurrentTurn.ActivePlayer;
        if (player.ActiveRoute == null)
            throw new InvalidOperationException("No active route set.");

        var route = player.ActiveRoute;
        int currentIndex = player.RouteProgressIndex;

        // Validate we have enough nodes to move
        int targetIndex = currentIndex + steps;
        if (targetIndex >= route.NodeIds.Count)
            targetIndex = route.NodeIds.Count - 1;

        int actualSteps = targetIndex - currentIndex;

        // Track segments and validate non-reuse
        for (int i = currentIndex; i < targetIndex; i++)
        {
            if (i >= route.Segments.Count) break;
            var segment = route.Segments[i];
            var segKey = new SegmentKey(segment.FromNodeId, segment.ToNodeId);

            if (player.UsedSegments.Contains(segKey))
                throw new InvalidOperationException("Segment reuse violation.");

            player.UsedSegments.Add(segKey);
            CurrentTurn.RailroadsRiddenThisTurn.Add(segment.RailroadIndex);
        }

        // Update position
        player.RouteProgressIndex = targetIndex;
        string newNodeId = route.NodeIds[targetIndex];
        player.CurrentNodeId = newNodeId;

        // Update CurrentCity if the new node is a city
        if (_cityByNodeId.TryGetValue(newNodeId, out var city))
        {
            player.CurrentCity = city;
        }

        CurrentTurn.MovementRemaining -= actualSteps;

        // Check if arrived at destination
        bool arrivedAtDestination = player.Destination != null &&
            _cityByNodeId.TryGetValue(newNodeId, out var arrivalCity) &&
            string.Equals(arrivalCity.Name, player.Destination.Name, StringComparison.OrdinalIgnoreCase);

        if (arrivedAtDestination)
        {
            HandleArrival(player);
        }
        else if (CurrentTurn.MovementRemaining <= 0)
        {
            // No more movement — check for bonus roll
            if (CurrentTurn.BonusRollAvailable)
            {
                // Roll bonus die
                var bonusDice = _randomProvider.RollDiceIndividual(1);
                int bonusMovement = bonusDice[0];
                CurrentTurn.MovementAllowance = bonusMovement;
                CurrentTurn.MovementRemaining = bonusMovement;
                CurrentTurn.BonusRollAvailable = false;
                // Stay in Move phase
            }
            else
            {
                // Movement complete, advance to Purchase phase (skip Arrival if not arrived)
                CurrentTurn.Phase = TurnPhase.Purchase;
            }
        }
    }

    /// <summary>
    /// Computes cheapest route from player's current position to destination.
    /// Does NOT mutate state.
    /// </summary>
    public Route SuggestRoute()
    {
        var player = CurrentTurn.ActivePlayer;
        if (player.Destination == null)
            throw new InvalidOperationException("No destination assigned.");

        string? startNodeId = player.CurrentNodeId;
        if (startNodeId == null)
            throw new InvalidOperationException("Player has no current node position.");

        // Find destination node ID
        string? destNodeId = FindCityNodeId(player.Destination);
        if (destNodeId == null)
            throw new InvalidOperationException($"Cannot find node for destination city '{player.Destination.Name}'.");

        // Use Dijkstra to find cheapest route considering use fees
        return FindCheapestRoute(startNodeId, destNodeId, player);
    }

    /// <summary>
    /// Saves a route as the active player's planned path.
    /// </summary>
    public void SaveRoute(Route route)
    {
        ArgumentNullException.ThrowIfNull(route);

        var player = CurrentTurn.ActivePlayer;
        if (player.Destination == null)
            throw new InvalidOperationException("No destination assigned.");

        player.ActiveRoute = route;
        player.RouteProgressIndex = 0;
    }

    /// <summary>
    /// Purchases an unowned railroad for the active player.
    /// </summary>
    public void BuyRailroad(Railroad railroad)
    {
        EnsureInProgress();

        if (CurrentTurn.Phase != TurnPhase.Purchase)
            throw new InvalidOperationException("Not in Purchase phase.");

        ArgumentNullException.ThrowIfNull(railroad);

        if (railroad.Owner != null)
            throw new InvalidOperationException("Railroad is already owned.");
        if (railroad.IsPublic)
            throw new InvalidOperationException("Public railroads cannot be purchased.");

        var player = CurrentTurn.ActivePlayer;
        if (player.Cash < railroad.PurchasePrice)
            throw new InvalidOperationException("Insufficient funds.");

        player.Cash -= railroad.PurchasePrice;
        railroad.Owner = player;
        player.OwnedRailroads.Add(railroad);

        CheckAllRailroadsSold();

        // Advance to UseFees
        CurrentTurn.Phase = TurnPhase.UseFees;
        ResolveUseFees();
    }

    /// <summary>
    /// Upgrades the active player's locomotive type.
    /// </summary>
    public void UpgradeLocomotive(LocomotiveType target)
    {
        EnsureInProgress();

        if (CurrentTurn.Phase != TurnPhase.Purchase)
            throw new InvalidOperationException("Not in Purchase phase.");

        var player = CurrentTurn.ActivePlayer;

        int cost = GetUpgradeCost(player.LocomotiveType, target);
        if (cost < 0)
            throw new InvalidOperationException("Invalid upgrade path.");
        if (player.Cash < cost)
            throw new InvalidOperationException("Insufficient funds.");

        var oldType = player.LocomotiveType;
        player.Cash -= cost;
        player.LocomotiveType = target;

        LocomotiveUpgraded?.Invoke(this, new LocomotiveUpgradedEventArgs(player, oldType, target));

        // Advance to UseFees
        CurrentTurn.Phase = TurnPhase.UseFees;
        ResolveUseFees();
    }

    /// <summary>
    /// Initiates an auction for a railroad owned by the active player.
    /// </summary>
    public void AuctionRailroad(Railroad railroad)
    {
        EnsureInProgress();

        if (CurrentTurn.Phase != TurnPhase.Purchase && CurrentTurn.Phase != TurnPhase.UseFees)
            throw new InvalidOperationException("Cannot auction in current phase.");

        ArgumentNullException.ThrowIfNull(railroad);

        if (railroad.Owner != CurrentTurn.ActivePlayer)
            throw new InvalidOperationException("Player does not own this railroad.");

        var eligibleBidders = Players.Where(p => p.IsActive && p != CurrentTurn.ActivePlayer).ToList();
        AuctionStarted?.Invoke(this, new AuctionStartedEventArgs(railroad, eligibleBidders));
    }

    /// <summary>
    /// Resolves an auction with a winning bid.
    /// </summary>
    public void ResolveAuction(Railroad railroad, Player? winner, int winningBid)
    {
        EnsureInProgress();
        ArgumentNullException.ThrowIfNull(railroad);

        var previousOwner = railroad.Owner;

        if (winner != null && winningBid > 0)
        {
            // Transfer ownership
            if (winner.Cash < winningBid)
                throw new InvalidOperationException("Winner has insufficient funds for bid.");

            winner.Cash -= winningBid;
            if (previousOwner != null)
            {
                previousOwner.Cash += winningBid;
                previousOwner.OwnedRailroads.Remove(railroad);
            }
            railroad.Owner = winner;
            winner.OwnedRailroads.Add(railroad);
        }
        else
        {
            // No winner — railroad returns to bank
            if (previousOwner != null)
            {
                previousOwner.OwnedRailroads.Remove(railroad);
            }
            railroad.Owner = null;
        }

        CheckAllRailroadsSold();
        AuctionCompleted?.Invoke(this, new AuctionCompletedEventArgs(railroad, winner, winningBid));
    }

    /// <summary>
    /// Skips purchase opportunity and advances to fee resolution.
    /// </summary>
    public void DeclinePurchase()
    {
        EnsureInProgress();

        if (CurrentTurn.Phase != TurnPhase.Purchase)
            throw new InvalidOperationException("Not in Purchase phase.");

        CurrentTurn.Phase = TurnPhase.UseFees;
        ResolveUseFees();
    }

    /// <summary>
    /// Ends the current turn and advances to the next player.
    /// </summary>
    public void EndTurn()
    {
        EnsureInProgress();

        if (CurrentTurn.Phase != TurnPhase.EndTurn)
            throw new InvalidOperationException("Not in EndTurn phase.");

        // Clear turn-specific tracking
        CurrentTurn.RailroadsRiddenThisTurn.Clear();
        CurrentTurn.DiceResult = null;
        CurrentTurn.MovementAllowance = 0;
        CurrentTurn.MovementRemaining = 0;
        CurrentTurn.BonusRollAvailable = false;

        // Find next active player
        int currentIndex = CurrentTurn.ActivePlayer.Index;
        int nextIndex = currentIndex;
        do
        {
            nextIndex = (nextIndex + 1) % Players.Count;
        }
        while (!Players[nextIndex].IsActive && nextIndex != currentIndex);

        if (nextIndex == currentIndex && !Players[nextIndex].IsActive)
        {
            // No active players — shouldn't happen normally
            return;
        }

        var nextPlayer = Players[nextIndex];
        CurrentTurn.ActivePlayer = nextPlayer;
        CurrentTurn.TurnNumber++;

        // Set phase based on whether player already has a destination
        CurrentTurn.Phase = nextPlayer.Destination != null
            ? TurnPhase.Roll
            : TurnPhase.DrawDestination;

        TurnStarted?.Invoke(this, new TurnStartedEventArgs(nextPlayer, CurrentTurn.TurnNumber));
    }

    #endregion

    #region Snapshot/Persistence

    /// <summary>
    /// Creates a serializable snapshot of all game state.
    /// </summary>
    public GameState ToSnapshot()
    {
        var snapshot = new GameState
        {
            GameStatus = GameStatus.ToString(),
            TurnNumber = CurrentTurn.TurnNumber,
            ActivePlayerIndex = CurrentTurn.ActivePlayer.Index,
            AllRailroadsSold = AllRailroadsSold,
            WinnerIndex = Winner?.Index,
            Turn = new TurnState
            {
                Phase = CurrentTurn.Phase.ToString(),
                MovementAllowance = CurrentTurn.MovementAllowance,
                MovementRemaining = CurrentTurn.MovementRemaining,
                BonusRollAvailable = CurrentTurn.BonusRollAvailable,
                RailroadsRiddenThisTurn = CurrentTurn.RailroadsRiddenThisTurn.ToList(),
                DiceResult = CurrentTurn.DiceResult != null
                    ? new DiceResultState
                    {
                        WhiteDice = CurrentTurn.DiceResult.WhiteDice,
                        RedDie = CurrentTurn.DiceResult.RedDie
                    }
                    : null
            }
        };

        foreach (var player in Players)
        {
            var ps = new PlayerState
            {
                Name = player.Name,
                Cash = player.Cash,
                HomeCityName = player.HomeCity.Name,
                CurrentCityName = player.CurrentCity.Name,
                DestinationCityName = player.Destination?.Name,
                LocomotiveType = player.LocomotiveType.ToString(),
                IsActive = player.IsActive,
                IsBankrupt = player.IsBankrupt,
                HasDeclared = player.HasDeclared,
                OwnedRailroadIndices = player.OwnedRailroads.Select(r => r.Index).ToList(),
                CurrentNodeId = player.CurrentNodeId,
                RouteProgressIndex = player.RouteProgressIndex,
                UsedSegments = player.UsedSegments.Select(s => s.ToString()).ToList()
            };

            if (player.ActiveRoute != null)
            {
                ps.ActiveRoute = new RouteState
                {
                    NodeIds = player.ActiveRoute.NodeIds.ToList(),
                    TotalCost = player.ActiveRoute.TotalCost,
                    Segments = player.ActiveRoute.Segments.Select(s => new RouteSegmentState
                    {
                        FromNodeId = s.FromNodeId,
                        ToNodeId = s.ToNodeId,
                        RailroadIndex = s.RailroadIndex
                    }).ToList()
                };

                var selectedRouteStartIndex = Math.Clamp(player.RouteProgressIndex, 0, Math.Max(0, player.ActiveRoute.NodeIds.Count - 1));
                ps.SelectedRouteNodeIds = player.ActiveRoute.NodeIds.Skip(selectedRouteStartIndex).ToList();
                ps.SelectedRouteSegmentKeys = player.ActiveRoute.Segments
                    .Skip(Math.Clamp(player.RouteProgressIndex, 0, player.ActiveRoute.Segments.Count))
                    .Select(static segment => SerializeSelectedRouteSegment(segment.FromNodeId, segment.ToNodeId, segment.RailroadIndex))
                    .ToList();

                if (ps.SelectedRouteNodeIds.Count == 0 && !string.IsNullOrWhiteSpace(player.CurrentNodeId))
                {
                    ps.SelectedRouteNodeIds.Add(player.CurrentNodeId);
                }
            }

            snapshot.Players.Add(ps);
        }

        foreach (var rr in Railroads)
        {
            snapshot.RailroadOwnership[rr.Index] = rr.Owner?.Index;
        }

        return snapshot;
    }

    /// <summary>
    /// Restores a fully functional GameEngine from a snapshot.
    /// </summary>
    public static GameEngine FromSnapshot(GameState snapshot, MapDefinition mapDefinition, IRandomProvider randomProvider)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(mapDefinition);
        ArgumentNullException.ThrowIfNull(randomProvider);

        var engine = new GameEngine(mapDefinition, randomProvider);

        // Restore railroads
        foreach (var rd in mapDefinition.Railroads)
        {
            int price = rd.Index < RailroadPrices.Length ? RailroadPrices[rd.Index] : 10000;
            bool isPublic = PublicRailroadIndices.Contains(rd.Index);
            engine.Railroads.Add(new Railroad(rd, price, isPublic));
        }

        // Restore players
        foreach (var ps in snapshot.Players)
        {
            var player = new Player(ps.Name, engine.Players.Count)
            {
                Cash = ps.Cash,
                LocomotiveType = Enum.Parse<LocomotiveType>(ps.LocomotiveType),
                IsActive = ps.IsActive,
                IsBankrupt = ps.IsBankrupt,
                HasDeclared = ps.HasDeclared,
                CurrentNodeId = ps.CurrentNodeId,
                RouteProgressIndex = ps.RouteProgressIndex
            };

            // Resolve cities by name
            if (engine._cityByName.TryGetValue(ps.HomeCityName, out var homeCity))
                player.HomeCity = homeCity;
            if (engine._cityByName.TryGetValue(ps.CurrentCityName, out var currentCity))
                player.CurrentCity = currentCity;
            if (ps.DestinationCityName != null && engine._cityByName.TryGetValue(ps.DestinationCityName, out var destCity))
                player.Destination = destCity;

            // Restore used segments
            foreach (var segStr in ps.UsedSegments)
            {
                var parts = segStr.Split('-');
                if (parts.Length == 2)
                    player.UsedSegments.Add(new SegmentKey(parts[0], parts[1]));
            }

            // Restore active route
            if (ps.ActiveRoute != null)
            {
                var segments = ps.ActiveRoute.Segments.Select(s => new RouteSegment
                {
                    FromNodeId = s.FromNodeId,
                    ToNodeId = s.ToNodeId,
                    RailroadIndex = s.RailroadIndex
                }).ToList();

                player.ActiveRoute = new Route(ps.ActiveRoute.NodeIds, segments, ps.ActiveRoute.TotalCost);
            }

            engine.Players.Add(player);
        }

        // Restore railroad ownership
        foreach (var kvp in snapshot.RailroadOwnership)
        {
            var rr = engine.Railroads.FirstOrDefault(r => r.Index == kvp.Key);
            if (rr != null && kvp.Value.HasValue && kvp.Value.Value < engine.Players.Count)
            {
                var owner = engine.Players[kvp.Value.Value];
                rr.Owner = owner;
                if (!owner.OwnedRailroads.Contains(rr))
                    owner.OwnedRailroads.Add(rr);
            }
        }

        // Restore turn
        engine._currentTurn = new Turn
        {
            ActivePlayer = engine.Players[snapshot.ActivePlayerIndex],
            TurnNumber = snapshot.TurnNumber,
            Phase = Enum.Parse<TurnPhase>(snapshot.Turn.Phase),
            MovementAllowance = snapshot.Turn.MovementAllowance,
            MovementRemaining = snapshot.Turn.MovementRemaining,
            BonusRollAvailable = snapshot.Turn.BonusRollAvailable
        };

        if (snapshot.Turn.DiceResult != null)
        {
            engine._currentTurn.DiceResult = new DiceResult(
                snapshot.Turn.DiceResult.WhiteDice,
                snapshot.Turn.DiceResult.RedDie);
        }

        foreach (var rrIdx in snapshot.Turn.RailroadsRiddenThisTurn)
            engine._currentTurn.RailroadsRiddenThisTurn.Add(rrIdx);

        // Restore game status
        engine._gameStatus = Enum.Parse<GameStatus>(snapshot.GameStatus);
        engine._allRailroadsSold = snapshot.AllRailroadsSold;

        if (snapshot.WinnerIndex.HasValue && snapshot.WinnerIndex.Value < engine.Players.Count)
            engine._winner = engine.Players[snapshot.WinnerIndex.Value];

        return engine;
    }

    private static string SerializeSelectedRouteSegment(string fromNodeId, string toNodeId, int railroadIndex)
    {
        return string.Concat(fromNodeId, "|", toNodeId, "|", railroadIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    #endregion

    #region Private Helpers

    private void EnsureInProgress()
    {
        if (GameStatus != GameStatus.InProgress)
            throw new InvalidOperationException("Game is not in progress.");
    }

    private CityDefinition DrawRandomCity()
    {
        var regionsWithProb = MapDefinition.Regions
            .Where(r => r.Probability.HasValue && r.Probability.Value > 0)
            .ToList();

        if (regionsWithProb.Count == 0)
        {
            // Fallback: pick any city
            return MapDefinition.Cities[_randomProvider.RollDiceIndividual(1)[0] % MapDefinition.Cities.Count];
        }

        var regionProbs = regionsWithProb.Select(r => r.Probability!.Value).ToList();
        int regionIdx = _randomProvider.WeightedDraw(regionProbs);
        var selectedRegion = regionsWithProb[regionIdx];

        var citiesInRegion = MapDefinition.Cities
            .Where(c => c.RegionCode == selectedRegion.Code && c.Probability.HasValue && c.Probability.Value > 0)
            .ToList();

        if (citiesInRegion.Count == 0)
        {
            // Fallback to all cities in the region
            citiesInRegion = MapDefinition.Cities.Where(c => c.RegionCode == selectedRegion.Code).ToList();
            if (citiesInRegion.Count == 0)
                return MapDefinition.Cities[0]; // Ultimate fallback
            return citiesInRegion[_randomProvider.RollDiceIndividual(1)[0] % citiesInRegion.Count];
        }

        var cityProbs = citiesInRegion.Select(c => c.Probability!.Value).ToList();
        int cityIdx = _randomProvider.WeightedDraw(cityProbs);
        return citiesInRegion[cityIdx];
    }

    private void HandleArrival(Player player)
    {
        // Calculate payout
        int payout = 0;
        if (player.Destination != null && player.CurrentCity.PayoutIndex.HasValue && player.Destination.PayoutIndex.HasValue)
        {
            payout = PayoutTable.GetPayout(player.CurrentCity.PayoutIndex.Value, player.Destination.PayoutIndex.Value);
        }

        player.Cash += payout;

        var destination = player.Destination!;
        player.Destination = null;
        player.ActiveRoute = null;
        player.UsedSegments.Clear();

        DestinationReached?.Invoke(this, new DestinationReachedEventArgs(player, destination, payout));

        // Check win condition: $200,000+ and at home city
        if (player.Cash >= 200_000 &&
            string.Equals(player.CurrentCity.Name, player.HomeCity.Name, StringComparison.OrdinalIgnoreCase))
        {
            Winner = player;
            GameStatus = GameStatus.Completed;
            GameOver?.Invoke(this, new GameOverEventArgs(player));
            return;
        }

        // Move to Purchase phase
        CurrentTurn.Phase = TurnPhase.Purchase;
    }

    private void ResolveUseFees()
    {
        var player = CurrentTurn.ActivePlayer;
        var railroadsUsed = CurrentTurn.RailroadsRiddenThisTurn;

        if (railroadsUsed.Count == 0)
        {
            CurrentTurn.Phase = TurnPhase.EndTurn;
            return;
        }

        // Group railroads by owner
        var byOwner = new Dictionary<int, (Player? owner, List<int> railroads)>();
        foreach (var rrIdx in railroadsUsed)
        {
            var railroad = Railroads.FirstOrDefault(r => r.Index == rrIdx);
            if (railroad == null) continue;

            // Skip player's own railroads
            if (railroad.Owner == player) continue;

            // Use a counter key to group by owner, using -1 for bank
            int ownerKey = railroad.Owner?.Index ?? -1;
            if (!byOwner.TryGetValue(ownerKey, out var entry))
            {
                entry = (railroad.Owner, new List<int>());
                byOwner[ownerKey] = entry;
            }
            entry.railroads.Add(rrIdx);
        }

        // Calculate fees
        bool bankUsed = byOwner.ContainsKey(-1);
        int bankFee = bankUsed ? 1000 : 0;

        // Pay bank fee
        if (bankFee > 0)
        {
            player.Cash -= bankFee;
            UsageFeeCharged?.Invoke(this, new UsageFeeChargedEventArgs(player, null, bankFee, byOwner[-1].railroads));
        }

        // Pay opponent fees
        int opponentRate = AllRailroadsSold ? 10000 : 5000;
        foreach (var kvp in byOwner)
        {
            if (kvp.Key == -1) continue; // Skip bank (already handled)

            var (owner, rrList) = kvp.Value;
            int fee = opponentRate; // Flat rate per opponent
            player.Cash -= fee;
            owner!.Cash += fee;

            UsageFeeCharged?.Invoke(this, new UsageFeeChargedEventArgs(player, owner, fee, rrList));
        }

        // Check for bankruptcy
        if (player.Cash < 0)
        {
            HandleBankruptcy(player);
        }

        CurrentTurn.Phase = TurnPhase.EndTurn;
    }

    private void HandleBankruptcy(Player player)
    {
        // If player has railroads, they could auction them
        // For now, simplified: mark bankrupt if cash < 0 and no railroads to sell
        if (player.OwnedRailroads.Count == 0)
        {
            player.IsBankrupt = true;
            player.IsActive = false;
            PlayerBankrupt?.Invoke(this, new PlayerBankruptEventArgs(player));

            // Check if only one player remains
            var activePlayers = Players.Where(p => p.IsActive).ToList();
            if (activePlayers.Count == 1)
            {
                Winner = activePlayers[0];
                GameStatus = GameStatus.Completed;
                GameOver?.Invoke(this, new GameOverEventArgs(activePlayers[0]));
            }
        }
        // If player has railroads, they should auction them first
        // The UI/caller would call AuctionRailroad() before the engine forces bankruptcy
    }

    private void CheckAllRailroadsSold()
    {
        AllRailroadsSold = Railroads.Where(r => !r.IsPublic).All(r => r.Owner != null);
    }

    private static int GetUpgradeCost(LocomotiveType from, LocomotiveType to)
    {
        return (from, to) switch
        {
            (LocomotiveType.Freight, LocomotiveType.Express) => 4000,
            (LocomotiveType.Freight, LocomotiveType.Superchief) => 20000,
            (LocomotiveType.Express, LocomotiveType.Superchief) => 20000,
            _ => -1 // Invalid upgrade
        };
    }

    private static string NodeKey(int regionIndex, int dotIndex) => $"{regionIndex}:{dotIndex}";

    private string? FindCityNodeId(CityDefinition city)
    {
        if (city.MapDotIndex.HasValue)
        {
            var region = MapDefinition.Regions.FirstOrDefault(r => r.Code == city.RegionCode);
            if (region != null)
            {
                int regionIndex = MapDefinition.Regions.IndexOf(region);
                return NodeKey(regionIndex, city.MapDotIndex.Value);
            }
        }
        return null;
    }

    private static Dictionary<string, List<RouteGraphEdge>> BuildAdjacency(MapDefinition mapDefinition)
    {
        var adjacency = new Dictionary<string, List<RouteGraphEdge>>(StringComparer.OrdinalIgnoreCase);

        foreach (var segment in mapDefinition.RailroadRouteSegments)
        {
            var fromNodeId = NodeKey(segment.StartRegionIndex, segment.StartDotIndex);
            var toNodeId = NodeKey(segment.EndRegionIndex, segment.EndDotIndex);

            if (string.Equals(fromNodeId, toNodeId, StringComparison.OrdinalIgnoreCase))
                continue;

            var forward = new RouteGraphEdge(fromNodeId, toNodeId, segment.RailroadIndex);
            var reverse = new RouteGraphEdge(toNodeId, fromNodeId, segment.RailroadIndex);

            if (!adjacency.TryGetValue(fromNodeId, out var fromList))
            {
                fromList = new List<RouteGraphEdge>();
                adjacency[fromNodeId] = fromList;
            }
            fromList.Add(forward);

            if (!adjacency.TryGetValue(toNodeId, out var toList))
            {
                toList = new List<RouteGraphEdge>();
                adjacency[toNodeId] = toList;
            }
            toList.Add(reverse);
        }

        return adjacency;
    }

    private Route FindCheapestRoute(string startNodeId, string destNodeId, Player player)
    {
        // Dijkstra's algorithm with use-fee-based edge costs
        var dist = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var prev = new Dictionary<string, (string node, RouteGraphEdge edge)>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pq = new PriorityQueue<string, int>();

        dist[startNodeId] = 0;
        pq.Enqueue(startNodeId, 0);

        while (pq.Count > 0)
        {
            var current = pq.Dequeue();
            if (visited.Contains(current))
                continue;
            visited.Add(current);

            if (string.Equals(current, destNodeId, StringComparison.OrdinalIgnoreCase))
                break;

            if (!_adjacency.TryGetValue(current, out var edges))
                continue;

            foreach (var edge in edges)
            {
                if (visited.Contains(edge.ToNodeId))
                    continue;

                // Check non-reuse
                var segKey = new SegmentKey(edge.FromNodeId, edge.ToNodeId);
                if (player.UsedSegments.Contains(segKey))
                    continue;

                // Calculate edge cost based on railroad ownership
                int edgeCost = 1; // Base cost per segment (milepost)
                var railroad = Railroads.FirstOrDefault(r => r.Index == edge.RailroadIndex);
                if (railroad != null && railroad.Owner != player)
                {
                    // Add use fee penalty for opponent/bank railroads
                    edgeCost += railroad.Owner == null ? 1 : 5;
                }

                int newDist = dist[current] + edgeCost;
                if (!dist.TryGetValue(edge.ToNodeId, out var oldDist) || newDist < oldDist)
                {
                    dist[edge.ToNodeId] = newDist;
                    prev[edge.ToNodeId] = (current, edge);
                    pq.Enqueue(edge.ToNodeId, newDist);
                }
            }
        }

        // Reconstruct route
        if (!prev.ContainsKey(destNodeId) && !string.Equals(startNodeId, destNodeId, StringComparison.OrdinalIgnoreCase))
        {
            // No route found — return empty route
            return new Route(new[] { startNodeId }, Array.Empty<RouteSegment>(), 0);
        }

        var nodeIds = new List<string>();
        var segments = new List<RouteSegment>();
        var current2 = destNodeId;

        while (prev.ContainsKey(current2))
        {
            var (prevNode, edge) = prev[current2];
            nodeIds.Add(current2);
            segments.Add(new RouteSegment
            {
                FromNodeId = edge.FromNodeId,
                ToNodeId = edge.ToNodeId,
                RailroadIndex = edge.RailroadIndex
            });
            current2 = prevNode;
        }
        nodeIds.Add(startNodeId);

        nodeIds.Reverse();
        segments.Reverse();

        // Calculate total cost
        int totalCost = 0;
        foreach (var seg in segments)
        {
            var rr = Railroads.FirstOrDefault(r => r.Index == seg.RailroadIndex);
            if (rr != null && rr.Owner != player)
            {
                totalCost += rr.Owner == null ? 1000 : (AllRailroadsSold ? 10000 : 5000);
            }
        }

        return new Route(nodeIds, segments, totalCost);
    }

    #endregion
}

/// <summary>
/// Internal edge in the route graph.
/// </summary>
internal sealed class RouteGraphEdge
{
    public string FromNodeId { get; }
    public string ToNodeId { get; }
    public int RailroadIndex { get; }

    public RouteGraphEdge(string fromNodeId, string toNodeId, int railroadIndex)
    {
        FromNodeId = fromNodeId;
        ToNodeId = toNodeId;
        RailroadIndex = railroadIndex;
    }
}
