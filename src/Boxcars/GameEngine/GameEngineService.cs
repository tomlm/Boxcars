using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Threading.Channels;
using Azure.Data.Tables;
using Boxcars.Data;
using Boxcars.Engine;
using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Domain;
using Boxcars.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using RailBaronGameEngine = global::Boxcars.Engine.Domain.GameEngine;
using RailBaronGameState = global::Boxcars.Engine.Persistence.GameState;

namespace Boxcars.GameEngine;

public sealed class GameEngineService : BackgroundService, IGameEngine
{
    private readonly Channel<QueuedAction> _actions = Channel.CreateUnbounded<QueuedAction>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    private const string DefaultMapFileName = "U21MAP.RB3";
    private static readonly IReadOnlyList<string> DefaultPlayers = ["Player 1", "Player 2", "Player 3"];

    private readonly IWebHostEnvironment _webHostEnvironment;
    private readonly TableClient _gamesTable;
    private readonly ConcurrentDictionary<string, RailBaronGameEngine> _gameEngines = new(StringComparer.OrdinalIgnoreCase);
    private readonly TaskCompletionSource _mapReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _eventSequence;
    private MapDefinition? _mapDefinition;

    public GameEngineService(IWebHostEnvironment webHostEnvironment, TableServiceClient tableServiceClient)
    {
        _webHostEnvironment = webHostEnvironment;
        _gamesTable = tableServiceClient.GetTableClient(TableNames.GamesTable);
    }

    public event Action<string, RailBaronGameState>? OnStateChanged;

    public async Task<string> CreateGameAsync(CreateGameRequest request, GameCreationOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _mapReady.Task.WaitAsync(cancellationToken);

        var gameId = string.IsNullOrWhiteSpace(options?.PreferredGameId)
            ? Guid.NewGuid().ToString("N")
            : options.PreferredGameId;

        ValidateGameId(gameId);

        if (_mapDefinition is null)
        {
            throw new InvalidOperationException("The game map definition has not been initialized yet.");
        }

        var players = request.Players
            .Select(player => string.IsNullOrWhiteSpace(player.DisplayName) ? player.UserId : player.DisplayName)
            .ToList();

        var createdGameEngine = CreateGameEngine(players);
        if (!_gameEngines.TryAdd(gameId, createdGameEngine))
        {
            throw new InvalidOperationException($"A game with id '{gameId}' already exists.");
        }

        var gameEntity = new GameEntity
        {
            PartitionKey = gameId,
            RowKey = "GAME",
            GameId = gameId,
            CreatorId = request.CreatorUserId,
            MapFileName = request.MapFileName,
            MaxPlayers = request.MaxPlayers,
            CurrentPlayerCount = request.MaxPlayers,
            CreatedAt = DateTimeOffset.UtcNow,
            SettingsJson = JsonSerializer.Serialize(new
            {
                request.MapFileName,
                request.MaxPlayers
            }),
            PlayersJson = GamePlayerSelectionSerialization.Serialize(request.Players)
        };

        await _gamesTable.AddEntityAsync(gameEntity, cancellationToken);

        var snapshot = createdGameEngine.ToSnapshot();
        await PersistEventAsync(gameId, snapshot, "CreateGame", "Game created.", request.CreatorUserId, new
        {
            request.MapFileName,
            request.Players
        }, cancellationToken);

        snapshot = await AdvanceAutomaticTurnFlowAsync(gameId, createdGameEngine, cancellationToken);

        OnStateChanged?.Invoke(gameId, snapshot);
        return gameId;
    }

    public async Task<RailBaronGameState> GetCurrentStateAsync(string gameId, CancellationToken cancellationToken = default)
    {
        ValidateGameId(gameId);
        await _mapReady.Task.WaitAsync(cancellationToken);
        var gameEngine = await GetOrCreateGameEngineAsync(gameId, cancellationToken);
        return await AdvanceAutomaticTurnFlowAsync(gameId, gameEngine, cancellationToken);
    }

