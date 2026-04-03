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
    public const int AuctionBidIncrement = 250;
    private const int RoutePlanningDirectnessPenaltyPerSegment = 250;
    private const int RoutePlanningHostileExitPenalty = 1500;

    private GameStatus _gameStatus;
    private Turn _currentTurn = null!;
    private bool _allRailroadsSold;
    private Player? _winner;

    // Map infrastructure
    private IRandomProvider _randomProvider = null!;
    private Dictionary<string, TrainDot> _dotLookup = null!;
    private Dictionary<string, List<RouteGraphEdge>> _adjacency = null!;
    private Dictionary<string, CityDefinition> _cityByNodeId = null!;
    private Dictionary<string, CityDefinition> _cityByName = null!;
    private GameSettings _settings = null!;

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
    public MapDefinition MapDefinition { get; private set; } = null!;

    public GameSettings Settings => _settings;

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
    public GameEngine(MapDefinition mapDefinition, IReadOnlyList<string> playerNames, IRandomProvider randomProvider, int superchiefPrice = 40_000)
        : this(mapDefinition, playerNames, randomProvider, GameSettings.Default with { SuperchiefPrice = superchiefPrice })
    {
    }

    public GameEngine(MapDefinition mapDefinition, IReadOnlyList<string> playerNames, IRandomProvider randomProvider, GameSettings settings)
    {
        ArgumentNullException.ThrowIfNull(mapDefinition);
        ArgumentNullException.ThrowIfNull(playerNames);
        ArgumentNullException.ThrowIfNull(randomProvider);
        ArgumentNullException.ThrowIfNull(settings);

        Initialize(mapDefinition, playerNames, randomProvider, settings);
    }

    private void Initialize(MapDefinition mapDefinition, IReadOnlyList<string> playerNames, IRandomProvider randomProvider, GameSettings settings)
    {
        var normalizedSettings = settings;

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
        _settings = normalizedSettings;

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
                    var nodeId = NodeKey(region.Index, city.MapDotIndex.Value);
                    _cityByNodeId[nodeId] = city;
                }
            }
            _cityByName[city.Name] = city;
        }

        // Initialize railroads
        foreach (var rd in mapDefinition.Railroads)
        {
            int price = rd.PurchasePrice ?? GetRailroadPurchasePrice(rd.Index);
            bool isPublic = PublicRailroadIndices.Contains(rd.Index);
            Railroads.Add(new Railroad(rd, price, isPublic));
        }

        // Initialize players with random home cities
        for (int i = 0; i < playerNames.Count; i++)
        {
            var player = new Player(playerNames[i], i, _settings);
            var homeCity = DrawRandomCity();
            player.HomeCity = homeCity;
            player.CurrentCity = homeCity;

            // Set the player's current node ID based on home city
            if (homeCity.MapDotIndex.HasValue)
            {
                var region = mapDefinition.Regions.FirstOrDefault(r => r.Code == homeCity.RegionCode);
                if (region != null)
                {
                    player.CurrentNodeId = NodeKey(region.Index, homeCity.MapDotIndex.Value);
                }
            }

            Players.Add(player);
        }

        // Initialize turn
        _currentTurn = new Turn
        {
            ActivePlayer = Players[0],
            TurnNumber = 1
        };

        PrepareTurnForActivePlayer(Players[0]);

        GameStatus = GameStatus.InProgress;
    }

    private GameEngine(MapDefinition mapDefinition, IRandomProvider randomProvider, GameSettings settings)
    {
        MapDefinition = mapDefinition;
        _randomProvider = randomProvider;
        _settings = settings;

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
                    _cityByNodeId[NodeKey(region.Index, city.MapDotIndex.Value)] = city;
                }
            }
            _cityByName[city.Name] = city;
        }
    }

    /// <summary>
    /// Private constructor for snapshot restoration.
    /// </summary>
    private GameEngine(MapDefinition mapDefinition, IRandomProvider randomProvider, int superchiefPrice)
        : this(mapDefinition, randomProvider, GameSettings.Default with { SuperchiefPrice = superchiefPrice })
    {
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
        if (string.Equals(city.RegionCode, player.CurrentCity.RegionCode, StringComparison.OrdinalIgnoreCase))
        {
            var pendingRegionChoice = BuildPendingRegionChoice(player, city.RegionCode, PendingDestinationAssignmentKind.NormalDestination);
            if (pendingRegionChoice is not null)
            {
                CurrentTurn.PendingRegionChoice = pendingRegionChoice;
                CurrentTurn.Phase = TurnPhase.RegionChoice;
                return city;
            }
        }

        FinalizeDestinationAssignment(player, city, PendingDestinationAssignmentKind.NormalDestination);
        return city;
    }

    public CityDefinition ChooseHomeCity(string cityName)
    {
        EnsureInProgress();

        if (CurrentTurn.Phase != TurnPhase.HomeCityChoice)
            throw new InvalidOperationException("Not in HomeCityChoice phase.");

        ArgumentException.ThrowIfNullOrWhiteSpace(cityName);

        var player = CurrentTurn.ActivePlayer;
        var pendingHomeCityChoice = CurrentTurn.PendingHomeCityChoice
            ?? throw new InvalidOperationException("No pending home-city choice exists.");

        if (!pendingHomeCityChoice.EligibleCityNames.Contains(cityName, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"City '{cityName}' is not an eligible home-city choice.");

        var city = MapDefinition.Cities.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, cityName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.RegionCode, pendingHomeCityChoice.RegionCode, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Home city '{cityName}' was not found in region '{pendingHomeCityChoice.RegionCode}'.");

        SetPlayerHomeCity(player, city);
        player.HasResolvedHomeCityChoice = true;
        CurrentTurn.PendingHomeCityChoice = null;
        CurrentTurn.Phase = player.Destination is not null
            ? TurnPhase.Roll
            : TurnPhase.DrawDestination;

        return city;
    }

    public void ResolveHomeSwap(bool swapHomeAndDestination)
    {
        EnsureInProgress();

        if (CurrentTurn.Phase != TurnPhase.HomeSwap)
            throw new InvalidOperationException("Not in HomeSwap phase.");

        var player = CurrentTurn.ActivePlayer;
        _ = CurrentTurn.PendingHomeSwap
            ?? throw new InvalidOperationException("No pending home swap exists.");

        if (swapHomeAndDestination)
        {
            var originalHome = player.HomeCity;
            var firstDestination = player.Destination
                ?? throw new InvalidOperationException("A destination is required to perform a home swap.");

            SetPlayerHomeCity(player, firstDestination);
            player.Destination = originalHome;
            player.TripOriginCity = player.HomeCity;
            player.ActiveRoute = null;
            player.RouteProgressIndex = 0;
        }

        player.HasResolvedHomeSwap = true;
        CurrentTurn.PendingHomeSwap = null;
        CurrentTurn.Phase = HasPreparedBonusMove()
            ? TurnPhase.Move
            : TurnPhase.Roll;
    }

    public void Declare()
    {
        EnsureInProgress();

        var player = CurrentTurn.ActivePlayer;
        if (CurrentTurn.Phase != TurnPhase.DrawDestination)
            throw new InvalidOperationException("Not in DrawDestination phase.");
        if (player.Destination != null)
            throw new InvalidOperationException("Player already has a destination.");
        if (player.Cash < _settings.WinningCash)
            throw new InvalidOperationException("Player does not have enough cash to declare.");

        if (string.Equals(player.CurrentCity.Name, player.HomeCity.Name, StringComparison.OrdinalIgnoreCase))
        {
            Winner = player;
            GameStatus = GameStatus.Completed;
            GameOver?.Invoke(this, new GameOverEventArgs(player));
            return;
        }

        player.HasDeclared = true;
        player.TripOriginCity = player.CurrentCity;
        player.ActiveRoute = null;
        player.RouteProgressIndex = 0;

        var city = DrawRandomCity();
        if (string.Equals(city.RegionCode, player.CurrentCity.RegionCode, StringComparison.OrdinalIgnoreCase))
        {
            var pendingRegionChoice = BuildPendingRegionChoice(player, city.RegionCode, PendingDestinationAssignmentKind.DeclaredAlternateDestination);
            if (pendingRegionChoice is not null)
            {
                CurrentTurn.PendingRegionChoice = pendingRegionChoice;
                CurrentTurn.Phase = TurnPhase.RegionChoice;
                return;
            }
        }

        FinalizeDestinationAssignment(player, city, PendingDestinationAssignmentKind.DeclaredAlternateDestination);
    }

    public CityDefinition ChooseDestinationRegion(string regionCode)
    {
        EnsureInProgress();

        if (CurrentTurn.Phase != TurnPhase.RegionChoice)
        {
            throw new InvalidOperationException("Not in RegionChoice phase.");
        }

        var player = CurrentTurn.ActivePlayer;
        var pendingRegionChoice = CurrentTurn.PendingRegionChoice
            ?? throw new InvalidOperationException("No pending region choice exists.");

        if (!pendingRegionChoice.EligibleRegionCodes.Contains(regionCode, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Region '{regionCode}' is not an eligible replacement region.");
        }

        var city = DrawRandomCityFromRegion(regionCode);
        var assignmentKind = pendingRegionChoice.AssignmentKind;
        CurrentTurn.PendingRegionChoice = null;
        FinalizeDestinationAssignment(player, city, assignmentKind);
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
        CurrentTurn.ArrivalResolution = null;

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
        var traversedNodeIds = new List<string>();

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
            var segKey = new SegmentKey(segment.FromNodeId, segment.ToNodeId, segment.RailroadIndex);

            if (player.UsedSegments.Contains(segKey))
            {
                // Stop movement before the duplicate — the route planner should
                // have avoided this, but treat it as the end of valid movement.
                targetIndex = i;
                break;
            }

            player.UsedSegments.Add(segKey);
            CurrentTurn.RailroadsRiddenThisTurn.Add(segment.RailroadIndex);
            TrackRailroadFeeRate(player, segment.RailroadIndex);
            UpdateGrandfatheredRailroadAccess(player, segment.RailroadIndex);
            traversedNodeIds.Add(route.NodeIds[i + 1]);
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

        // Recalculate in case targetIndex was reduced by segment-reuse safety net
        actualSteps = targetIndex - currentIndex;
        CurrentTurn.MovementRemaining -= actualSteps;
    TryResolveRovers(player, traversedNodeIds);

        // Check if arrived at destination.
        // Use the destination's canonical node id first, then fall back to city-name resolution.
        var destinationNodeId = player.Destination is null ? null : FindCityNodeId(player.Destination);
        var arrivedAtDestination = player.Destination != null &&
            (
                (!string.IsNullOrWhiteSpace(destinationNodeId)
                    && string.Equals(newNodeId, destinationNodeId, StringComparison.OrdinalIgnoreCase))
                || (_cityByNodeId.TryGetValue(newNodeId, out var arrivalCity)
                    && string.Equals(arrivalCity.Name, player.Destination.Name, StringComparison.OrdinalIgnoreCase))
                || string.Equals(player.CurrentCity.Name, player.Destination.Name, StringComparison.OrdinalIgnoreCase)
            );

        if (arrivedAtDestination)
        {
            if (!string.Equals(player.CurrentCity.Name, player.Destination!.Name, StringComparison.OrdinalIgnoreCase))
            {
                player.CurrentCity = player.Destination;
            }

            HandleArrival(player, actualSteps);
        }
        else if (CurrentTurn.MovementRemaining <= 0)
        {
            // No more movement — check for bonus roll
            if (CurrentTurn.BonusRollAvailable)
            {
                StartBonusMove(player);
            }
            else
            {
                // Movement complete without arrival: resolve fees immediately and finish the turn flow.
                CurrentTurn.Phase = TurnPhase.UseFees;
                ResolveUseFees();
            }
        }
    }

    /// <summary>
    /// Computes cheapest route from player's current position to destination.
    /// Does NOT mutate state.
    /// </summary>
    public Route SuggestRoute()
    {
        return SuggestRouteForPlayer(CurrentTurn.ActivePlayer.Index);
    }

    /// <summary>
    /// Computes the cheapest route for the specified player from their current position to their destination.
    /// Does NOT mutate state.
    /// </summary>
    public Route SuggestRouteForPlayer(int playerIndex)
    {
        if (playerIndex < 0 || playerIndex >= Players.Count)
            throw new ArgumentOutOfRangeException(nameof(playerIndex));

        var player = Players[playerIndex];
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
        UpdateGrandfatheringForOwnershipChange(railroad.Index, previousOwner: null, newOwner: player);
        CurrentTurn.ArrivalResolution = null;

        CheckAllRailroadsSold();

        ContinueAfterPurchase(player);
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

        int cost = GetUpgradeCost(player.LocomotiveType, target, _settings);
        if (cost < 0)
            throw new InvalidOperationException("Invalid upgrade path.");
        if (player.Cash < cost)
            throw new InvalidOperationException("Insufficient funds.");

        var oldType = player.LocomotiveType;
        player.Cash -= cost;
        player.LocomotiveType = target;
        CurrentTurn.ArrivalResolution = null;

        LocomotiveUpgraded?.Invoke(this, new LocomotiveUpgradedEventArgs(player, oldType, target));

        ContinueAfterPurchase(player);
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
        int startingPrice = railroad.PurchasePrice / 2;
        var orderedParticipants = eligibleBidders
            .OrderBy(player => (player.Index - CurrentTurn.ActivePlayer.Index + Players.Count) % Players.Count)
            .Select(player => new AuctionParticipant
            {
                PlayerIndex = player.Index,
                PlayerName = player.Name,
                CashOnHand = player.Cash,
                LastBidAmount = null,
                IsEligible = player.Cash >= startingPrice,
                HasDroppedOut = player.Cash < startingPrice,
                HasPassedThisRound = false,
                LastAction = player.Cash >= startingPrice
                    ? AuctionParticipantAction.None
                    : AuctionParticipantAction.AutoDropOut
            })
            .ToList();

        orderedParticipants = UpdateAuctionParticipantAffordability(orderedParticipants, startingPrice, leaderPlayerIndex: null);

        CurrentTurn.SelectedRailroadForSaleIndex = railroad.Index;
        CurrentTurn.AuctionState = new AuctionState
        {
            RailroadIndex = railroad.Index,
            RailroadName = railroad.Name,
            SellerPlayerIndex = CurrentTurn.ActivePlayer.Index,
            SellerPlayerName = CurrentTurn.ActivePlayer.Name,
            StartingPrice = startingPrice,
            CurrentBid = 0,
            LastBidderPlayerIndex = null,
            CurrentBidderPlayerIndex = GetNextOpenParticipantIndex(orderedParticipants, CurrentTurn.ActivePlayer.Index),
            RoundNumber = 1,
            ConsecutiveNoBidTurnCount = 0,
            Status = AuctionStatus.Open,
            Participants = orderedParticipants
        };

        if (!orderedParticipants.Any(participant => participant.IsEligible && !participant.HasDroppedOut))
        {
            CompleteAuctionWithBankFallback(railroad);
            return;
        }

        AuctionStarted?.Invoke(this, new AuctionStartedEventArgs(
            railroad,
            eligibleBidders.Where(player => player.Cash >= startingPrice).ToList()));
    }

    public void SubmitAuctionBid(Railroad railroad, Player bidder, int winningBid)
    {
        EnsureInProgress();
        ArgumentNullException.ThrowIfNull(railroad);
        ArgumentNullException.ThrowIfNull(bidder);

        var auctionState = RequireOpenAuctionState(railroad);
        RequireCurrentAuctionParticipant(auctionState, bidder);

        if (winningBid > bidder.Cash)
        {
            DropOutOfAuction(railroad, bidder, automatic: true);
            return;
        }

        int requiredBid = auctionState.CurrentBid > 0
            ? auctionState.CurrentBid + AuctionBidIncrement
            : auctionState.StartingPrice;
        if (winningBid < requiredBid)
            throw new InvalidOperationException($"Bid must be at least {requiredBid}.");

        var updatedParticipants = auctionState.Participants
            .Select(participant => new AuctionParticipant
            {
                PlayerIndex = participant.PlayerIndex,
                PlayerName = participant.PlayerName,
                CashOnHand = Players[participant.PlayerIndex].Cash,
                LastBidAmount = participant.PlayerIndex == bidder.Index ? winningBid : participant.LastBidAmount,
                IsEligible = participant.IsEligible,
                HasDroppedOut = participant.HasDroppedOut,
                HasPassedThisRound = false,
                LastAction = participant.PlayerIndex == bidder.Index ? AuctionParticipantAction.Bid : AuctionParticipantAction.None
            })
            .ToList();

        updatedParticipants = UpdateAuctionParticipantAffordability(
            updatedParticipants,
            winningBid + AuctionBidIncrement,
            bidder.Index);

        var updatedAuctionState = new AuctionState
        {
            RailroadIndex = auctionState.RailroadIndex,
            RailroadName = auctionState.RailroadName,
            SellerPlayerIndex = auctionState.SellerPlayerIndex,
            SellerPlayerName = auctionState.SellerPlayerName,
            StartingPrice = auctionState.StartingPrice,
            CurrentBid = winningBid,
            LastBidderPlayerIndex = bidder.Index,
            CurrentBidderPlayerIndex = GetNextOpenParticipantIndex(updatedParticipants, bidder.Index),
            RoundNumber = auctionState.RoundNumber,
            ConsecutiveNoBidTurnCount = 0,
            Status = AuctionStatus.Open,
            Participants = updatedParticipants
        };

        CurrentTurn.AuctionState = updatedAuctionState;
        EvaluateAuctionCompletion(railroad);
    }

    public void PassAuctionTurn(Railroad railroad, Player player)
    {
        EnsureInProgress();
        ArgumentNullException.ThrowIfNull(railroad);
        ArgumentNullException.ThrowIfNull(player);

        var auctionState = RequireOpenAuctionState(railroad);
        RequireCurrentAuctionParticipant(auctionState, player);

        var updatedParticipants = auctionState.Participants
            .Select(participant => new AuctionParticipant
            {
                PlayerIndex = participant.PlayerIndex,
                PlayerName = participant.PlayerName,
                CashOnHand = Players[participant.PlayerIndex].Cash,
                LastBidAmount = participant.LastBidAmount,
                IsEligible = participant.IsEligible,
                HasDroppedOut = participant.HasDroppedOut,
                HasPassedThisRound = participant.PlayerIndex == player.Index || participant.HasPassedThisRound,
                LastAction = participant.PlayerIndex == player.Index ? AuctionParticipantAction.Pass : participant.LastAction
            })
            .ToList();

        updatedParticipants = UpdateAuctionParticipantAffordability(
            updatedParticipants,
            auctionState.CurrentBid > 0 ? auctionState.CurrentBid + AuctionBidIncrement : auctionState.StartingPrice,
            auctionState.LastBidderPlayerIndex);

        CurrentTurn.AuctionState = new AuctionState
        {
            RailroadIndex = auctionState.RailroadIndex,
            RailroadName = auctionState.RailroadName,
            SellerPlayerIndex = auctionState.SellerPlayerIndex,
            SellerPlayerName = auctionState.SellerPlayerName,
            StartingPrice = auctionState.StartingPrice,
            CurrentBid = auctionState.CurrentBid,
            LastBidderPlayerIndex = auctionState.LastBidderPlayerIndex,
            CurrentBidderPlayerIndex = GetNextOpenParticipantIndex(updatedParticipants, player.Index),
            RoundNumber = auctionState.RoundNumber,
            ConsecutiveNoBidTurnCount = auctionState.ConsecutiveNoBidTurnCount + 1,
            Status = AuctionStatus.Open,
            Participants = updatedParticipants
        };

        EvaluateAuctionCompletion(railroad);
    }

    public void DropOutOfAuction(Railroad railroad, Player player, bool automatic = false)
    {
        EnsureInProgress();
        ArgumentNullException.ThrowIfNull(railroad);
        ArgumentNullException.ThrowIfNull(player);

        var auctionState = RequireOpenAuctionState(railroad);
        RequireCurrentAuctionParticipant(auctionState, player);

        var updatedParticipants = auctionState.Participants
            .Select(participant => new AuctionParticipant
            {
                PlayerIndex = participant.PlayerIndex,
                PlayerName = participant.PlayerName,
                CashOnHand = Players[participant.PlayerIndex].Cash,
                LastBidAmount = participant.LastBidAmount,
                IsEligible = participant.IsEligible,
                HasDroppedOut = participant.PlayerIndex == player.Index || participant.HasDroppedOut,
                HasPassedThisRound = participant.HasPassedThisRound,
                LastAction = participant.PlayerIndex == player.Index
                    ? (automatic ? AuctionParticipantAction.AutoDropOut : AuctionParticipantAction.DropOut)
                    : participant.LastAction
            })
            .ToList();

                updatedParticipants = UpdateAuctionParticipantAffordability(
                    updatedParticipants,
                    auctionState.CurrentBid > 0 ? auctionState.CurrentBid + AuctionBidIncrement : auctionState.StartingPrice,
                    auctionState.LastBidderPlayerIndex);

        CurrentTurn.AuctionState = new AuctionState
        {
            RailroadIndex = auctionState.RailroadIndex,
            RailroadName = auctionState.RailroadName,
            SellerPlayerIndex = auctionState.SellerPlayerIndex,
            SellerPlayerName = auctionState.SellerPlayerName,
            StartingPrice = auctionState.StartingPrice,
            CurrentBid = auctionState.CurrentBid,
            LastBidderPlayerIndex = auctionState.LastBidderPlayerIndex,
            CurrentBidderPlayerIndex = GetNextOpenParticipantIndex(updatedParticipants, player.Index),
            RoundNumber = auctionState.RoundNumber,
            ConsecutiveNoBidTurnCount = auctionState.ConsecutiveNoBidTurnCount + 1,
            Status = AuctionStatus.Open,
            Participants = updatedParticipants
        };

        EvaluateAuctionCompletion(railroad);
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

        UpdateGrandfatheringForOwnershipChange(railroad.Index, previousOwner, railroad.Owner);

        CheckAllRailroadsSold();
        AuctionCompleted?.Invoke(this, new AuctionCompletedEventArgs(railroad, winner, winningBid));
    }

    /// <summary>
    /// Sells an owned railroad directly to the bank during forced fee resolution.
    /// </summary>
    public void SellRailroadToBank(Railroad railroad)
    {
        EnsureInProgress();
        ArgumentNullException.ThrowIfNull(railroad);

        if (CurrentTurn.Phase != TurnPhase.UseFees)
            throw new InvalidOperationException("Railroads can only be sold to the bank during UseFees.");

        if (CurrentTurn.ForcedSaleState is null && CurrentTurn.PendingFeeAmount <= 0)
            throw new InvalidOperationException("A railroad can only be sold to the bank during forced sale.");

        var player = CurrentTurn.ActivePlayer;
        if (railroad.Owner != player)
            throw new InvalidOperationException("Player does not own this railroad.");

        int salePrice = railroad.PurchasePrice / 2;

        player.Cash += salePrice;
        player.OwnedRailroads.Remove(railroad);
        var previousOwner = railroad.Owner;
        railroad.Owner = null;

        UpdateGrandfatheringForOwnershipChange(railroad.Index, previousOwner, newOwner: null);
        CheckAllRailroadsSold();

        var amountOwed = CurrentTurn.PendingFeeAmount > 0
            ? CurrentTurn.PendingFeeAmount
            : CalculatePendingFeeAmount(player);
        var previousSalesCompletedCount = CurrentTurn.ForcedSaleState?.SalesCompletedCount ?? 0;

        CurrentTurn.AuctionState = null;
        CurrentTurn.SelectedRailroadForSaleIndex = player.OwnedRailroads
            .OrderBy(ownedRailroad => ownedRailroad.Name, StringComparer.OrdinalIgnoreCase)
            .Select(ownedRailroad => (int?)ownedRailroad.Index)
            .FirstOrDefault();
        CurrentTurn.ForcedSaleState = new ForcedSaleState
        {
            AmountOwed = amountOwed,
            CashBeforeFees = CurrentTurn.ForcedSaleState?.CashBeforeFees ?? player.Cash - salePrice,
            CashAfterLastSale = player.Cash,
            SalesCompletedCount = previousSalesCompletedCount + 1,
            CanPayNow = player.Cash >= amountOwed,
            EliminationTriggered = false
        };

        if (player.Cash >= amountOwed)
        {
            ResolveUseFees();
            return;
        }

        if (player.OwnedRailroads.Count == 0)
        {
            EliminatePlayerForUnpaidFees(
                player,
                amountOwed,
                CurrentTurn.ForcedSaleState.CashBeforeFees,
                CurrentTurn.ForcedSaleState.SalesCompletedCount);
        }
    }

    /// <summary>
    /// Skips purchase opportunity and advances to fee resolution.
    /// </summary>
    public void DeclinePurchase()
    {
        EnsureInProgress();

        if (CurrentTurn.Phase != TurnPhase.Purchase)
            throw new InvalidOperationException("Not in Purchase phase.");

        CurrentTurn.ArrivalResolution = null;
        ContinueAfterPurchase(CurrentTurn.ActivePlayer);
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
        CurrentTurn.RailroadsRequiringFullOwnerRateThisTurn.Clear();
        CurrentTurn.DiceResult = null;
        CurrentTurn.MovementAllowance = 0;
        CurrentTurn.MovementRemaining = 0;
        CurrentTurn.BonusRollAvailable = false;
        CurrentTurn.ArrivalResolution = null;
        CurrentTurn.PendingHomeCityChoice = null;
        CurrentTurn.PendingHomeSwap = null;
        ClearForcedSaleContext();

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

        PrepareTurnForActivePlayer(nextPlayer);

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
                PendingFeeAmount = CurrentTurn.PendingFeeAmount,
                SelectedRailroadForSaleIndex = CurrentTurn.SelectedRailroadForSaleIndex,
                RailroadsRiddenThisTurn = CurrentTurn.RailroadsRiddenThisTurn.ToList(),
                RailroadsRequiringFullOwnerRateThisTurn = CurrentTurn.RailroadsRequiringFullOwnerRateThisTurn.ToList(),
                ArrivalResolution = CurrentTurn.ArrivalResolution is null
                    ? null
                    : new ArrivalResolutionState
                    {
                        PlayerIndex = CurrentTurn.ArrivalResolution.PlayerIndex,
                        DestinationCityName = CurrentTurn.ArrivalResolution.DestinationCityName,
                        PayoutAmount = CurrentTurn.ArrivalResolution.PayoutAmount,
                        CashAfterPayout = CurrentTurn.ArrivalResolution.CashAfterPayout,
                        PurchaseOpportunityAvailable = CurrentTurn.ArrivalResolution.PurchaseOpportunityAvailable,
                        Message = CurrentTurn.ArrivalResolution.Message
                    },
                ForcedSale = CurrentTurn.ForcedSaleState is null
                    ? null
                    : new ForcedSaleTurnState
                    {
                        AmountOwed = CurrentTurn.ForcedSaleState.AmountOwed,
                        CashBeforeFees = CurrentTurn.ForcedSaleState.CashBeforeFees,
                        CashAfterLastSale = CurrentTurn.ForcedSaleState.CashAfterLastSale,
                        SalesCompletedCount = CurrentTurn.ForcedSaleState.SalesCompletedCount,
                        CanPayNow = CurrentTurn.ForcedSaleState.CanPayNow,
                        EliminationTriggered = CurrentTurn.ForcedSaleState.EliminationTriggered
                    },
                Auction = CurrentTurn.AuctionState is null
                    ? null
                    : new AuctionTurnState
                    {
                        RailroadIndex = CurrentTurn.AuctionState.RailroadIndex,
                        RailroadName = CurrentTurn.AuctionState.RailroadName,
                        SellerPlayerIndex = CurrentTurn.AuctionState.SellerPlayerIndex,
                        SellerPlayerName = CurrentTurn.AuctionState.SellerPlayerName,
                        StartingPrice = CurrentTurn.AuctionState.StartingPrice,
                        CurrentBid = CurrentTurn.AuctionState.CurrentBid,
                        LastBidderPlayerIndex = CurrentTurn.AuctionState.LastBidderPlayerIndex,
                        CurrentBidderPlayerIndex = CurrentTurn.AuctionState.CurrentBidderPlayerIndex,
                        RoundNumber = CurrentTurn.AuctionState.RoundNumber,
                        ConsecutiveNoBidTurnCount = CurrentTurn.AuctionState.ConsecutiveNoBidTurnCount,
                        Status = CurrentTurn.AuctionState.Status.ToString(),
                        Participants = CurrentTurn.AuctionState.Participants
                            .Select(participant => new AuctionParticipantTurnState
                            {
                                PlayerIndex = participant.PlayerIndex,
                                PlayerName = participant.PlayerName,
                                CashOnHand = participant.CashOnHand,
                                LastBidAmount = participant.LastBidAmount,
                                IsEligible = participant.IsEligible,
                                HasDroppedOut = participant.HasDroppedOut,
                                HasPassedThisRound = participant.HasPassedThisRound,
                                LastAction = participant.LastAction.ToString()
                            })
                            .ToList()
                    },
                PendingRegionChoice = CurrentTurn.PendingRegionChoice is null
                    ? null
                    : new PendingRegionChoiceTurnState
                    {
                        PlayerIndex = CurrentTurn.PendingRegionChoice.PlayerIndex,
                        CurrentCityName = CurrentTurn.PendingRegionChoice.CurrentCityName,
                        CurrentRegionCode = CurrentTurn.PendingRegionChoice.CurrentRegionCode,
                        TriggeredByInitialRegionCode = CurrentTurn.PendingRegionChoice.TriggeredByInitialRegionCode,
                        AssignmentKind = CurrentTurn.PendingRegionChoice.AssignmentKind.ToString(),
                        EligibleRegionCodes = CurrentTurn.PendingRegionChoice.EligibleRegionCodes.ToList(),
                        EligibleCityCountsByRegion = CurrentTurn.PendingRegionChoice.EligibleCityCountsByRegion.ToDictionary(
                            entry => entry.Key,
                            entry => entry.Value,
                            StringComparer.OrdinalIgnoreCase)
                    },
                PendingHomeCityChoice = CurrentTurn.PendingHomeCityChoice is null
                    ? null
                    : new PendingHomeCityChoiceTurnState
                    {
                        PlayerIndex = CurrentTurn.PendingHomeCityChoice.PlayerIndex,
                        RegionCode = CurrentTurn.PendingHomeCityChoice.RegionCode,
                        RegionName = CurrentTurn.PendingHomeCityChoice.RegionName,
                        CurrentHomeCityName = CurrentTurn.PendingHomeCityChoice.CurrentHomeCityName,
                        EligibleCityNames = CurrentTurn.PendingHomeCityChoice.EligibleCityNames.ToList()
                    },
                PendingHomeSwap = CurrentTurn.PendingHomeSwap is null
                    ? null
                    : new PendingHomeSwapTurnState
                    {
                        PlayerIndex = CurrentTurn.PendingHomeSwap.PlayerIndex,
                        CurrentHomeCityName = CurrentTurn.PendingHomeSwap.CurrentHomeCityName,
                        FirstDestinationCityName = CurrentTurn.PendingHomeSwap.FirstDestinationCityName
                    },
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
                TurnsTaken = player.TurnsTaken,
                FreightTurnCount = player.FreightTurnCount,
                FreightRollTotal = player.FreightRollTotal,
                ExpressTurnCount = player.ExpressTurnCount,
                ExpressRollTotal = player.ExpressRollTotal,
                SuperchiefTurnCount = player.SuperchiefTurnCount,
                SuperchiefRollTotal = player.SuperchiefRollTotal,
                BonusRollCount = player.BonusRollCount,
                BonusRollTotal = player.BonusRollTotal,
                TotalPayoffsCollected = player.TotalPayoffsCollected,
                TotalFeesPaid = player.TotalFeesPaid,
                FeesPaidToPlayers = player.FeesPaidToPlayers
                    .OrderBy(entry => entry.Key)
                    .Select(entry => new FeePaidToPlayerState
                    {
                        PlayerIndex = entry.Key,
                        Amount = entry.Value
                    })
                    .ToList(),
                TotalFeesCollected = player.TotalFeesCollected,
                TotalRailroadFaceValuePurchased = player.TotalRailroadFaceValuePurchased,
                TotalRailroadAmountPaid = player.TotalRailroadAmountPaid,
                AuctionWins = player.AuctionWins,
                AuctionBidsPlaced = player.AuctionBidsPlaced,
                RailroadsPurchasedCount = player.RailroadsPurchasedCount,
                RailroadsAuctionedCount = player.RailroadsAuctionedCount,
                RailroadsSoldToBankCount = player.RailroadsSoldToBankCount,
                DestinationCount = player.DestinationCount,
                UnfriendlyDestinationCount = player.UnfriendlyDestinationCount,
                DestinationLogEntries = player.DestinationLogEntries.ToList(),
                HomeCityName = player.HomeCity.Name,
                CurrentCityName = player.CurrentCity.Name,
                TripStartCityName = player.TripOriginCity?.Name,
                DestinationCityName = player.Destination?.Name,
                AlternateDestinationCityName = player.AlternateDestination?.Name,
                LocomotiveType = player.LocomotiveType.ToString(),
                IsActive = player.IsActive,
                IsBankrupt = player.IsBankrupt,
                HasDeclared = player.HasDeclared,
                HasResolvedHomeCityChoice = player.HasResolvedHomeCityChoice,
                HasResolvedHomeSwap = player.HasResolvedHomeSwap,
                PendingImmediateArrival = player.PendingImmediateArrival,
                OwnedRailroadIndices = player.OwnedRailroads.Select(r => r.Index).ToList(),
                GrandfatheredRailroadIndices = player.GrandfatheredRailroadIndices.OrderBy(index => index).ToList(),
                GrandfatheredRailroadFees = player.GrandfatheredRailroadFees
                    .OrderBy(entry => entry.Key)
                    .Select(entry => new GrandfatheredRailroadFeeState
                    {
                        RailroadIndex = entry.Key,
                        FeeAmount = entry.Value
                    })
                    .ToList(),
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
    public static GameEngine FromSnapshot(GameState snapshot, MapDefinition mapDefinition, IRandomProvider randomProvider, GameSettings settings)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(mapDefinition);
        ArgumentNullException.ThrowIfNull(randomProvider);
        ArgumentNullException.ThrowIfNull(settings);

        var engine = new GameEngine(mapDefinition, randomProvider, settings);

        // Restore railroads
        foreach (var rd in mapDefinition.Railroads)
        {
            int price = rd.PurchasePrice ?? GetRailroadPurchasePrice(rd.Index);
            bool isPublic = PublicRailroadIndices.Contains(rd.Index);
            engine.Railroads.Add(new Railroad(rd, price, isPublic));
        }

        // Restore players
        foreach (var ps in snapshot.Players)
        {
            var player = new Player(ps.Name, engine.Players.Count, engine._settings)
            {
                Cash = ps.Cash,
                TurnsTaken = ps.TurnsTaken,
                FreightTurnCount = ps.FreightTurnCount,
                FreightRollTotal = ps.FreightRollTotal,
                ExpressTurnCount = ps.ExpressTurnCount,
                ExpressRollTotal = ps.ExpressRollTotal,
                SuperchiefTurnCount = ps.SuperchiefTurnCount,
                SuperchiefRollTotal = ps.SuperchiefRollTotal,
                BonusRollCount = ps.BonusRollCount,
                BonusRollTotal = ps.BonusRollTotal,
                TotalPayoffsCollected = ps.TotalPayoffsCollected,
                TotalFeesPaid = ps.TotalFeesPaid,
                TotalFeesCollected = ps.TotalFeesCollected,
                TotalRailroadFaceValuePurchased = ps.TotalRailroadFaceValuePurchased,
                TotalRailroadAmountPaid = ps.TotalRailroadAmountPaid,
                AuctionWins = ps.AuctionWins,
                AuctionBidsPlaced = ps.AuctionBidsPlaced,
                RailroadsPurchasedCount = ps.RailroadsPurchasedCount,
                RailroadsAuctionedCount = ps.RailroadsAuctionedCount,
                RailroadsSoldToBankCount = ps.RailroadsSoldToBankCount,
                DestinationCount = ps.DestinationCount,
                UnfriendlyDestinationCount = ps.UnfriendlyDestinationCount,
                LocomotiveType = Enum.Parse<LocomotiveType>(ps.LocomotiveType),
                IsActive = ps.IsActive,
                IsBankrupt = ps.IsBankrupt,
                HasDeclared = ps.HasDeclared,
                HasResolvedHomeCityChoice = ps.HasResolvedHomeCityChoice,
                HasResolvedHomeSwap = ps.HasResolvedHomeSwap,
                PendingImmediateArrival = ps.PendingImmediateArrival,
                CurrentNodeId = ps.CurrentNodeId,
                RouteProgressIndex = ps.RouteProgressIndex
            };

            // Resolve cities by name
            if (engine._cityByName.TryGetValue(ps.HomeCityName, out var homeCity))
                player.HomeCity = homeCity;
            if (engine._cityByName.TryGetValue(ps.CurrentCityName, out var currentCity))
                player.CurrentCity = currentCity;
            if (ps.TripStartCityName != null && engine._cityByName.TryGetValue(ps.TripStartCityName, out var tripStartCity))
                player.TripOriginCity = tripStartCity;
            if (ps.DestinationCityName != null && engine._cityByName.TryGetValue(ps.DestinationCityName, out var destCity))
                player.Destination = destCity;
            if (ps.AlternateDestinationCityName != null && engine._cityByName.TryGetValue(ps.AlternateDestinationCityName, out var alternateDestinationCity))
                player.AlternateDestination = alternateDestinationCity;

            foreach (var segStr in ps.UsedSegments)
            {
                var colonIndex = segStr.IndexOf(':');
                if (colonIndex >= 0)
                {
                    var nodePart = segStr.Substring(0, colonIndex);
                    var rrPart = segStr.Substring(colonIndex + 1);
                    var parts = nodePart.Split('-');
                    if (parts.Length == 2 && int.TryParse(rrPart, out var rrIndex))
                        player.UsedSegments.Add(new SegmentKey(parts[0], parts[1], rrIndex));
                }
                else
                {
                    var parts = segStr.Split('-');
                    if (parts.Length == 2)
                        player.UsedSegments.Add(new SegmentKey(parts[0], parts[1], -1));
                }
            }

            foreach (var railroadIndex in ps.GrandfatheredRailroadIndices)
            {
                player.GrandfatheredRailroadIndices.Add(railroadIndex);
            }

            foreach (var grandfatheredRailroadFee in ps.GrandfatheredRailroadFees)
            {
                if (grandfatheredRailroadFee.RailroadIndex < 0 || grandfatheredRailroadFee.FeeAmount <= 0)
                {
                    continue;
                }

                player.GrandfatheredRailroadIndices.Add(grandfatheredRailroadFee.RailroadIndex);
                player.GrandfatheredRailroadFees[grandfatheredRailroadFee.RailroadIndex] = grandfatheredRailroadFee.FeeAmount;
            }

            foreach (var feePaidToPlayer in ps.FeesPaidToPlayers)
            {
                if (feePaidToPlayer.PlayerIndex < 0 || feePaidToPlayer.PlayerIndex == player.Index || feePaidToPlayer.Amount <= 0)
                {
                    continue;
                }

                player.FeesPaidToPlayers[feePaidToPlayer.PlayerIndex] = feePaidToPlayer.Amount;
            }

            foreach (var destinationLogEntry in ps.DestinationLogEntries)
            {
                if (!string.IsNullOrWhiteSpace(destinationLogEntry))
                {
                    player.DestinationLogEntries.Add(destinationLogEntry);
                }
            }

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

        engine._currentTurn = new Turn
        {
            ActivePlayer = engine.Players[snapshot.ActivePlayerIndex],
            TurnNumber = snapshot.TurnNumber,
            Phase = Enum.Parse<TurnPhase>(snapshot.Turn.Phase),
            MovementAllowance = snapshot.Turn.MovementAllowance,
            MovementRemaining = snapshot.Turn.MovementRemaining,
            BonusRollAvailable = snapshot.Turn.BonusRollAvailable,
            PendingFeeAmount = snapshot.Turn.PendingFeeAmount,
            SelectedRailroadForSaleIndex = snapshot.Turn.SelectedRailroadForSaleIndex,
            ArrivalResolution = snapshot.Turn.ArrivalResolution is null
                ? null
                : new ArrivalResolution
                {
                    PlayerIndex = snapshot.Turn.ArrivalResolution.PlayerIndex,
                    DestinationCityName = snapshot.Turn.ArrivalResolution.DestinationCityName,
                    PayoutAmount = snapshot.Turn.ArrivalResolution.PayoutAmount,
                    CashAfterPayout = snapshot.Turn.ArrivalResolution.CashAfterPayout,
                    PurchaseOpportunityAvailable = snapshot.Turn.ArrivalResolution.PurchaseOpportunityAvailable,
                    Message = snapshot.Turn.ArrivalResolution.Message
                },
            ForcedSaleState = snapshot.Turn.ForcedSale is null
                ? null
                : new ForcedSaleState
                {
                    AmountOwed = snapshot.Turn.ForcedSale.AmountOwed,
                    CashBeforeFees = snapshot.Turn.ForcedSale.CashBeforeFees,
                    CashAfterLastSale = snapshot.Turn.ForcedSale.CashAfterLastSale,
                    SalesCompletedCount = snapshot.Turn.ForcedSale.SalesCompletedCount,
                    CanPayNow = snapshot.Turn.ForcedSale.CanPayNow,
                    EliminationTriggered = snapshot.Turn.ForcedSale.EliminationTriggered
                },
            AuctionState = snapshot.Turn.Auction is null
                ? null
                : new AuctionState
                {
                    RailroadIndex = snapshot.Turn.Auction.RailroadIndex,
                    RailroadName = snapshot.Turn.Auction.RailroadName,
                    SellerPlayerIndex = snapshot.Turn.Auction.SellerPlayerIndex,
                    SellerPlayerName = snapshot.Turn.Auction.SellerPlayerName,
                    StartingPrice = snapshot.Turn.Auction.StartingPrice,
                    CurrentBid = snapshot.Turn.Auction.CurrentBid,
                    LastBidderPlayerIndex = snapshot.Turn.Auction.LastBidderPlayerIndex,
                    CurrentBidderPlayerIndex = snapshot.Turn.Auction.CurrentBidderPlayerIndex,
                    RoundNumber = snapshot.Turn.Auction.RoundNumber,
                    ConsecutiveNoBidTurnCount = snapshot.Turn.Auction.ConsecutiveNoBidTurnCount,
                    Status = Enum.TryParse<AuctionStatus>(snapshot.Turn.Auction.Status, out var parsedStatus)
                        ? parsedStatus
                        : AuctionStatus.Open,
                    Participants = snapshot.Turn.Auction.Participants
                        .Select(participant => new AuctionParticipant
                        {
                            PlayerIndex = participant.PlayerIndex,
                            PlayerName = participant.PlayerName,
                            CashOnHand = participant.CashOnHand,
                            LastBidAmount = participant.LastBidAmount,
                            IsEligible = participant.IsEligible,
                            HasDroppedOut = participant.HasDroppedOut,
                            HasPassedThisRound = participant.HasPassedThisRound,
                            LastAction = Enum.TryParse<AuctionParticipantAction>(participant.LastAction, out var parsedAction)
                                ? parsedAction
                                : AuctionParticipantAction.None
                        })
                        .ToList()
                },
            PendingRegionChoice = snapshot.Turn.PendingRegionChoice is null
                ? null
                : new PendingRegionChoice
                {
                    PlayerIndex = snapshot.Turn.PendingRegionChoice.PlayerIndex,
                    CurrentCityName = snapshot.Turn.PendingRegionChoice.CurrentCityName,
                    CurrentRegionCode = snapshot.Turn.PendingRegionChoice.CurrentRegionCode,
                    TriggeredByInitialRegionCode = snapshot.Turn.PendingRegionChoice.TriggeredByInitialRegionCode,
                    AssignmentKind = Enum.TryParse<PendingDestinationAssignmentKind>(snapshot.Turn.PendingRegionChoice.AssignmentKind, out var assignmentKind)
                        ? assignmentKind
                        : PendingDestinationAssignmentKind.NormalDestination,
                    EligibleRegionCodes = snapshot.Turn.PendingRegionChoice.EligibleRegionCodes,
                    EligibleCityCountsByRegion = snapshot.Turn.PendingRegionChoice.EligibleCityCountsByRegion
                },
            PendingHomeCityChoice = snapshot.Turn.PendingHomeCityChoice is null
                ? null
                : new PendingHomeCityChoice
                {
                    PlayerIndex = snapshot.Turn.PendingHomeCityChoice.PlayerIndex,
                    RegionCode = snapshot.Turn.PendingHomeCityChoice.RegionCode,
                    RegionName = snapshot.Turn.PendingHomeCityChoice.RegionName,
                    CurrentHomeCityName = snapshot.Turn.PendingHomeCityChoice.CurrentHomeCityName,
                    EligibleCityNames = snapshot.Turn.PendingHomeCityChoice.EligibleCityNames
                },
            PendingHomeSwap = snapshot.Turn.PendingHomeSwap is null
                ? null
                : new PendingHomeSwap
                {
                    PlayerIndex = snapshot.Turn.PendingHomeSwap.PlayerIndex,
                    CurrentHomeCityName = snapshot.Turn.PendingHomeSwap.CurrentHomeCityName,
                    FirstDestinationCityName = snapshot.Turn.PendingHomeSwap.FirstDestinationCityName
                }
        };

        if (snapshot.Turn.DiceResult != null)
        {
            engine._currentTurn.DiceResult = new DiceResult(
                snapshot.Turn.DiceResult.WhiteDice,
                snapshot.Turn.DiceResult.RedDie);
        }

        foreach (var rrIdx in snapshot.Turn.RailroadsRiddenThisTurn)
            engine._currentTurn.RailroadsRiddenThisTurn.Add(rrIdx);

        foreach (var rrIdx in snapshot.Turn.RailroadsRequiringFullOwnerRateThisTurn)
            engine._currentTurn.RailroadsRequiringFullOwnerRateThisTurn.Add(rrIdx);

        engine._gameStatus = Enum.Parse<GameStatus>(snapshot.GameStatus);
        engine._allRailroadsSold = snapshot.AllRailroadsSold;

        if (snapshot.WinnerIndex.HasValue && snapshot.WinnerIndex.Value < engine.Players.Count)
            engine._winner = engine.Players[snapshot.WinnerIndex.Value];

        return engine;
    }

    public static GameEngine FromSnapshot(GameState snapshot, MapDefinition mapDefinition, IRandomProvider randomProvider, int superchiefPrice = 40_000)
    {
        return FromSnapshot(snapshot, mapDefinition, randomProvider, GameSettings.Default with { SuperchiefPrice = superchiefPrice });
    }

    private static string SerializeSelectedRouteSegment(string fromNodeId, string toNodeId, int railroadIndex)
    {
        return string.Concat(fromNodeId, "|", toNodeId, "|", railroadIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public static int GetRailroadPurchasePrice(int railroadIndex)
    {
        if (railroadIndex == 0)
        {
            return RailroadPrices[0];
        }

        if (railroadIndex > 0 && railroadIndex <= RailroadPrices.Length)
        {
            return RailroadPrices[railroadIndex - 1];
        }

        return 10_000;
    }

    public static int GetUpgradeCost(LocomotiveType currentEngineType, LocomotiveType targetEngineType, GameSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return (currentEngineType, targetEngineType) switch
        {
            (LocomotiveType.Freight, LocomotiveType.Express) => settings.ExpressPrice,
            (LocomotiveType.Freight, LocomotiveType.Superchief) => settings.SuperchiefPrice,
            (LocomotiveType.Express, LocomotiveType.Superchief) => settings.SuperchiefPrice,
            _ => -1
        };
    }

    public static int GetUpgradeCost(LocomotiveType currentEngineType, LocomotiveType targetEngineType, int superchiefPrice)
    {
        return GetUpgradeCost(
            currentEngineType,
            targetEngineType,
            GameSettings.Default with { SuperchiefPrice = superchiefPrice });
    }

    public static int GetPublicFee(GameSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return settings.PublicFee;
    }

    public static int GetPrivateFee(GameSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return settings.PrivateFee;
    }

    public static int GetUnfriendlyFee(GameSettings settings, bool allRailroadsSold)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return allRailroadsSold ? settings.UnfriendlyFee2 : settings.UnfriendlyFee1;
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

    private CityDefinition DrawRandomCityFromRegion(string regionCode)
    {
        var citiesInRegion = MapDefinition.Cities
            .Where(city => string.Equals(city.RegionCode, regionCode, StringComparison.OrdinalIgnoreCase)
                && city.Probability.HasValue
                && city.Probability.Value > 0)
            .ToList();

        if (citiesInRegion.Count == 0)
        {
            throw new InvalidOperationException($"Region '{regionCode}' has no weighted destination city candidates.");
        }

        var cityProbabilities = citiesInRegion.Select(city => city.Probability!.Value).ToList();
        var cityIndex = _randomProvider.WeightedDraw(cityProbabilities);
        return citiesInRegion[cityIndex];
    }

    private PendingRegionChoice? BuildPendingRegionChoice(Player player, string triggeredRegionCode, PendingDestinationAssignmentKind assignmentKind)
    {
        var currentRegionCode = player.CurrentCity.RegionCode;
        var eligibleRegions = MapDefinition.Regions
            .Select(region => new
            {
                region.Code,
                EligibleCityCount = MapDefinition.Cities.Count(city => string.Equals(city.RegionCode, region.Code, StringComparison.OrdinalIgnoreCase)
                    && city.Probability.HasValue
                    && city.Probability.Value > 0)
            })
            .Where(region => region.EligibleCityCount > 0)
            .ToList();

        if (eligibleRegions.Count == 0)
        {
            return null;
        }

        return new PendingRegionChoice
        {
            PlayerIndex = player.Index,
            CurrentCityName = player.CurrentCity.Name,
            CurrentRegionCode = currentRegionCode,
            TriggeredByInitialRegionCode = triggeredRegionCode,
            AssignmentKind = assignmentKind,
            EligibleRegionCodes = eligibleRegions.Select(region => region.Code).ToList(),
            EligibleCityCountsByRegion = eligibleRegions.ToDictionary(
                region => region.Code,
                region => region.EligibleCityCount,
                StringComparer.OrdinalIgnoreCase)
        };
    }

    private void FinalizeDestinationAssignment(Player player, CityDefinition city, PendingDestinationAssignmentKind assignmentKind)
    {
        if (assignmentKind == PendingDestinationAssignmentKind.DeclaredAlternateDestination)
        {
            FinalizeDeclaredAlternateDestinationAssignment(player, city);
            return;
        }

        if (string.Equals(city.RegionCode, player.CurrentCity.RegionCode, StringComparison.OrdinalIgnoreCase)
            && string.Equals(city.Name, player.CurrentCity.Name, StringComparison.OrdinalIgnoreCase))
        {
            player.Destination = null;
            player.TripOriginCity = null;
            CurrentTurn.Phase = TurnPhase.EndTurn;
            return;
        }

        player.Destination = city;
        player.TripOriginCity = player.CurrentCity;
        player.ActiveRoute = null;
        player.RouteProgressIndex = 0;
        player.UsedSegments.Clear();
        DestinationAssigned?.Invoke(this, new DestinationAssignedEventArgs(player, city));

        if (_settings.HomeSwapping && !player.HasResolvedHomeSwap)
        {
            CurrentTurn.PendingHomeSwap = new PendingHomeSwap
            {
                PlayerIndex = player.Index,
                CurrentHomeCityName = player.HomeCity.Name,
                FirstDestinationCityName = city.Name
            };
            CurrentTurn.Phase = TurnPhase.HomeSwap;
            return;
        }

        CurrentTurn.Phase = HasPreparedBonusMove()
            ? TurnPhase.Move
            : TurnPhase.Roll;
    }

    private void HandleArrival(Player player, int actualSteps)
    {
        var diceResult = CurrentTurn.DiceResult;
        var whiteDiceTotal = diceResult?.WhiteDice.Sum() ?? 0;
        var hasUnusedSuperchiefRedDie = player.LocomotiveType == LocomotiveType.Superchief
            && diceResult?.RedDie is int
            && actualSteps <= whiteDiceTotal;
        var pendingFees = CalculatePendingFeeAmount(player);

        CurrentTurn.BonusRollAvailable = CurrentTurn.BonusRollAvailable || hasUnusedSuperchiefRedDie;

        // Calculate payout
        int payout = 0;
        var tripOriginCity = player.TripOriginCity ?? player.CurrentCity;
        var arrivingHomeWhileDeclared = player.HasDeclared
            && string.Equals(player.Destination?.Name, player.HomeCity.Name, StringComparison.OrdinalIgnoreCase);
        var shouldAwardPayout = !arrivingHomeWhileDeclared
            || string.Equals(player.AlternateDestination?.Name, player.HomeCity.Name, StringComparison.OrdinalIgnoreCase);
        if (shouldAwardPayout
            && player.Destination != null
            && tripOriginCity.PayoutIndex.HasValue
            && player.Destination.PayoutIndex.HasValue)
        {
            var originPayoutIndex = tripOriginCity.PayoutIndex.Value;
            var destinationPayoutIndex = player.Destination.PayoutIndex.Value;
            if (!MapDefinition.TryGetPayout(originPayoutIndex, destinationPayoutIndex, out payout))
            {
                throw new InvalidOperationException(
                    $"Map payout chart does not define a payout from '{tripOriginCity.Name}' ({originPayoutIndex}) to '{player.Destination.Name}' ({destinationPayoutIndex}).");
            }
        }

        player.Cash += payout;

        var destination = player.Destination!;
        CurrentTurn.ArrivalResolution = new ArrivalResolution
        {
            PlayerIndex = player.Index,
            DestinationCityName = destination.Name,
            PayoutAmount = payout,
            CashAfterPayout = player.Cash,
            PurchaseOpportunityAvailable = !arrivingHomeWhileDeclared,
            Message = arrivingHomeWhileDeclared
                ? $"{player.Name} reached home, collected ${payout:N0}, and has ${pendingFees:N0} in fees due before victory is confirmed."
                : $"{player.Name} reached {destination.Name}, collected ${payout:N0}, and has ${pendingFees:N0} in fees due."
        };
        player.Destination = null;
        player.ActiveRoute = null;
        player.UsedSegments.Clear();

        DestinationReached?.Invoke(this, new DestinationReachedEventArgs(player, destination, payout, player.Cash, purchaseOpportunityAvailable: !arrivingHomeWhileDeclared));

        if (!player.HasDeclared
            && player.Cash >= _settings.WinningCash &&
            string.Equals(player.CurrentCity.Name, player.HomeCity.Name, StringComparison.OrdinalIgnoreCase))
        {
            Winner = player;
            GameStatus = GameStatus.Completed;
            GameOver?.Invoke(this, new GameOverEventArgs(player));
            return;
        }

        if (arrivingHomeWhileDeclared)
        {
            CurrentTurn.Phase = TurnPhase.UseFees;
            ResolveUseFees();
            return;
        }

        player.TripOriginCity = null;
        CurrentTurn.Phase = TurnPhase.Purchase;
    }

    private void ContinueAfterPurchase(Player player)
    {
        if (CurrentTurn.BonusRollAvailable)
        {
            PrepareBonusMove(player);

            if (player.Destination is null)
            {
                CurrentTurn.Phase = TurnPhase.DrawDestination;
                return;
            }

            CurrentTurn.Phase = TurnPhase.Move;
            return;
        }

        CurrentTurn.Phase = TurnPhase.UseFees;
        ResolveUseFees();
    }

    private void StartBonusMove(Player player)
    {
        PrepareBonusMove(player);
        CurrentTurn.Phase = TurnPhase.Move;
    }

    private void PrepareBonusMove(Player player)
    {
        int bonusMovement;

        if (player.LocomotiveType == LocomotiveType.Superchief
            && CurrentTurn.DiceResult?.RedDie is int preservedRedDie
            && (CurrentTurn.DiceResult.WhiteDice?.Sum() ?? 0) > 0)
        {
            bonusMovement = preservedRedDie;
        }
        else
        {
            bonusMovement = _randomProvider.RollDiceIndividual(1)[0];
        }

        CurrentTurn.DiceResult = new DiceResult([0, 0], bonusMovement);
        CurrentTurn.MovementAllowance = bonusMovement;
        CurrentTurn.MovementRemaining = bonusMovement;
        CurrentTurn.BonusRollAvailable = false;
        CurrentTurn.ArrivalResolution = null;
    }

    private bool HasPreparedBonusMove()
    {
        return CurrentTurn.DiceResult?.RedDie is int
            && CurrentTurn.WhiteDiceAreCleared()
            && CurrentTurn.MovementAllowance > 0
            && CurrentTurn.MovementRemaining >= 0;
    }

    private void ResolveUseFees()
    {
        var player = CurrentTurn.ActivePlayer;
        var feeCalculation = BuildFeeCalculation(player, CurrentTurn.RailroadsRiddenThisTurn);
        var pendingFeeAmount = CalculatePendingFeeAmount(player);
        CurrentTurn.PendingFeeAmount = pendingFeeAmount;

        if (!feeCalculation.HasCharges || pendingFeeAmount <= 0)
        {
            ResolvePostFeeState(player);
            ClearForcedSaleContext();
            CurrentTurn.Phase = TurnPhase.EndTurn;
            return;
        }

        if (player.Cash < pendingFeeAmount)
        {
            BeginForcedSale(player, pendingFeeAmount);
            return;
        }

        if (feeCalculation.UsesPublicFee)
        {
            var fee = GetPublicFee(_settings);
            player.Cash -= fee;
            player.TotalFeesPaid += fee;
            UsageFeeCharged?.Invoke(this, new UsageFeeChargedEventArgs(player, null, fee, []));
        }

        if (feeCalculation.UsesPrivateFee)
        {
            var fee = GetPrivateFee(_settings);
            player.Cash -= fee;
            player.TotalFeesPaid += fee;
            UsageFeeCharged?.Invoke(this, new UsageFeeChargedEventArgs(player, null, fee, []));
        }

        foreach (var feeBucket in feeCalculation.OpponentBuckets.Values.OrderBy(bucket => bucket.Owner?.Index ?? -1))
        {
            var fee = feeBucket.FeeAmount;

            player.Cash -= fee;
            player.TotalFeesPaid += fee;
            if (feeBucket.Owner is not null)
            {
                feeBucket.Owner.Cash += fee;
                feeBucket.Owner.TotalFeesCollected += fee;
                player.FeesPaidToPlayers[feeBucket.Owner.Index] = player.FeesPaidToPlayers.GetValueOrDefault(feeBucket.Owner.Index) + fee;
            }

            UsageFeeCharged?.Invoke(this, new UsageFeeChargedEventArgs(player, feeBucket.Owner, fee, feeBucket.Railroads));
        }

        ResolvePostFeeState(player);
        ClearForcedSaleContext();
        CurrentTurn.Phase = TurnPhase.EndTurn;
    }

    private void ResolvePostFeeState(Player player)
    {
        if (player.HasDeclared && player.Cash < _settings.WinningCash)
        {
            UndeclarePlayer(player, payRoverFeeTo: null);
            return;
        }

        if (player.HasDeclared
            && string.Equals(player.CurrentCity.Name, player.HomeCity.Name, StringComparison.OrdinalIgnoreCase)
            && player.Cash >= _settings.WinningCash)
        {
            player.TripOriginCity = null;
            Winner = player;
            GameStatus = GameStatus.Completed;
            GameOver?.Invoke(this, new GameOverEventArgs(player));
        }
    }

    private void PrepareTurnForActivePlayer(Player player)
    {
        CurrentTurn.PendingRegionChoice = null;
        CurrentTurn.PendingHomeCityChoice = null;
        CurrentTurn.PendingHomeSwap = null;

        if (player.PendingImmediateArrival && player.Destination is not null)
        {
            player.PendingImmediateArrival = false;
            HandleArrival(player, 0);
            return;
        }

        if (_settings.HomeCityChoice && !player.HasResolvedHomeCityChoice)
        {
            CurrentTurn.PendingHomeCityChoice = BuildPendingHomeCityChoice(player);
            if (CurrentTurn.PendingHomeCityChoice is null)
            {
                player.HasResolvedHomeCityChoice = true;
                CurrentTurn.Phase = player.Destination is not null
                    ? TurnPhase.Roll
                    : TurnPhase.DrawDestination;
                return;
            }

            CurrentTurn.Phase = TurnPhase.HomeCityChoice;
            return;
        }

        CurrentTurn.Phase = player.Destination != null
            ? TurnPhase.Roll
            : TurnPhase.DrawDestination;
    }

    private PendingHomeCityChoice? BuildPendingHomeCityChoice(Player player)
    {
        var regionCode = player.HomeCity.RegionCode;
        var regionName = MapDefinition.Regions.FirstOrDefault(region => string.Equals(region.Code, regionCode, StringComparison.OrdinalIgnoreCase))?.Name
            ?? regionCode;
        var claimedHomeCities = Players
            .Where(otherPlayer => otherPlayer != player)
            .Select(otherPlayer => otherPlayer.HomeCity.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var eligibleCityNames = MapDefinition.Cities
            .Where(city => string.Equals(city.RegionCode, regionCode, StringComparison.OrdinalIgnoreCase))
            .Select(city => city.Name)
            .Where(cityName => !claimedHomeCities.Contains(cityName) || string.Equals(cityName, player.HomeCity.Name, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(cityName => cityName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return eligibleCityNames.Count == 0
            ? null
            : new PendingHomeCityChoice
            {
                PlayerIndex = player.Index,
                RegionCode = regionCode,
                RegionName = regionName,
                CurrentHomeCityName = player.HomeCity.Name,
                EligibleCityNames = eligibleCityNames
            };
    }

    private void FinalizeDeclaredAlternateDestinationAssignment(Player player, CityDefinition city)
    {
        player.AlternateDestination = city;
        player.Destination = player.HomeCity;
        player.ActiveRoute = null;
        player.RouteProgressIndex = 0;

        DestinationAssigned?.Invoke(this, new DestinationAssignedEventArgs(player, city));

        CurrentTurn.Phase = string.Equals(city.Name, player.CurrentCity.Name, StringComparison.OrdinalIgnoreCase)
            ? TurnPhase.EndTurn
            : HasPreparedBonusMove()
                ? TurnPhase.Move
                : TurnPhase.Roll;
    }

    private void TryResolveRovers(Player movingPlayer, List<string> traversedNodeIds)
    {
        if (traversedNodeIds.Count == 0)
        {
            return;
        }

        var traversedNodeSet = traversedNodeIds
            .Where(nodeId => !string.IsNullOrWhiteSpace(nodeId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var declaredPlayer in Players.Where(candidate =>
                     candidate != movingPlayer
                     && candidate.HasDeclared
                     && !string.IsNullOrWhiteSpace(candidate.CurrentNodeId)
                     && traversedNodeSet.Contains(candidate.CurrentNodeId!)))
        {
            UndeclarePlayer(declaredPlayer, movingPlayer);
        }
    }

    private void UndeclarePlayer(Player player, Player? payRoverFeeTo)
    {
        if (!player.HasDeclared)
        {
            return;
        }

        player.HasDeclared = false;

        if (payRoverFeeTo is not null)
        {
            player.Cash -= _settings.RoverCash;
            payRoverFeeTo.Cash += _settings.RoverCash;
        }

        player.Destination = player.AlternateDestination;
        player.AlternateDestination = null;
        player.ActiveRoute = null;
        player.RouteProgressIndex = 0;

        if (player.Destination is not null
            && string.Equals(player.CurrentCity.Name, player.Destination.Name, StringComparison.OrdinalIgnoreCase))
        {
            player.PendingImmediateArrival = true;
        }
    }

    private void SetPlayerHomeCity(Player player, CityDefinition homeCity)
    {
        player.HomeCity = homeCity;
        player.CurrentCity = homeCity;
        player.CurrentNodeId = FindCityNodeId(homeCity)
            ?? throw new InvalidOperationException($"Unable to resolve a node for home city '{homeCity.Name}'.");
    }

    private void BeginForcedSale(Player player, int amountOwed)
    {
        CurrentTurn.PendingFeeAmount = amountOwed;

        if (player.OwnedRailroads.Count == 0)
        {
            EliminatePlayerForUnpaidFees(player, amountOwed, player.Cash, salesCompletedCount: 0);
            return;
        }

        CurrentTurn.ForcedSaleState = new ForcedSaleState
        {
            AmountOwed = amountOwed,
            CashBeforeFees = player.Cash,
            CashAfterLastSale = player.Cash,
            SalesCompletedCount = 0,
            CanPayNow = false,
            EliminationTriggered = false
        };
        CurrentTurn.AuctionState = null;
        CurrentTurn.SelectedRailroadForSaleIndex = player.OwnedRailroads
            .OrderBy(ownedRailroad => ownedRailroad.Name, StringComparer.OrdinalIgnoreCase)
            .Select(ownedRailroad => (int?)ownedRailroad.Index)
            .FirstOrDefault();
        CurrentTurn.Phase = TurnPhase.UseFees;
    }

    private void EliminatePlayerForUnpaidFees(Player player, int amountOwed, int cashBeforeFees, int salesCompletedCount)
    {
        CurrentTurn.ForcedSaleState = new ForcedSaleState
        {
            AmountOwed = amountOwed,
            CashBeforeFees = cashBeforeFees,
            CashAfterLastSale = player.Cash,
            SalesCompletedCount = salesCompletedCount,
            CanPayNow = false,
            EliminationTriggered = true
        };
        CurrentTurn.AuctionState = null;
        CurrentTurn.SelectedRailroadForSaleIndex = null;
        HandleBankruptcy(player);
        CurrentTurn.Phase = TurnPhase.EndTurn;
    }

    private void HandleBankruptcy(Player player)
    {
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
    }

    private void ClearForcedSaleContext()
    {
        CurrentTurn.PendingFeeAmount = 0;
        CurrentTurn.SelectedRailroadForSaleIndex = null;
        CurrentTurn.ForcedSaleState = null;
        CurrentTurn.AuctionState = null;
    }

    private AuctionState RequireOpenAuctionState(Railroad railroad)
    {
        var auctionState = CurrentTurn.AuctionState
            ?? throw new InvalidOperationException("There is no active auction.");

        if (auctionState.Status != AuctionStatus.Open)
            throw new InvalidOperationException("The auction is no longer open.");

        if (auctionState.RailroadIndex != railroad.Index)
            throw new InvalidOperationException("This railroad is not the subject of the active auction.");

        return auctionState;
    }

    private static void RequireCurrentAuctionParticipant(AuctionState auctionState, Player player)
    {
        if (auctionState.CurrentBidderPlayerIndex != player.Index)
            throw new InvalidOperationException("It is not this player's turn in the auction.");

        var participant = auctionState.Participants.FirstOrDefault(entry => entry.PlayerIndex == player.Index)
            ?? throw new InvalidOperationException("The player is not participating in this auction.");

        if (participant.HasDroppedOut || !participant.IsEligible)
            throw new InvalidOperationException("The player is not eligible to act in this auction.");
    }

    private void EvaluateAuctionCompletion(Railroad railroad)
    {
        var auctionState = CurrentTurn.AuctionState;
        if (auctionState is null)
        {
            return;
        }

        var remainingParticipants = auctionState.Participants
            .Where(participant => participant.IsEligible && !participant.HasDroppedOut)
            .ToList();

        if (auctionState.LastBidderPlayerIndex is null)
        {
            if (remainingParticipants.Count == 0 || remainingParticipants.All(participant => participant.HasPassedThisRound))
            {
                CompleteAuctionWithBankFallback(railroad);
            }

            return;
        }

        var nonLeadingParticipants = remainingParticipants
            .Where(participant => participant.PlayerIndex != auctionState.LastBidderPlayerIndex.Value)
            .ToList();

        if (nonLeadingParticipants.Count == 0 || nonLeadingParticipants.All(participant => participant.HasPassedThisRound))
        {
            var winner = Players[auctionState.LastBidderPlayerIndex.Value];
            CompleteAuctionWithWinner(railroad, winner, auctionState.CurrentBid);
        }
    }

    private List<AuctionParticipant> UpdateAuctionParticipantAffordability(
        List<AuctionParticipant> participants,
        int requiredBid,
        int? leaderPlayerIndex)
    {
        return participants
            .Select(participant =>
            {
                var cashOnHand = Players[participant.PlayerIndex].Cash;
                var shouldMarkAutoDrop = !participant.HasDroppedOut
                    && participant.IsEligible
                    && participant.PlayerIndex != leaderPlayerIndex
                    && cashOnHand < requiredBid;

                return new AuctionParticipant
                {
                    PlayerIndex = participant.PlayerIndex,
                    PlayerName = participant.PlayerName,
                    CashOnHand = cashOnHand,
                    LastBidAmount = participant.LastBidAmount,
                    IsEligible = participant.IsEligible && !shouldMarkAutoDrop,
                    HasDroppedOut = participant.HasDroppedOut || shouldMarkAutoDrop,
                    HasPassedThisRound = participant.HasPassedThisRound || shouldMarkAutoDrop,
                    LastAction = shouldMarkAutoDrop ? AuctionParticipantAction.AutoDropOut : participant.LastAction
                };
            })
            .ToList();
    }

    private void CompleteAuctionWithWinner(Railroad railroad, Player winner, int winningBid)
    {
        CurrentTurn.AuctionState = CurrentTurn.AuctionState is null
            ? null
            : new AuctionState
            {
                RailroadIndex = CurrentTurn.AuctionState.RailroadIndex,
                RailroadName = CurrentTurn.AuctionState.RailroadName,
                SellerPlayerIndex = CurrentTurn.AuctionState.SellerPlayerIndex,
                SellerPlayerName = CurrentTurn.AuctionState.SellerPlayerName,
                StartingPrice = CurrentTurn.AuctionState.StartingPrice,
                CurrentBid = winningBid,
                LastBidderPlayerIndex = winner.Index,
                CurrentBidderPlayerIndex = null,
                RoundNumber = CurrentTurn.AuctionState.RoundNumber,
                ConsecutiveNoBidTurnCount = CurrentTurn.AuctionState.ConsecutiveNoBidTurnCount,
                Status = AuctionStatus.Awarded,
                Participants = CurrentTurn.AuctionState.Participants
            };

        ResolveAuction(railroad, winner, winningBid);
        CurrentTurn.AuctionState = null;

        if (CurrentTurn.Phase == TurnPhase.UseFees)
        {
            ResolveUseFees();
        }
    }

    private void CompleteAuctionWithBankFallback(Railroad railroad)
    {
        CurrentTurn.AuctionState = CurrentTurn.AuctionState is null
            ? null
            : new AuctionState
            {
                RailroadIndex = CurrentTurn.AuctionState.RailroadIndex,
                RailroadName = CurrentTurn.AuctionState.RailroadName,
                SellerPlayerIndex = CurrentTurn.AuctionState.SellerPlayerIndex,
                SellerPlayerName = CurrentTurn.AuctionState.SellerPlayerName,
                StartingPrice = CurrentTurn.AuctionState.StartingPrice,
                CurrentBid = 0,
                LastBidderPlayerIndex = null,
                CurrentBidderPlayerIndex = null,
                RoundNumber = CurrentTurn.AuctionState.RoundNumber,
                ConsecutiveNoBidTurnCount = CurrentTurn.AuctionState.ConsecutiveNoBidTurnCount,
                Status = AuctionStatus.BankFallback,
                Participants = CurrentTurn.AuctionState.Participants
            };

        if (CurrentTurn.Phase == TurnPhase.UseFees)
        {
            CurrentTurn.AuctionState = null;
            SellRailroadToBank(railroad);
            return;
        }

        ResolveAuction(railroad, winner: null, winningBid: 0);
        CurrentTurn.AuctionState = null;
    }

    private static int? GetNextOpenParticipantIndex(List<AuctionParticipant> participants, int currentPlayerIndex)
    {
        if (participants.Count == 0)
        {
            return null;
        }

        var currentPosition = participants
            .Select((participant, index) => new { participant, index })
            .Where(entry => entry.participant.PlayerIndex == currentPlayerIndex)
            .Select(entry => entry.index)
            .DefaultIfEmpty(-1)
            .First();

        if (currentPosition < 0)
        {
            return participants.FirstOrDefault(participant => participant.IsEligible && !participant.HasDroppedOut)?.PlayerIndex;
        }

        for (var offset = 1; offset <= participants.Count; offset++)
        {
            var candidate = participants[(currentPosition + offset) % participants.Count];
            if (candidate.IsEligible && !candidate.HasDroppedOut)
            {
                return candidate.PlayerIndex;
            }
        }

        return null;
    }

    private void CheckAllRailroadsSold()
    {
        var allRailroadsSold = Railroads.Where(r => !r.IsPublic).All(r => r.Owner != null);
        if (!AllRailroadsSold && allRailroadsSold)
        {
            GrandfatherPlayersOnOpponentRailroads(GetUnfriendlyFee(_settings, allRailroadsSold: false));
        }

        AllRailroadsSold = allRailroadsSold;
    }

    private int CalculatePendingFeeAmount(Player player)
    {
        var feeCalculation = BuildFeeCalculation(player, CurrentTurn.RailroadsRiddenThisTurn);
        if (!feeCalculation.HasCharges)
        {
            return 0;
        }

        var amount = 0;

        if (feeCalculation.UsesPublicFee)
        {
            amount += GetPublicFee(_settings);
        }

        if (feeCalculation.UsesPrivateFee)
        {
            amount += GetPrivateFee(_settings);
        }

        amount += feeCalculation.OpponentBuckets.Values.Sum(feeBucket => feeBucket.FeeAmount);

        return amount;
    }

    private FeeCalculation BuildFeeCalculation(Player rider, IEnumerable<int> railroadIndices)
    {
        var opponentBuckets = new Dictionary<int, FeeBucket>();
        var usesPublicFee = false;
        var usesPrivateFee = false;

        foreach (var rrIdx in railroadIndices)
        {
            var railroad = Railroads.FirstOrDefault(r => r.Index == rrIdx);
            if (railroad == null)
            {
                continue;
            }

            if (railroad.Owner is null)
            {
                usesPublicFee = true;
                continue;
            }

            if (railroad.Owner == rider)
            {
                usesPrivateFee = true;
                continue;
            }

            var feeAmount = rider.GrandfatheredRailroadFees.TryGetValue(railroad.Index, out var grandfatheredFee)
                ? grandfatheredFee
                : CurrentTurn.RailroadsRequiringFullOwnerRateThisTurn.Contains(railroad.Index)
                    ? GetUnfriendlyFee(_settings, AllRailroadsSold)
                    : GetPrivateFee(_settings);

            AddRailroadToFeeBucket(opponentBuckets, railroad, feeAmount);
        }

        return new FeeCalculation(usesPublicFee, usesPrivateFee, opponentBuckets);
    }

    private static void UpdateGrandfatheredRailroadAccess(Player player, int railroadIndex)
    {
        if (player.GrandfatheredRailroadIndices.Count == 0)
        {
            return;
        }

        if (player.GrandfatheredRailroadIndices.Contains(railroadIndex))
        {
            player.GrandfatheredRailroadIndices.IntersectWith([railroadIndex]);
            foreach (var grandfatheredRailroad in player.GrandfatheredRailroadFees.Keys.Where(index => index != railroadIndex).ToList())
            {
                player.GrandfatheredRailroadFees.Remove(grandfatheredRailroad);
            }
            return;
        }

        player.GrandfatheredRailroadIndices.Clear();
        player.GrandfatheredRailroadFees.Clear();
    }

    private void TrackRailroadFeeRate(Player rider, int railroadIndex)
    {
        var railroad = Railroads.FirstOrDefault(candidate => candidate.Index == railroadIndex);
        if (railroad is null || railroad.Owner is null || railroad.Owner == rider)
        {
            return;
        }

        if (!rider.GrandfatheredRailroadIndices.Contains(railroadIndex))
        {
            CurrentTurn.RailroadsRequiringFullOwnerRateThisTurn.Add(railroadIndex);
        }
    }

    private void UpdateGrandfatheringForOwnershipChange(int railroadIndex, Player? previousOwner, Player? newOwner)
    {
        foreach (var rider in Players)
        {
            rider.GrandfatheredRailroadIndices.Remove(railroadIndex);
            rider.GrandfatheredRailroadFees.Remove(railroadIndex);
        }

        if (newOwner is null)
        {
            return;
        }

        var preservedFee = previousOwner is null
            ? GetPublicFee(_settings)
            : GetUnfriendlyFee(_settings, AllRailroadsSold);

        foreach (var rider in Players.Where(player => player != newOwner && IsPlayerOnRailroadNode(player, railroadIndex)))
        {
            GrantGrandfatheredAccess(rider, railroadIndex, preservedFee);
        }
    }

    private void GrandfatherPlayersOnOpponentRailroads(int preservedFee)
    {
        foreach (var rider in Players.Where(player => !string.IsNullOrWhiteSpace(player.CurrentNodeId)))
        {
            foreach (var railroad in Railroads.Where(railroad =>
                         railroad.Owner is not null
                         && railroad.Owner != rider
                         && IsPlayerOnRailroadNode(rider, railroad.Index)
                         && !rider.GrandfatheredRailroadIndices.Contains(railroad.Index)))
            {
                GrantGrandfatheredAccess(rider, railroad.Index, preservedFee);
            }
        }
    }

    private static void GrantGrandfatheredAccess(Player rider, int railroadIndex, int feeAmount)
    {
        rider.GrandfatheredRailroadIndices.Add(railroadIndex);
        rider.GrandfatheredRailroadFees[railroadIndex] = feeAmount;
    }

    private bool IsPlayerOnRailroadNode(Player player, int railroadIndex)
    {
        if (string.IsNullOrWhiteSpace(player.CurrentNodeId))
        {
            return false;
        }

        return MapDefinition.RailroadRouteSegments.Any(segment =>
            segment.RailroadIndex == railroadIndex
            && (string.Equals(NodeKey(segment.StartRegionIndex, segment.StartDotIndex), player.CurrentNodeId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(NodeKey(segment.EndRegionIndex, segment.EndDotIndex), player.CurrentNodeId, StringComparison.OrdinalIgnoreCase)));
    }

    private static void AddRailroadToFeeBucket(
        Dictionary<int, FeeBucket> feeBuckets,
        Railroad railroad,
        int feeAmount)
    {
        var ownerKey = railroad.Owner!.Index;
        if (!feeBuckets.TryGetValue(ownerKey, out var bucket))
        {
            bucket = new FeeBucket(railroad.Owner, feeAmount);
            feeBuckets[ownerKey] = bucket;
        }

        bucket.Railroads.Add(railroad.Index);
        if (feeAmount > bucket.FeeAmount)
        {
            bucket.FeeAmount = feeAmount;
        }
    }

    private int GetTraversalFee(Player player, Railroad railroad)
    {
        return railroad.Owner switch
        {
            null => GetPublicFee(_settings),
            _ when railroad.Owner == player => GetPrivateFee(_settings),
            _ when player.GrandfatheredRailroadFees.TryGetValue(railroad.Index, out var grandfatheredFee) => grandfatheredFee,
            _ => GetUnfriendlyFee(_settings, AllRailroadsSold)
        };
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
                return NodeKey(region.Index, city.MapDotIndex.Value);
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
        var movementPointsPerTurn = player.LocomotiveType == LocomotiveType.Superchief ? 3 : 2;
        var cityCoverage = BuildRoutePlanningCityCoverage();
        var networkMetricsByPlayerIndex = new Dictionary<int, RoutePlanningNetworkMetrics>();
        var friendlyDestinationReachableNodes = BuildReachableNodesForRoutePlanning(
            destNodeId,
            edge => !IsUnfriendlyRailroadForPlayer(player, edge.RailroadIndex));
        var startState = new RoutePlanningState(startNodeId, LastRailroadIndex: -1, PointsUsedInCurrentTurn: 0);
        var priorityQueue = new PriorityQueue<RoutePlanningState, (int WeightedCost, int TotalCost, int TotalTurns, int TotalSegments)>();
        var bestCosts = new Dictionary<RoutePlanningState, RoutePlanningCostKey>
        {
            [startState] = new RoutePlanningCostKey(0, 0, 0, 0)
        };
        var previous = new Dictionary<RoutePlanningState, RoutePlanningPrevious>();
        var settledStates = new HashSet<RoutePlanningState>();
        RoutePlanningDestinationChoice? bestDestination = null;

        priorityQueue.Enqueue(startState, (0, 0, 0, 0));

        while (priorityQueue.Count > 0)
        {
            var state = priorityQueue.Dequeue();
            if (!bestCosts.TryGetValue(state, out var stateCost) || !settledStates.Add(state))
            {
                continue;
            }

            if (bestDestination is not null && stateCost.WeightedCost > bestDestination.Value.CombinedWeightedCost)
            {
                break;
            }

            if (string.Equals(state.NodeId, destNodeId, StringComparison.OrdinalIgnoreCase))
            {
                if (PathDefersFriendlyExit(player, state, previous, friendlyDestinationReachableNodes))
                {
                    continue;
                }

                var exitAnalysis = CalculateDestinationExitAnalysis(player, state, stateCost, movementPointsPerTurn);
                var tieBreakAnalysis = CalculateTieBreakAnalysis(player, startState, state, previous, cityCoverage, networkMetricsByPlayerIndex);
                var destinationChoice = new RoutePlanningDestinationChoice(state, stateCost, exitAnalysis, tieBreakAnalysis);
                if (bestDestination is null || IsBetterRouteDestination(destinationChoice, bestDestination.Value))
                {
                    bestDestination = destinationChoice;
                }

                continue;
            }

            if (!_adjacency.TryGetValue(state.NodeId, out var edges))
            {
                continue;
            }

            foreach (var edge in edges)
            {
                var segKey = new SegmentKey(edge.FromNodeId, edge.ToNodeId, edge.RailroadIndex);
                if (player.UsedSegments.Contains(segKey))
                {
                    continue;
                }

                if (PathContainsSegment(previous, state, segKey))
                {
                    continue;
                }

                var railroad = Railroads.FirstOrDefault(candidate => candidate.Index == edge.RailroadIndex);
                if (railroad is null)
                {
                    continue;
                }

                var isFirstEdge = state.LastRailroadIndex < 0;
                var exhaustedTurnCapacity = !isFirstEdge && state.PointsUsedInCurrentTurn >= movementPointsPerTurn;
                var startsNewTurn = isFirstEdge || exhaustedTurnCapacity;
                var switchedRailroad = !isFirstEdge && state.LastRailroadIndex != edge.RailroadIndex;
                var turnsAdded = startsNewTurn ? 1 : 0;
                var additionalCost = (startsNewTurn || switchedRailroad) ? GetTraversalFee(player, railroad) : 0;
                var hostileExitPenalty = CalculateHostileExitPenalty(player, edges, edge, friendlyDestinationReachableNodes);
                var nextPointsUsed = startsNewTurn ? 1 : state.PointsUsedInCurrentTurn + 1;
                var nextState = new RoutePlanningState(edge.ToNodeId, edge.RailroadIndex, nextPointsUsed);
                var candidateCost = new RoutePlanningCostKey(
                    TotalCost: stateCost.TotalCost + additionalCost,
                    WeightedCost: stateCost.WeightedCost + additionalCost + RoutePlanningDirectnessPenaltyPerSegment + hostileExitPenalty,
                    TotalTurns: stateCost.TotalTurns + turnsAdded,
                    TotalSegments: stateCost.TotalSegments + 1);

                if (bestCosts.TryGetValue(nextState, out var existingCost)
                    && !IsBetterRouteCost(candidateCost, existingCost))
                {
                    continue;
                }

                bestCosts[nextState] = candidateCost;
                previous[nextState] = new RoutePlanningPrevious(state, edge, additionalCost);
                priorityQueue.Enqueue(nextState, (candidateCost.WeightedCost, candidateCost.TotalCost, candidateCost.TotalTurns, candidateCost.TotalSegments));
            }
        }

        if (bestDestination is null)
        {
            return new Route(new[] { startNodeId }, Array.Empty<RouteSegment>(), 0);
        }

        var nodeIds = new List<string>();
        var segments = new List<RouteSegment>();
        var current = bestDestination.Value.DestinationState;

        nodeIds.Add(current.NodeId);

        while (current != startState)
        {
            if (!previous.TryGetValue(current, out var previousEntry))
            {
                return new Route(new[] { startNodeId }, Array.Empty<RouteSegment>(), 0);
            }

            segments.Add(new RouteSegment
            {
                FromNodeId = previousEntry.Edge.FromNodeId,
                ToNodeId = previousEntry.Edge.ToNodeId,
                RailroadIndex = previousEntry.Edge.RailroadIndex
            });
            current = previousEntry.PreviousState;
            nodeIds.Add(current.NodeId);
        }

        nodeIds.Reverse();
        segments.Reverse();

        return new Route(nodeIds, segments, bestDestination.Value.ArrivalCost.TotalCost);
    }

    private static bool PathContainsSegment(
        Dictionary<RoutePlanningState, RoutePlanningPrevious> previous,
        RoutePlanningState state,
        SegmentKey segmentKey)
    {
        var currentState = state;

        while (previous.TryGetValue(currentState, out var previousEntry))
        {
            if (segmentKey == new SegmentKey(
                    previousEntry.Edge.FromNodeId,
                    previousEntry.Edge.ToNodeId,
                    previousEntry.Edge.RailroadIndex))
            {
                return true;
            }

            currentState = previousEntry.PreviousState;
        }

        return false;
    }

    private HashSet<string> BuildReachableNodesForRoutePlanning(
        string destinationNodeId,
        Func<RouteGraphEdge, bool> canTraverse)
    {
        var reachableNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(destinationNodeId))
        {
            return reachableNodes;
        }

        var queue = new Queue<string>();
        if (reachableNodes.Add(destinationNodeId))
        {
            queue.Enqueue(destinationNodeId);
        }

        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();
            if (!_adjacency.TryGetValue(nodeId, out var outgoingEdges))
            {
                continue;
            }

            foreach (var edge in outgoingEdges)
            {
                if (!canTraverse(edge) || !reachableNodes.Add(edge.ToNodeId))
                {
                    continue;
                }

                queue.Enqueue(edge.ToNodeId);
            }
        }

        return reachableNodes;
    }

    private int CalculateHostileExitPenalty(
        Player player,
        IReadOnlyList<RouteGraphEdge> outgoingEdges,
        RouteGraphEdge candidateEdge,
        HashSet<string> friendlyDestinationReachableNodes)
    {
        if (friendlyDestinationReachableNodes.Count == 0
            || !IsUnfriendlyRailroadForPlayer(player, candidateEdge.RailroadIndex))
        {
            return 0;
        }

        var hasFriendlyConnectedExit = outgoingEdges.Any(edge =>
            !IsUnfriendlyRailroadForPlayer(player, edge.RailroadIndex)
            && friendlyDestinationReachableNodes.Contains(edge.ToNodeId));

        return hasFriendlyConnectedExit ? RoutePlanningHostileExitPenalty : 0;
    }

    private bool PathDefersFriendlyExit(
        Player player,
        RoutePlanningState destinationState,
        Dictionary<RoutePlanningState, RoutePlanningPrevious> previous,
        HashSet<string> friendlyDestinationReachableNodes)
    {
        var currentState = destinationState;

        while (previous.TryGetValue(currentState, out var previousEntry))
        {
            if (IsUnfriendlyRailroadForPlayer(player, previousEntry.Edge.RailroadIndex)
                && _adjacency.TryGetValue(previousEntry.PreviousState.NodeId, out var outgoingEdges)
                && outgoingEdges.Any(edge =>
                    !IsUnfriendlyRailroadForPlayer(player, edge.RailroadIndex)
                    && friendlyDestinationReachableNodes.Contains(edge.ToNodeId)))
            {
                return true;
            }

            currentState = previousEntry.PreviousState;
        }

        return false;
    }

    private RoutePlanningExitAnalysis CalculateDestinationExitAnalysis(
        Player player,
        RoutePlanningState destinationState,
        RoutePlanningCostKey arrivalCost,
        int movementPointsPerTurn)
    {
        var worstCaseExitCost = CalculateDestinationExitCost(player, destinationState, movementPointsPerTurn);
        if (worstCaseExitCost <= 0)
        {
            return new RoutePlanningExitAnalysis(0d, 0, 0d);
        }

        var bonusOutProbability = CalculateBonusOutProbability(player, destinationState, arrivalCost);
        var expectedExitCost = worstCaseExitCost * (1d - bonusOutProbability);
        return new RoutePlanningExitAnalysis(expectedExitCost, worstCaseExitCost, bonusOutProbability);
    }

    private int CalculateDestinationExitCost(Player player, RoutePlanningState destinationState, int movementPointsPerTurn)
    {
        if (destinationState.LastRailroadIndex < 0
            || !IsUnfriendlyRailroadForPlayer(player, destinationState.LastRailroadIndex)
            || !IsUnfriendlyDestinationForPlayer(player, destinationState.NodeId))
        {
            return 0;
        }

        var startState = new RoutePlanningState(destinationState.NodeId, destinationState.LastRailroadIndex, movementPointsPerTurn);
        var priorityQueue = new PriorityQueue<RoutePlanningState, (int WeightedCost, int TotalCost, int TotalTurns, int TotalSegments)>();
        var bestCosts = new Dictionary<RoutePlanningState, RoutePlanningCostKey>
        {
            [startState] = new RoutePlanningCostKey(0, 0, 0, 0)
        };
        var settledStates = new HashSet<RoutePlanningState>();

        priorityQueue.Enqueue(startState, (0, 0, 0, 0));

        while (priorityQueue.Count > 0)
        {
            var state = priorityQueue.Dequeue();
            if (!bestCosts.TryGetValue(state, out var stateCost) || !settledStates.Add(state))
            {
                continue;
            }

            if (state != startState && !IsUnfriendlyRailroadForPlayer(player, state.LastRailroadIndex))
            {
                return stateCost.TotalCost;
            }

            if (!_adjacency.TryGetValue(state.NodeId, out var edges))
            {
                continue;
            }

            foreach (var edge in edges)
            {
                var railroad = Railroads.FirstOrDefault(candidate => candidate.Index == edge.RailroadIndex);
                if (railroad is null)
                {
                    continue;
                }

                var exhaustedTurnCapacity = state.PointsUsedInCurrentTurn >= movementPointsPerTurn;
                var startsNewTurn = exhaustedTurnCapacity;
                var switchedRailroad = state.LastRailroadIndex != edge.RailroadIndex;
                var turnsAdded = startsNewTurn ? 1 : 0;
                var additionalCost = (startsNewTurn || switchedRailroad) ? GetTraversalFee(player, railroad) : 0;
                var nextPointsUsed = startsNewTurn ? 1 : state.PointsUsedInCurrentTurn + 1;
                var nextState = new RoutePlanningState(edge.ToNodeId, edge.RailroadIndex, nextPointsUsed);
                var candidateCost = new RoutePlanningCostKey(
                    TotalCost: stateCost.TotalCost + additionalCost,
                    WeightedCost: stateCost.WeightedCost + additionalCost,
                    TotalTurns: stateCost.TotalTurns + turnsAdded,
                    TotalSegments: stateCost.TotalSegments + 1);

                if (bestCosts.TryGetValue(nextState, out var existingCost)
                    && !IsBetterRouteCost(candidateCost, existingCost))
                {
                    continue;
                }

                bestCosts[nextState] = candidateCost;
                priorityQueue.Enqueue(nextState, (candidateCost.WeightedCost, candidateCost.TotalCost, candidateCost.TotalTurns, candidateCost.TotalSegments));
            }
        }

        return int.MaxValue / 4;
    }

    private double CalculateBonusOutProbability(Player player, RoutePlanningState destinationState, RoutePlanningCostKey arrivalCost)
    {
        if (player != CurrentTurn.ActivePlayer
            || CurrentTurn.Phase != TurnPhase.Move
            || arrivalCost.TotalTurns != 1)
        {
            return 0d;
        }

        var minimumEscapeSegments = CalculateMinimumBonusOutSegments(player, destinationState);
        if (!minimumEscapeSegments.HasValue)
        {
            return 0d;
        }

        var whiteDiceTotal = CurrentTurn.DiceResult?.WhiteDice.Sum() ?? 0;
        if (player.LocomotiveType == LocomotiveType.Superchief
            && CurrentTurn.DiceResult?.RedDie is int redDie
            && whiteDiceTotal > 0)
        {
            return arrivalCost.TotalSegments <= whiteDiceTotal && redDie >= minimumEscapeSegments.Value
                ? 1d
                : 0d;
        }

        if (!CurrentTurn.BonusRollAvailable)
        {
            return 0d;
        }

        var successCount = Enumerable.Range(1, 6).Count(distance => distance >= minimumEscapeSegments.Value);
        return successCount / 6d;
    }

    private int? CalculateMinimumBonusOutSegments(Player player, RoutePlanningState destinationState)
    {
        if (destinationState.LastRailroadIndex < 0 || !IsUnfriendlyRailroadForPlayer(player, destinationState.LastRailroadIndex))
        {
            return 0;
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            BuildNodeRailroadKey(destinationState.NodeId, destinationState.LastRailroadIndex)
        };
        var queue = new Queue<(string NodeId, int RailroadIndex, int SegmentsUsed)>();
        queue.Enqueue((destinationState.NodeId, destinationState.LastRailroadIndex, 0));

        while (queue.Count > 0)
        {
            var state = queue.Dequeue();
            if (!_adjacency.TryGetValue(state.NodeId, out var edges))
            {
                continue;
            }

            foreach (var edge in edges)
            {
                var nextSegmentsUsed = state.SegmentsUsed + 1;
                if (!IsUnfriendlyRailroadForPlayer(player, edge.RailroadIndex))
                {
                    return nextSegmentsUsed;
                }

                if (edge.RailroadIndex != destinationState.LastRailroadIndex)
                {
                    continue;
                }

                var nextStateKey = BuildNodeRailroadKey(edge.ToNodeId, edge.RailroadIndex);
                if (visited.Add(nextStateKey))
                {
                    queue.Enqueue((edge.ToNodeId, edge.RailroadIndex, nextSegmentsUsed));
                }
            }
        }

        return null;
    }

    private bool IsUnfriendlyDestinationForPlayer(Player player, string nodeId)
    {
        return _adjacency.TryGetValue(nodeId, out var edges)
            && edges.Count > 0
            && edges.All(edge => IsUnfriendlyRailroadForPlayer(player, edge.RailroadIndex));
    }

    private bool IsUnfriendlyRailroadForPlayer(Player player, int railroadIndex)
    {
        var railroad = Railroads.FirstOrDefault(candidate => candidate.Index == railroadIndex);
        return railroad?.Owner is not null
            && railroad.Owner != player
            && !player.GrandfatheredRailroadFees.ContainsKey(railroadIndex);
    }

    private RoutePlanningTieBreakAnalysis CalculateTieBreakAnalysis(
        Player player,
        RoutePlanningState startState,
        RoutePlanningState destinationState,
        Dictionary<RoutePlanningState, RoutePlanningPrevious> previous,
        IReadOnlyList<RoutePlanningCityCoverageEntry> cityCoverage,
        Dictionary<int, RoutePlanningNetworkMetrics> networkMetricsByPlayerIndex)
    {
        var ownerCounts = new Dictionary<int, int>();
        var currentState = destinationState;

        while (currentState != startState)
        {
            if (!previous.TryGetValue(currentState, out var previousEntry))
            {
                break;
            }

            if (previousEntry.TotalCostAdded > 0)
            {
                var railroad = Railroads.FirstOrDefault(candidate => candidate.Index == previousEntry.Edge.RailroadIndex);
                if (railroad?.Owner is not null && railroad.Owner != player && !player.GrandfatheredRailroadFees.ContainsKey(railroad.Index))
                {
                    ownerCounts[railroad.Owner.Index] = ownerCounts.GetValueOrDefault(railroad.Owner.Index) + 1;
                }
            }

            currentState = previousEntry.PreviousState;
        }

        if (ownerCounts.Count == 0)
        {
            return RoutePlanningTieBreakAnalysis.Empty;
        }

        var ownerCashValues = ownerCounts.Keys
            .Select(ownerPlayerIndex => Players[ownerPlayerIndex].Cash)
            .OrderBy(value => value)
            .ToArray();
        var ownerAccessibleDestinationPercentages = ownerCounts.Keys
            .Select(ownerPlayerIndex => GetRoutePlanningNetworkMetrics(ownerPlayerIndex, cityCoverage, networkMetricsByPlayerIndex).AccessibleDestinationPercent)
            .OrderBy(value => value)
            .ToArray();
        var ownerMonopolyDestinationPercentages = ownerCounts.Keys
            .Select(ownerPlayerIndex => GetRoutePlanningNetworkMetrics(ownerPlayerIndex, cityCoverage, networkMetricsByPlayerIndex).MonopolyDestinationPercent)
            .OrderBy(value => value)
            .ToArray();

        return new RoutePlanningTieBreakAnalysis(
            ownerCashValues,
            ownerAccessibleDestinationPercentages,
            ownerMonopolyDestinationPercentages,
            ownerCounts.Count,
            ownerCounts.Values.Max());
    }

    private RoutePlanningNetworkMetrics GetRoutePlanningNetworkMetrics(
        int playerIndex,
        IReadOnlyList<RoutePlanningCityCoverageEntry> cityCoverage,
        Dictionary<int, RoutePlanningNetworkMetrics> networkMetricsByPlayerIndex)
    {
        if (networkMetricsByPlayerIndex.TryGetValue(playerIndex, out var existingMetrics))
        {
            return existingMetrics;
        }

        var ownedRailroadIndices = Players[playerIndex].OwnedRailroads.Select(railroad => railroad.Index).ToHashSet();
        var metrics = new RoutePlanningNetworkMetrics(
            AccessibleDestinationPercent: Math.Round(cityCoverage
                .Where(entry => entry.ServingRailroads.Overlaps(ownedRailroadIndices))
                .Sum(entry => entry.GlobalAccessPercentage), 1, MidpointRounding.AwayFromZero),
            MonopolyDestinationPercent: Math.Round(cityCoverage
                .Where(entry => entry.ServingRailroads.Count > 0 && entry.ServingRailroads.All(ownedRailroadIndices.Contains))
                .Sum(entry => entry.GlobalAccessPercentage), 1, MidpointRounding.AwayFromZero));

        networkMetricsByPlayerIndex[playerIndex] = metrics;
        return metrics;
    }

    private List<RoutePlanningCityCoverageEntry> BuildRoutePlanningCityCoverage()
    {
        var regionIndexByCode = MapDefinition.Regions
            .ToDictionary(region => region.Code, region => region.Index, StringComparer.OrdinalIgnoreCase);
        var globalAccessByCity = BuildRoutePlanningGlobalCityAccessPercentages(regionIndexByCode);
        var withinRegionAccessByCity = BuildRoutePlanningWithinRegionCityAccessPercentages(regionIndexByCode);
        var coverage = new List<RoutePlanningCityCoverageEntry>();

        foreach (var city in MapDefinition.Cities)
        {
            if (!city.MapDotIndex.HasValue || !regionIndexByCode.TryGetValue(city.RegionCode, out var regionIndex))
            {
                continue;
            }

            var servingRailroads = MapDefinition.RailroadRouteSegments
                .Where(segment => RoutePlanningSegmentTouchesCity(segment, regionIndex, city.MapDotIndex.Value))
                .Select(segment => segment.RailroadIndex)
                .ToHashSet();
            var cityKey = BuildRoutePlanningCityKey(city);
            coverage.Add(new RoutePlanningCityCoverageEntry(
                city.RegionCode,
                cityKey,
                servingRailroads,
                withinRegionAccessByCity.TryGetValue(cityKey, out var withinRegionPercentage) ? withinRegionPercentage : 0m,
                globalAccessByCity.TryGetValue(cityKey, out var globalAccessPercentage) ? globalAccessPercentage : 0m));
        }

        return coverage;
    }

    private Dictionary<string, decimal> BuildRoutePlanningGlobalCityAccessPercentages(IReadOnlyDictionary<string, int> regionIndexByCode)
    {
        var regionWeights = BuildRoutePlanningRegionProbabilityByCode(regionIndexByCode);
        var withinRegionWeights = BuildRoutePlanningWithinRegionCityAccessPercentages(regionIndexByCode);

        return withinRegionWeights.ToDictionary(
            entry => entry.Key,
            entry =>
            {
                var separatorIndex = entry.Key.IndexOf(':');
                var regionCode = separatorIndex >= 0 ? entry.Key[..separatorIndex] : string.Empty;
                return regionWeights.TryGetValue(regionCode, out var regionWeight)
                    ? Math.Round(regionWeight * entry.Value / 100m, 2, MidpointRounding.AwayFromZero)
                    : 0m;
            },
            StringComparer.OrdinalIgnoreCase);
    }

    private Dictionary<string, decimal> BuildRoutePlanningWithinRegionCityAccessPercentages(IReadOnlyDictionary<string, int> regionIndexByCode)
    {
        return MapDefinition.Cities
            .Where(city => city.MapDotIndex.HasValue && regionIndexByCode.ContainsKey(city.RegionCode))
            .GroupBy(city => city.RegionCode, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group =>
            {
                var weightedCities = group.Where(city => city.Probability.HasValue && city.Probability.Value > 0).ToList();
                if (weightedCities.Count > 0)
                {
                    return group.Select(city => new KeyValuePair<string, decimal>(
                        BuildRoutePlanningCityKey(city),
                        city.Probability.HasValue
                            ? Math.Round((decimal)city.Probability.Value, 2, MidpointRounding.AwayFromZero)
                            : 0m));
                }

                var uniformWeight = group.Any()
                    ? Math.Round(100m / group.Count(), 2, MidpointRounding.AwayFromZero)
                    : 0m;
                return group.Select(city => new KeyValuePair<string, decimal>(BuildRoutePlanningCityKey(city), uniformWeight));
            })
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
    }

    private Dictionary<string, decimal> BuildRoutePlanningRegionProbabilityByCode(IReadOnlyDictionary<string, int> regionIndexByCode)
    {
        var weightedRegions = MapDefinition.Regions.Where(region => region.Probability.HasValue && region.Probability.Value > 0).ToList();
        if (weightedRegions.Count > 0)
        {
            return weightedRegions.ToDictionary(
                region => region.Code,
                region => Math.Round((decimal)region.Probability!.Value, 3, MidpointRounding.AwayFromZero),
                StringComparer.OrdinalIgnoreCase);
        }

        var cityCountByRegion = MapDefinition.Cities
            .Where(city => city.MapDotIndex.HasValue && regionIndexByCode.ContainsKey(city.RegionCode))
            .GroupBy(city => city.RegionCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var totalCityCount = cityCountByRegion.Values.Sum();

        return cityCountByRegion.ToDictionary(
            entry => entry.Key,
            entry => totalCityCount == 0 ? 0m : Math.Round(entry.Value * 100m / totalCityCount, 3, MidpointRounding.AwayFromZero),
            StringComparer.OrdinalIgnoreCase);
    }

    private static bool RoutePlanningSegmentTouchesCity(RailroadRouteSegmentDefinition segment, int regionIndex, int dotIndex)
    {
        return (segment.StartRegionIndex == regionIndex && segment.StartDotIndex == dotIndex)
            || (segment.EndRegionIndex == regionIndex && segment.EndDotIndex == dotIndex);
    }

    private static string BuildRoutePlanningCityKey(CityDefinition city)
    {
        return string.Concat(city.RegionCode, ":", city.Name);
    }

    private static bool IsBetterRouteDestination(RoutePlanningDestinationChoice candidate, RoutePlanningDestinationChoice current)
    {
        if (candidate.CombinedWeightedCost != current.CombinedWeightedCost)
        {
            return candidate.CombinedWeightedCost < current.CombinedWeightedCost;
        }

        if (candidate.CombinedCost != current.CombinedCost)
        {
            return candidate.CombinedCost < current.CombinedCost;
        }

        if (candidate.ExitAnalysis.WorstCaseExitCost != current.ExitAnalysis.WorstCaseExitCost)
        {
            return candidate.ExitAnalysis.WorstCaseExitCost < current.ExitAnalysis.WorstCaseExitCost;
        }

        if (candidate.ExitAnalysis.BonusOutProbability != current.ExitAnalysis.BonusOutProbability)
        {
            return candidate.ExitAnalysis.BonusOutProbability > current.ExitAnalysis.BonusOutProbability;
        }

        var cashComparison = CompareVectors(candidate.TieBreakAnalysis.PaidOwnerCashValues, current.TieBreakAnalysis.PaidOwnerCashValues);
        if (cashComparison != 0)
        {
            return cashComparison < 0;
        }

        var accessibleComparison = CompareVectors(candidate.TieBreakAnalysis.PaidOwnerAccessibleDestinationPercentages, current.TieBreakAnalysis.PaidOwnerAccessibleDestinationPercentages);
        if (accessibleComparison != 0)
        {
            return accessibleComparison < 0;
        }

        var monopolyComparison = CompareVectors(candidate.TieBreakAnalysis.PaidOwnerMonopolyDestinationPercentages, current.TieBreakAnalysis.PaidOwnerMonopolyDestinationPercentages);
        if (monopolyComparison != 0)
        {
            return monopolyComparison < 0;
        }

        if (candidate.TieBreakAnalysis.DistinctPaidOwnerCount != current.TieBreakAnalysis.DistinctPaidOwnerCount)
        {
            return candidate.TieBreakAnalysis.DistinctPaidOwnerCount > current.TieBreakAnalysis.DistinctPaidOwnerCount;
        }

        if (candidate.TieBreakAnalysis.MaxPaymentsToSingleOwner != current.TieBreakAnalysis.MaxPaymentsToSingleOwner)
        {
            return candidate.TieBreakAnalysis.MaxPaymentsToSingleOwner < current.TieBreakAnalysis.MaxPaymentsToSingleOwner;
        }

        return IsBetterRouteCost(candidate.ArrivalCost, current.ArrivalCost);
    }

    private static bool IsBetterRouteCost(RoutePlanningCostKey candidate, RoutePlanningCostKey current)
    {
        if (candidate.WeightedCost != current.WeightedCost)
        {
            return candidate.WeightedCost < current.WeightedCost;
        }

        if (candidate.TotalCost != current.TotalCost)
        {
            return candidate.TotalCost < current.TotalCost;
        }

        if (candidate.TotalTurns != current.TotalTurns)
        {
            return candidate.TotalTurns < current.TotalTurns;
        }

        return candidate.TotalSegments < current.TotalSegments;
    }

    private static string BuildNodeRailroadKey(string nodeId, int railroadIndex)
    {
        return string.Concat(nodeId, "|", railroadIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static int CompareVectors<T>(IReadOnlyList<T> left, IReadOnlyList<T> right) where T : IComparable<T>
    {
        var count = Math.Min(left.Count, right.Count);
        for (var index = 0; index < count; index++)
        {
            var comparison = left[index].CompareTo(right[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
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

internal sealed class FeeBucket(Player? owner, int feeAmount)
{
    public Player? Owner { get; } = owner;
    public List<int> Railroads { get; } = new();
    public int FeeAmount { get; set; } = feeAmount;
}

internal sealed class FeeCalculation(bool usesPublicFee, bool usesPrivateFee, Dictionary<int, FeeBucket> opponentBuckets)
{
    public bool UsesPublicFee { get; } = usesPublicFee;
    public bool UsesPrivateFee { get; } = usesPrivateFee;
    public Dictionary<int, FeeBucket> OpponentBuckets { get; } = opponentBuckets;
    public bool HasCharges => UsesPublicFee || UsesPrivateFee || OpponentBuckets.Count > 0;
}

internal readonly record struct RoutePlanningState(string NodeId, int LastRailroadIndex, int PointsUsedInCurrentTurn);

internal readonly record struct RoutePlanningCostKey(int TotalCost, int WeightedCost, int TotalTurns, int TotalSegments);

internal readonly record struct RoutePlanningDestinationChoice(
    RoutePlanningState DestinationState,
    RoutePlanningCostKey ArrivalCost,
    RoutePlanningExitAnalysis ExitAnalysis,
    RoutePlanningTieBreakAnalysis TieBreakAnalysis)
{
    public double CombinedCost => ArrivalCost.TotalCost + ExitAnalysis.ExpectedExitCost;
    public double CombinedWeightedCost => ArrivalCost.WeightedCost + ExitAnalysis.ExpectedExitCost;
}

internal readonly record struct RoutePlanningPrevious(RoutePlanningState PreviousState, RouteGraphEdge Edge, int TotalCostAdded);

internal readonly record struct RoutePlanningExitAnalysis(
    double ExpectedExitCost,
    int WorstCaseExitCost,
    double BonusOutProbability);

internal readonly record struct RoutePlanningTieBreakAnalysis(
    IReadOnlyList<int> PaidOwnerCashValues,
    IReadOnlyList<decimal> PaidOwnerAccessibleDestinationPercentages,
    IReadOnlyList<decimal> PaidOwnerMonopolyDestinationPercentages,
    int DistinctPaidOwnerCount,
    int MaxPaymentsToSingleOwner)
{
    public static RoutePlanningTieBreakAnalysis Empty { get; } = new([], [], [], 0, 0);
}

internal readonly record struct RoutePlanningNetworkMetrics(decimal AccessibleDestinationPercent, decimal MonopolyDestinationPercent);

internal readonly record struct RoutePlanningCityCoverageEntry(
    string RegionCode,
    string CityKey,
    HashSet<int> ServingRailroads,
    decimal WithinRegionPercentage,
    decimal GlobalAccessPercentage);
