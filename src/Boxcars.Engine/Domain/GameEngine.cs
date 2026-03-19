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
    private readonly int _superchiefPrice;

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
    public GameEngine(MapDefinition mapDefinition, IReadOnlyList<string> playerNames, IRandomProvider randomProvider, int superchiefPrice = 40_000)
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
        _superchiefPrice = superchiefPrice > 0
            ? superchiefPrice
            : throw new ArgumentOutOfRangeException(nameof(superchiefPrice), "Superchief price must be greater than zero.");

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
                    player.CurrentNodeId = NodeKey(region.Index, homeCity.MapDotIndex.Value);
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
    private GameEngine(MapDefinition mapDefinition, IRandomProvider randomProvider, int superchiefPrice)
    {
        MapDefinition = mapDefinition;
        _randomProvider = randomProvider;
        _superchiefPrice = superchiefPrice > 0
            ? superchiefPrice
            : throw new ArgumentOutOfRangeException(nameof(superchiefPrice), "Superchief price must be greater than zero.");

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
            var pendingRegionChoice = BuildPendingRegionChoice(player, city.RegionCode);
            if (pendingRegionChoice is not null)
            {
                CurrentTurn.PendingRegionChoice = pendingRegionChoice;
                CurrentTurn.Phase = TurnPhase.RegionChoice;
                return city;
            }
        }

        FinalizeDestinationAssignment(player, city);
        return city;
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
        CurrentTurn.PendingRegionChoice = null;
        FinalizeDestinationAssignment(player, city);
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
                throw new InvalidOperationException("Segment reuse violation.");

            player.UsedSegments.Add(segKey);
            CurrentTurn.RailroadsRiddenThisTurn.Add(segment.RailroadIndex);
            TrackRailroadFeeRate(player, segment.RailroadIndex);
            UpdateGrandfatheredRailroadAccess(player, segment.RailroadIndex);
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
        UpdateGrandfatheringForOwnershipChange(railroad.Index, player);
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

        int cost = GetUpgradeCost(player.LocomotiveType, target, _superchiefPrice);
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

        UpdateGrandfatheringForOwnershipChange(railroad.Index, railroad.Owner);

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
        railroad.Owner = null;

        UpdateGrandfatheringForOwnershipChange(railroad.Index, newOwner: null);
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
                        EligibleRegionCodes = CurrentTurn.PendingRegionChoice.EligibleRegionCodes.ToList(),
                        EligibleCityCountsByRegion = CurrentTurn.PendingRegionChoice.EligibleCityCountsByRegion.ToDictionary(
                            entry => entry.Key,
                            entry => entry.Value,
                            StringComparer.OrdinalIgnoreCase)
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
                HomeCityName = player.HomeCity.Name,
                CurrentCityName = player.CurrentCity.Name,
                TripStartCityName = player.TripOriginCity?.Name,
                DestinationCityName = player.Destination?.Name,
                LocomotiveType = player.LocomotiveType.ToString(),
                IsActive = player.IsActive,
                IsBankrupt = player.IsBankrupt,
                HasDeclared = player.HasDeclared,
                OwnedRailroadIndices = player.OwnedRailroads.Select(r => r.Index).ToList(),
                GrandfatheredRailroadIndices = player.GrandfatheredRailroadIndices.OrderBy(index => index).ToList(),
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
    public static GameEngine FromSnapshot(GameState snapshot, MapDefinition mapDefinition, IRandomProvider randomProvider, int superchiefPrice = 40_000)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(mapDefinition);
        ArgumentNullException.ThrowIfNull(randomProvider);

        var engine = new GameEngine(mapDefinition, randomProvider, superchiefPrice);

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
            if (ps.TripStartCityName != null && engine._cityByName.TryGetValue(ps.TripStartCityName, out var tripStartCity))
                player.TripOriginCity = tripStartCity;
            if (ps.DestinationCityName != null && engine._cityByName.TryGetValue(ps.DestinationCityName, out var destCity))
                player.Destination = destCity;

            // Restore used segments
            foreach (var segStr in ps.UsedSegments)
            {
                // New format: "NodeA-NodeB:RR" — old format: "NodeA-NodeB"
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
                    // Legacy format without railroad index — treat as railroad -1
                    var parts = segStr.Split('-');
                    if (parts.Length == 2)
                        player.UsedSegments.Add(new SegmentKey(parts[0], parts[1], -1));
                }
            }

            foreach (var railroadIndex in ps.GrandfatheredRailroadIndices)
            {
                player.GrandfatheredRailroadIndices.Add(railroadIndex);
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
                    EligibleRegionCodes = snapshot.Turn.PendingRegionChoice.EligibleRegionCodes,
                    EligibleCityCountsByRegion = snapshot.Turn.PendingRegionChoice.EligibleCityCountsByRegion
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

    public static int GetUpgradeCost(LocomotiveType currentEngineType, LocomotiveType targetEngineType, int superchiefPrice)
    {
        return (currentEngineType, targetEngineType) switch
        {
            (LocomotiveType.Freight, LocomotiveType.Express) => 4_000,
            (LocomotiveType.Freight, LocomotiveType.Superchief) => superchiefPrice,
            (LocomotiveType.Express, LocomotiveType.Superchief) => superchiefPrice,
            _ => -1
        };
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

    private PendingRegionChoice? BuildPendingRegionChoice(Player player, string triggeredRegionCode)
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
            EligibleRegionCodes = eligibleRegions.Select(region => region.Code).ToList(),
            EligibleCityCountsByRegion = eligibleRegions.ToDictionary(
                region => region.Code,
                region => region.EligibleCityCount,
                StringComparer.OrdinalIgnoreCase)
        };
    }

    private void FinalizeDestinationAssignment(Player player, CityDefinition city)
    {
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
        if (player.Destination != null && tripOriginCity.PayoutIndex.HasValue && player.Destination.PayoutIndex.HasValue)
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
            PurchaseOpportunityAvailable = true,
            Message = $"{player.Name} reached {destination.Name}, collected ${payout:N0}, and has ${pendingFees:N0} in fees due."
        };
        player.Destination = null;
        player.TripOriginCity = null;
        player.ActiveRoute = null;
        player.UsedSegments.Clear();

        DestinationReached?.Invoke(this, new DestinationReachedEventArgs(player, destination, payout, player.Cash, purchaseOpportunityAvailable: true));

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
        var feeBuckets = BuildFeeBuckets(player, CurrentTurn.RailroadsRiddenThisTurn);
        var pendingFeeAmount = CalculatePendingFeeAmount(player);
        CurrentTurn.PendingFeeAmount = pendingFeeAmount;

        if (feeBuckets.Count == 0 || pendingFeeAmount <= 0)
        {
            ClearForcedSaleContext();
            CurrentTurn.Phase = TurnPhase.EndTurn;
            return;
        }

        if (player.Cash < pendingFeeAmount)
        {
            BeginForcedSale(player, pendingFeeAmount);
            return;
        }

        int opponentRate = AllRailroadsSold ? 10000 : 5000;
        foreach (var feeBucket in feeBuckets.Values.OrderBy(bucket => bucket.Owner?.Index ?? -1))
        {
            int fee = feeBucket.Owner is null
                ? 1000
                : feeBucket.RequiresFullOwnerRate ? opponentRate : 1000;

            player.Cash -= fee;
            if (feeBucket.Owner is not null)
            {
                feeBucket.Owner.Cash += fee;
            }

            UsageFeeCharged?.Invoke(this, new UsageFeeChargedEventArgs(player, feeBucket.Owner, fee, feeBucket.Railroads));
        }

        ClearForcedSaleContext();
        CurrentTurn.Phase = TurnPhase.EndTurn;
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
        AllRailroadsSold = Railroads.Where(r => !r.IsPublic).All(r => r.Owner != null);
    }

    private int CalculatePendingFeeAmount(Player player)
    {
        var feeBuckets = BuildFeeBuckets(player, CurrentTurn.RailroadsRiddenThisTurn);
        if (feeBuckets.Count == 0)
        {
            return 0;
        }

        var opponentRate = AllRailroadsSold ? 10000 : 5000;
        return feeBuckets.Values.Sum(feeBucket => feeBucket.Owner is null
            ? 1000
            : feeBucket.RequiresFullOwnerRate ? opponentRate : 1000);
    }

    private Dictionary<int, FeeBucket> BuildFeeBuckets(Player rider, IEnumerable<int> railroadIndices)
    {
        var feeBuckets = new Dictionary<int, FeeBucket>();
        foreach (var rrIdx in railroadIndices)
        {
            var railroad = Railroads.FirstOrDefault(r => r.Index == rrIdx);
            if (railroad == null)
            {
                continue;
            }

            AddRailroadToFeeBucket(feeBuckets, rider, railroad, CurrentTurn.RailroadsRequiringFullOwnerRateThisTurn);
        }

        return feeBuckets;
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
            return;
        }

        player.GrandfatheredRailroadIndices.Clear();
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

    private void UpdateGrandfatheringForOwnershipChange(int railroadIndex, Player? newOwner)
    {
        foreach (var rider in Players)
        {
            rider.GrandfatheredRailroadIndices.Remove(railroadIndex);
        }

        if (newOwner is null)
        {
            return;
        }

        foreach (var rider in Players.Where(player => player != newOwner && IsPlayerOnRailroadNode(player, railroadIndex)))
        {
            rider.GrandfatheredRailroadIndices.Add(railroadIndex);
        }
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
        Player rider,
        Railroad railroad,
        HashSet<int> railroadsRequiringFullOwnerRate)
    {
        var usesBaseRate = railroad.Owner is null || railroad.Owner == rider;
        int ownerKey = usesBaseRate ? -1 : railroad.Owner!.Index;
        if (!feeBuckets.TryGetValue(ownerKey, out var bucket))
        {
            bucket = new FeeBucket(usesBaseRate ? null : railroad.Owner);
            feeBuckets[ownerKey] = bucket;
        }

        bucket.Railroads.Add(railroad.Index);
        if (!usesBaseRate && railroadsRequiringFullOwnerRate.Contains(railroad.Index))
        {
            bucket.RequiresFullOwnerRate = true;
        }
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
                var segKey = new SegmentKey(edge.FromNodeId, edge.ToNodeId, edge.RailroadIndex);
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

internal sealed class FeeBucket(Player? owner)
{
    public Player? Owner { get; } = owner;
    public List<int> Railroads { get; } = new();
    public bool RequiresFullOwnerRate { get; set; }
}