    public ValueTask EnqueueActionAsync(string gameId, PlayerAction action, CancellationToken cancellationToken = default)
    {
        ValidateGameId(gameId);
        ArgumentNullException.ThrowIfNull(action);
        return _actions.Writer.WriteAsync(new QueuedAction(gameId, action), cancellationToken);
    }

    public async Task<bool> UndoLastOperationAsync(string gameId, CancellationToken cancellationToken = default)
    {
        ValidateGameId(gameId);
        await _mapReady.Task.WaitAsync(cancellationToken);

        var events = await GetEventsOrderedAsync(gameId, cancellationToken);
        if (events.Count < 2)
        {
            return false;
        }

        var previousEvent = events[^2];
        var restoredSnapshot = GameEventSerialization.DeserializeSnapshot(previousEvent.SerializedGameState);
        var restoredEngine = RestoreGameEngine(restoredSnapshot);

        _gameEngines[gameId] = restoredEngine;

        await PersistEventAsync(
            gameId,
            restoredSnapshot,
            "Undo",
            $"Undo applied. Reverted action '{events[^1].EventKind}'.",
            previousEvent.CreatedBy,
            new { RevertedEvent = events[^1].RowKey },
            cancellationToken);

        OnStateChanged?.Invoke(gameId, restoredSnapshot);
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await LoadMapDefinitionAsync(stoppingToken);
        _mapReady.TrySetResult();

        while (await _actions.Reader.WaitToReadAsync(stoppingToken))
        {
            while (_actions.Reader.TryRead(out var queuedAction))
            {
                try
                {
                    var gameEngine = await GetOrCreateGameEngineAsync(queuedAction.GameId, stoppingToken);
                    var gameEntity = await GetGameEntityAsync(queuedAction.GameId, stoppingToken)
                        ?? throw new KeyNotFoundException($"Game '{queuedAction.GameId}' was not found and is considered deleted.");

                    ProcessTurn(gameEntity, gameEngine, queuedAction.Action);
                    var snapshot = gameEngine.ToSnapshot();

                    await PersistEventAsync(
                        queuedAction.GameId,
                        snapshot,
                        queuedAction.Action.Kind.ToString(),
                        DescribeAction(queuedAction.Action),
                        queuedAction.Action.PlayerId,
                        queuedAction.Action,
                        stoppingToken);

                    snapshot = await AdvanceAutomaticTurnFlowAsync(queuedAction.GameId, gameEngine, stoppingToken);

                    OnStateChanged?.Invoke(queuedAction.GameId, snapshot);
                }
                catch (Exception)
                {
                }
            }
        }
    }

    private async Task LoadMapDefinitionAsync(CancellationToken cancellationToken)
    {
        var mapPath = Path.Combine(_webHostEnvironment.ContentRootPath, DefaultMapFileName);
        if (!File.Exists(mapPath))
        {
            throw new InvalidOperationException($"Map file '{DefaultMapFileName}' was not found in '{_webHostEnvironment.ContentRootPath}'.");
        }

        await using var stream = File.OpenRead(mapPath);
        var loadResult = await MapDefinition.LoadAsync(Path.GetFileName(mapPath), stream, cancellationToken);
        if (!loadResult.Succeeded || loadResult.Definition is null)
        {
            var errors = string.Join("; ", loadResult.Errors);
            throw new InvalidOperationException($"Unable to load map '{DefaultMapFileName}': {errors}");
        }

        _mapDefinition = loadResult.Definition;
    }

    private static void ProcessTurn(GameEntity gameEntity, RailBaronGameEngine gameEngine, PlayerAction action)
    {
        var activePlayer = gameEngine.CurrentTurn.ActivePlayer;
        if (action is not BidAction
            && !string.Equals(activePlayer.Name, action.PlayerId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Action player '{action.PlayerId}' does not match active player '{activePlayer.Name}'.");
        }

        ValidateActionAuthorization(gameEntity, gameEngine, action);

        switch (action)
        {
            case PickDestinationAction:
                gameEngine.DrawDestination();
                break;

            case RollDiceAction rollDiceAction:
                var diceResult = gameEngine.RollDice();
                ValidateDiceRoll(rollDiceAction, diceResult);
                break;

            case ChooseRouteAction chooseRouteAction:
                SavePlayerRoute(gameEngine, chooseRouteAction);
                break;

            case MoveAction moveAction:
                EnsureRoute(gameEngine);
                var steps = ResolveMoveSteps(gameEngine, moveAction);
                gameEngine.MoveAlongRoute(Math.Min(steps, gameEngine.CurrentTurn.MovementRemaining));
                break;

            case PurchaseRailroadAction purchaseRailroadAction:
                var railroadToPurchase = FindRailroad(gameEngine, purchaseRailroadAction.RailroadIndex);
                if (purchaseRailroadAction.AmountPaid > 0
                    && purchaseRailroadAction.AmountPaid != railroadToPurchase.PurchasePrice)
                {
                    throw new InvalidOperationException($"Purchase amount {purchaseRailroadAction.AmountPaid} does not match railroad price {railroadToPurchase.PurchasePrice}.");
                }

                gameEngine.BuyRailroad(railroadToPurchase);
                break;

            case StartAuctionAction startAuctionAction:
                gameEngine.AuctionRailroad(FindRailroad(gameEngine, startAuctionAction.RailroadIndex));
                break;

            case BidAction bidAction:
                var railroadToBid = FindRailroad(gameEngine, bidAction.RailroadIndex);
                var bidder = gameEngine.Players.FirstOrDefault(player => string.Equals(player.Name, bidAction.PlayerId, StringComparison.Ordinal));
                if (bidder is null)
                {
                    throw new InvalidOperationException($"Bidder '{bidAction.PlayerId}' is not in the game.");
                }

                gameEngine.ResolveAuction(railroadToBid, bidder, bidAction.AmountBid);
                break;

            case SellRailroadAction sellRailroadAction:
                if (sellRailroadAction.AmountReceived != 0)
                {
                    throw new InvalidOperationException("Selling to bank currently requires AmountReceived = 0.");
                }

                var railroadToSell = FindRailroad(gameEngine, sellRailroadAction.RailroadIndex);
                gameEngine.AuctionRailroad(railroadToSell);
                gameEngine.ResolveAuction(railroadToSell, winner: null, winningBid: 0);
                break;

            case BuySuperchiefAction buySuperchiefAction:
                if (buySuperchiefAction.AmountPaid != 20000)
                {
                    throw new InvalidOperationException("Buying a Superchief requires AmountPaid = 20000.");
                }

                gameEngine.UpgradeLocomotive(LocomotiveType.Superchief);
                break;

            case DeclinePurchaseAction:
                gameEngine.DeclinePurchase();
                break;

            case EndTurnAction:
                gameEngine.EndTurn();
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(action), action.GetType().Name, "Unsupported player action.");
        }
    }

    private static void ValidateActionAuthorization(GameEntity gameEntity, RailBaronGameEngine gameEngine, PlayerAction action)
    {
        if (action is BidAction)
        {
            return;
        }

        var activePlayerIndex = gameEngine.CurrentTurn.ActivePlayer.Index;
        if (action.PlayerIndex.HasValue && action.PlayerIndex.Value != activePlayerIndex)
        {
            throw new InvalidOperationException($"Action player index '{action.PlayerIndex.Value}' does not match active player index '{activePlayerIndex}'.");
        }

        var selections = GamePlayerSelectionSerialization.Deserialize(gameEntity.PlayersJson);
        if (activePlayerIndex < 0 || activePlayerIndex >= selections.Count)
        {
            throw new InvalidOperationException("Unable to resolve the active player's roster binding.");
        }

        var slotUserId = selections[activePlayerIndex].UserId;
        if (!PlayerControlRules.CanUserControlSlot(slotUserId, action.ActorUserId))
        {
            throw new InvalidOperationException("Only the controlling participant for the active player may perform this action.");
        }
    }

    private async Task<RailBaronGameEngine> GetOrCreateGameEngineAsync(string gameId, CancellationToken cancellationToken)
    {
        if (_mapDefinition is null)
        {
            throw new InvalidOperationException("The game map definition has not been initialized yet.");
        }

        var gameEntity = await GetGameEntityAsync(gameId, cancellationToken);
        if (gameEntity is null)
        {
            _gameEngines.TryRemove(gameId, out _);
            throw new KeyNotFoundException($"Game '{gameId}' was not found and is considered deleted.");
        }

        if (_gameEngines.TryGetValue(gameId, out var inMemoryGameEngine))
        {
            return inMemoryGameEngine;
        }

        var players = GamePlayerSelectionSerialization.Deserialize(gameEntity.PlayersJson)
            .Select(player => string.IsNullOrWhiteSpace(player.DisplayName) ? player.UserId : player.DisplayName)
            .ToList();

        var initializedGameEngine = CreateGameEngine(players);

        var persistedEvent = await GetLatestEventAsync(gameId, cancellationToken);
        if (persistedEvent is not null && !string.IsNullOrWhiteSpace(persistedEvent.SerializedGameState))
        {
            var restoredSnapshot = GameEventSerialization.DeserializeSnapshot(persistedEvent.SerializedGameState);
            initializedGameEngine = RestoreGameEngine(restoredSnapshot);
        }

        return _gameEngines.GetOrAdd(gameId, initializedGameEngine);
    }

    private async Task<GameEntity?> GetGameEntityAsync(string gameId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _gamesTable.GetEntityAsync<GameEntity>(gameId, "GAME", cancellationToken: cancellationToken);
            return response.Value;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private RailBaronGameEngine CreateGameEngine(IReadOnlyList<string> players)
    {
        if (_mapDefinition is null)
        {
            throw new InvalidOperationException("The game map definition has not been initialized yet.");
        }

        var normalizedPlayers = players
            .Where(player => !string.IsNullOrWhiteSpace(player))
            .Select(player => player.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        while (normalizedPlayers.Count < 2)
        {
            normalizedPlayers.Add($"Open Seat {normalizedPlayers.Count + 1}");
        }

        return new RailBaronGameEngine(_mapDefinition, normalizedPlayers, new DefaultRandomProvider());
    }

    private RailBaronGameEngine RestoreGameEngine(RailBaronGameState snapshot)
    {
        if (_mapDefinition is null)
        {
            throw new InvalidOperationException("The game map definition has not been initialized yet.");
        }

        return RailBaronGameEngine.FromSnapshot(snapshot, _mapDefinition, new DefaultRandomProvider());
    }

    private async Task PersistEventAsync(
        string gameId,
        RailBaronGameState snapshot,
        string eventKind,
        string changeSummary,
        string createdBy,
        object eventData,
        CancellationToken cancellationToken)
    {
        var tick = DateTime.UtcNow.Ticks;
        var sequence = Interlocked.Increment(ref _eventSequence);
        var rowKey = $"Event_{tick:D20}_{sequence:D8}";

        var entity = new GameEventEntity
        {
            PartitionKey = gameId,
            RowKey = rowKey,
            GameId = gameId,
            EventKind = eventKind,
            EventData = GameEventSerialization.SerializeEventData(eventData),
            PreviewRouteNodeIdsJson = GameEventSerialization.SerializeEventData(GetActivePlayerSelectedRouteNodeIds(snapshot)),
            PreviewRouteSegmentKeysJson = GameEventSerialization.SerializeEventData(GetActivePlayerSelectedRouteSegmentKeys(snapshot)),
            SerializedGameState = GameEventSerialization.SerializeSnapshot(snapshot),
            ChangeSummary = changeSummary,
            OccurredUtc = DateTimeOffset.UtcNow,
            CreatedBy = createdBy,
            ActingUserId = eventData is PlayerAction playerAction ? playerAction.ActorUserId : string.Empty,
            ActingPlayerIndex = eventData is PlayerAction actingAction ? actingAction.PlayerIndex : null
        };

        await _gamesTable.AddEntityAsync(entity, cancellationToken);
    }

    private async Task<RailBaronGameState> AdvanceAutomaticTurnFlowAsync(
        string gameId,
        RailBaronGameEngine gameEngine,
        CancellationToken cancellationToken)
    {
        var snapshot = gameEngine.ToSnapshot();

        for (var step = 0; step < 8; step++)
        {
            var automaticAction = CreateAutomaticTurnAction(gameEngine);
            if (automaticAction is null)
            {
                return snapshot;
            }

            ProcessAutomaticTurnAction(gameEngine, automaticAction);
            snapshot = gameEngine.ToSnapshot();

            await PersistEventAsync(
                gameId,
                snapshot,
                automaticAction.Kind.ToString(),
                DescribeAction(automaticAction),
                automaticAction.PlayerId,
                automaticAction,
                cancellationToken);
        }

        throw new InvalidOperationException("Automatic turn flow did not stabilize within the expected number of steps.");
    }

    private async Task<GameEventEntity?> GetLatestEventAsync(string gameId, CancellationToken cancellationToken)
    {
        GameEventEntity? latest = null;
        await foreach (var gameEvent in _gamesTable.QueryAsync<GameEventEntity>(
                           entity => entity.PartitionKey == gameId,
                           cancellationToken: cancellationToken))
        {
            if (!gameEvent.RowKey.StartsWith("Event_", StringComparison.Ordinal))
            {
                continue;
            }

            if (latest is null || string.CompareOrdinal(gameEvent.RowKey, latest.RowKey) > 0)
            {
                latest = gameEvent;
            }
        }

        return latest;
    }

    private async Task<List<GameEventEntity>> GetEventsOrderedAsync(string gameId, CancellationToken cancellationToken)
    {
        var events = new List<GameEventEntity>();
        await foreach (var gameEvent in _gamesTable.QueryAsync<GameEventEntity>(
                           entity => entity.PartitionKey == gameId,
                           cancellationToken: cancellationToken))
        {
            if (gameEvent.RowKey.StartsWith("Event_", StringComparison.Ordinal))
            {
                events.Add(gameEvent);
            }
        }

        events.Sort(static (left, right) => string.CompareOrdinal(left.RowKey, right.RowKey));
        return events;
    }

    private static string DescribeAction(PlayerAction action)
    {
        return action switch
        {
            PickDestinationAction => "Active player picked a destination.",
            RollDiceAction => "Active player rolled dice.",
            ChooseRouteAction => "Active player selected a route.",
            MoveAction moveAction => $"Active player moved {Math.Max(0, moveAction.PointsTaken.Count - 1)} steps.",
            PurchaseRailroadAction purchaseAction => $"Active player purchased railroad {purchaseAction.RailroadIndex}.",
            StartAuctionAction auctionAction => $"Active player started an auction for railroad {auctionAction.RailroadIndex}.",
            BidAction bidAction => $"{bidAction.PlayerId} bid {bidAction.AmountBid} on railroad {bidAction.RailroadIndex}.",
            SellRailroadAction sellAction => $"Active player sold railroad {sellAction.RailroadIndex} to the bank.",
            BuySuperchiefAction => "Active player upgraded to a Superchief.",
            DeclinePurchaseAction => "Active player declined purchase.",
            EndTurnAction => "Active player ended their turn.",
            _ => $"Action {action.Kind} applied."
        };
    }

    private static void ValidateGameId(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            throw new ArgumentException("GameId is required.", nameof(gameId));
        }
    }

    private static PlayerAction? CreateAutomaticTurnAction(RailBaronGameEngine gameEngine)
    {
        var activePlayer = gameEngine.CurrentTurn.ActivePlayer;
        var playerIndex = activePlayer.Index;

        return gameEngine.CurrentTurn.Phase switch
        {
            TurnPhase.DrawDestination => new PickDestinationAction
            {
                PlayerId = activePlayer.Name,
                PlayerIndex = playerIndex,
                ActorUserId = string.Empty
            },
            TurnPhase.Roll => new RollDiceAction
            {
                PlayerId = activePlayer.Name,
                PlayerIndex = playerIndex,
                ActorUserId = string.Empty,
                WhiteDieOne = 0,
                WhiteDieTwo = 0
            },
            TurnPhase.EndTurn => new EndTurnAction
            {
                PlayerId = activePlayer.Name,
                PlayerIndex = playerIndex,
                ActorUserId = string.Empty
            },
            _ => null
        };
    }

    private static void ProcessAutomaticTurnAction(RailBaronGameEngine gameEngine, PlayerAction action)
    {
        switch (action)
        {
            case PickDestinationAction:
                gameEngine.DrawDestination();
                break;

            case RollDiceAction rollDiceAction:
                var diceResult = gameEngine.RollDice();
                ValidateDiceRoll(rollDiceAction, diceResult);
                break;

            case EndTurnAction:
                gameEngine.EndTurn();
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(action), action.GetType().Name, "Unsupported automatic turn action.");
        }
    }

    private static void EnsureRoute(RailBaronGameEngine state)
    {
        var activePlayer = state.CurrentTurn.ActivePlayer;
        if (activePlayer.ActiveRoute is not null)
        {
            return;
        }

        var suggestedRoute = state.SuggestRoute();
        state.SaveRoute(suggestedRoute);
    }

    private static Railroad FindRailroad(RailBaronGameEngine state, int railroadIndex)
    {
        var railroad = state.Railroads.FirstOrDefault(item => item.Index == railroadIndex);
        return railroad ?? throw new InvalidOperationException($"Railroad index '{railroadIndex}' was not found.");
    }

    private static int ResolveMoveSteps(RailBaronGameEngine state, MoveAction action)
    {
        if (action.PointsTaken.Count > 1)
        {
            return action.PointsTaken.Count - 1;
        }

        return state.CurrentTurn.MovementRemaining;
    }

    private static void ValidateDiceRoll(RollDiceAction action, DiceResult result)
    {
        if (action.WhiteDieOne <= 0 && action.WhiteDieTwo <= 0 && action.RedDie is null)
        {
            return;
        }

        if (result.WhiteDice.Length < 2
            || action.WhiteDieOne != result.WhiteDice[0]
            || action.WhiteDieTwo != result.WhiteDice[1]
            || action.RedDie != result.RedDie)
        {
            throw new InvalidOperationException("Submitted dice values do not match rolled dice result.");
        }
    }

    private static void SavePlayerRoute(RailBaronGameEngine state, ChooseRouteAction action)
    {
        if (action.RouteNodeIds.Count > 1)
        {
            if (action.RouteNodeIds.Count - 1 > state.CurrentTurn.MovementRemaining)
            {
                throw new InvalidOperationException("Selected route exceeds movement remaining.");
            }

            var selectedRoute = BuildSelectedRoute(state, action);
            state.SaveRoute(selectedRoute);
            return;
        }

        var suggestedRoute = state.SuggestRoute();
        if (action.RouteNodeIds.Count > 0
            && !action.RouteNodeIds.SequenceEqual(suggestedRoute.NodeIds, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only the suggested route can be saved in this sample implementation.");
        }

        state.SaveRoute(suggestedRoute);
    }

    private static Boxcars.Engine.Domain.Route BuildSelectedRoute(RailBaronGameEngine state, ChooseRouteAction action)
    {
        var player = state.CurrentTurn.ActivePlayer;
        var nodeIds = action.RouteNodeIds.ToList();

        var segments = new List<RouteSegment>(Math.Max(0, nodeIds.Count - 1));
        for (var index = 0; index < nodeIds.Count - 1; index++)
        {
            var fromNodeId = nodeIds[index];
            var toNodeId = nodeIds[index + 1];
            var railroadIndex = TryParseSelectedSegmentKey(action.RouteSegmentKeys, index, fromNodeId, toNodeId);

            var matchingDefinition = state.MapDefinition.RailroadRouteSegments.FirstOrDefault(segment =>
                segment.RailroadIndex == railroadIndex
                && IsSameEdge(segment, fromNodeId, toNodeId));

            if (matchingDefinition is null)
            {
                throw new InvalidOperationException("Selected route contains an invalid railroad segment.");
            }

            segments.Add(new RouteSegment
            {
                FromNodeId = fromNodeId,
                ToNodeId = toNodeId,
                RailroadIndex = railroadIndex
            });
        }

        return new Boxcars.Engine.Domain.Route(nodeIds, segments, CalculateRouteCost(state, player, segments));
    }

    private static int TryParseSelectedSegmentKey(
        IReadOnlyList<string> selectedSegmentKeys,
        int segmentIndex,
        string fromNodeId,
        string toNodeId)
    {
        if (segmentIndex < selectedSegmentKeys.Count)
        {
            var parts = selectedSegmentKeys[segmentIndex].Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length == 3
                && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedRailroadIndex)
                && string.Equals(parts[0], fromNodeId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(parts[1], toNodeId, StringComparison.OrdinalIgnoreCase))
            {
                return parsedRailroadIndex;
            }
        }

        throw new InvalidOperationException("Selected route is missing railroad metadata for one or more segments.");
    }

    private static bool IsSameEdge(RailroadRouteSegmentDefinition segment, string fromNodeId, string toNodeId)
    {
        var segmentFromNodeId = BuildNodeId(segment.StartRegionIndex, segment.StartDotIndex);
        var segmentToNodeId = BuildNodeId(segment.EndRegionIndex, segment.EndDotIndex);

        return string.Equals(segmentFromNodeId, fromNodeId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(segmentToNodeId, toNodeId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(segmentFromNodeId, toNodeId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(segmentToNodeId, fromNodeId, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildNodeId(int regionIndex, int dotIndex)
    {
        return string.Concat(regionIndex.ToString(CultureInfo.InvariantCulture), ":", dotIndex.ToString(CultureInfo.InvariantCulture));
    }

    private static int CalculateRouteCost(RailBaronGameEngine state, Player player, IReadOnlyList<RouteSegment> segments)
    {
        var usesBankRailroad = false;
        var opposingOwnerIndices = new HashSet<int>();

        foreach (var segment in segments)
        {
            var railroad = state.Railroads.FirstOrDefault(candidate => candidate.Index == segment.RailroadIndex);
            if (railroad is null || railroad.Owner is null)
            {
                usesBankRailroad = true;
                continue;
            }

            if (railroad.Owner != player)
            {
                opposingOwnerIndices.Add(railroad.Owner.Index);
            }
        }

        var bankFee = usesBankRailroad ? 1000 : 0;
        var opponentRate = state.AllRailroadsSold ? 10000 : 5000;
        return bankFee + (opposingOwnerIndices.Count * opponentRate);
    }

    private readonly record struct QueuedAction(string GameId, PlayerAction Action);

    private static List<string> GetActivePlayerSelectedRouteNodeIds(RailBaronGameState snapshot)
    {
        if (snapshot.ActivePlayerIndex < 0 || snapshot.ActivePlayerIndex >= snapshot.Players.Count)
        {
            return [];
        }

        return snapshot.Players[snapshot.ActivePlayerIndex].SelectedRouteNodeIds.ToList();
    }

    private static List<string> GetActivePlayerSelectedRouteSegmentKeys(RailBaronGameState snapshot)
    {
        if (snapshot.ActivePlayerIndex < 0 || snapshot.ActivePlayerIndex >= snapshot.Players.Count)
        {
            return [];
        }

        return snapshot.Players[snapshot.ActivePlayerIndex].SelectedRouteSegmentKeys.ToList();
    }
}
