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
using Boxcars.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
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
    private readonly PurchaseRulesOptions _purchaseRulesOptions;
    private long _eventSequence;
    private MapDefinition? _mapDefinition;

    public GameEngineService(IWebHostEnvironment webHostEnvironment, TableServiceClient tableServiceClient, IOptions<PurchaseRulesOptions> purchaseRulesOptions)
    {
        _webHostEnvironment = webHostEnvironment;
        _gamesTable = tableServiceClient.GetTableClient(TableNames.GamesTable);
        _purchaseRulesOptions = purchaseRulesOptions.Value;
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
                        DescribeAction(queuedAction.Action, snapshot, gameEngine),
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

    private void ProcessTurn(GameEntity gameEntity, RailBaronGameEngine gameEngine, PlayerAction action)
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
                SavePlayerRoute(gameEngine, moveAction);
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

            case BuyEngineAction buyEngineAction:
                var expectedEnginePrice = RailBaronGameEngine.GetUpgradeCost(
                    gameEngine.CurrentTurn.ActivePlayer.LocomotiveType,
                    buyEngineAction.EngineType,
                    _purchaseRulesOptions.SuperchiefPrice);
                if (expectedEnginePrice < 0)
                {
                    throw new InvalidOperationException($"Cannot upgrade from {gameEngine.CurrentTurn.ActivePlayer.LocomotiveType} to {buyEngineAction.EngineType}.");
                }

                if (buyEngineAction.AmountPaid != expectedEnginePrice)
                {
                    throw new InvalidOperationException($"Buying a {buyEngineAction.EngineType} requires AmountPaid = {expectedEnginePrice}.");
                }

                gameEngine.UpgradeLocomotive(buyEngineAction.EngineType);
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

        return new RailBaronGameEngine(_mapDefinition, normalizedPlayers, new DefaultRandomProvider(), _purchaseRulesOptions.SuperchiefPrice);
    }

    private RailBaronGameEngine RestoreGameEngine(RailBaronGameState snapshot)
    {
        if (_mapDefinition is null)
        {
            throw new InvalidOperationException("The game map definition has not been initialized yet.");
        }

        return RailBaronGameEngine.FromSnapshot(snapshot, _mapDefinition, new DefaultRandomProvider(), _purchaseRulesOptions.SuperchiefPrice);
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
                DescribeAction(automaticAction, snapshot, gameEngine),
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

    private static string DescribeAction(PlayerAction action, RailBaronGameState snapshot, RailBaronGameEngine gameEngine)
    {
        var actorName = ResolveActorName(action, snapshot);

        return action switch
        {
            PickDestinationAction => DescribeDestinationPick(actorName, action, snapshot),
            RollDiceAction => $"{actorName} rolled {FormatDiceRoll(snapshot.Turn.DiceResult, snapshot.Turn.BonusRollAvailable, action as RollDiceAction)}",
            ChooseRouteAction => string.Empty,
            MoveAction moveAction => DescribeMove(actorName, moveAction, snapshot),
            PurchaseRailroadAction purchaseAction => $"{actorName} bought the {GetRailroadDisplayName(FindRailroad(gameEngine, purchaseAction.RailroadIndex))} railroad for {FormatCurrency(ResolveAmountPaid(purchaseAction.AmountPaid, FindRailroad(gameEngine, purchaseAction.RailroadIndex).PurchasePrice))}",
            StartAuctionAction auctionAction => $"{actorName} started an auction for the {GetRailroadDisplayName(FindRailroad(gameEngine, auctionAction.RailroadIndex))} railroad",
            BidAction bidAction => $"{actorName} bid {FormatCurrency(bidAction.AmountBid)} for the {GetRailroadDisplayName(FindRailroad(gameEngine, bidAction.RailroadIndex))} railroad",
            SellRailroadAction sellAction => DescribeRailroadSale(actorName, sellAction, gameEngine),
            BuyEngineAction buyEngineAction => $"{actorName} bought a {buyEngineAction.EngineType} for {FormatCurrency(buyEngineAction.AmountPaid)}",
            DeclinePurchaseAction => $"{actorName} declined the purchase opportunity",
            EndTurnAction => $"{actorName} ended their turn",
            _ => $"{actorName} performed {action.Kind}"
        };
    }

    private static string DescribeDestinationPick(string actorName, PlayerAction action, RailBaronGameState snapshot)
    {
        var destinationName = TryGetPlayerState(action, snapshot)?.DestinationCityName;
        return string.IsNullOrWhiteSpace(destinationName)
            ? $"{actorName} drew a new destination"
            : $"{actorName} has a new destination: {destinationName}";
    }

    private static string DescribeMove(string actorName, MoveAction action, RailBaronGameState snapshot)
    {
        var arrival = snapshot.Turn.ArrivalResolution;
        if (arrival is not null)
        {
            return arrival.Message;
        }

        var steps = Math.Max(0, action.PointsTaken.Count - 1);
        var moveSummary = steps == 1
            ? $"{actorName} moved 1 space"
            : $"{actorName} moved {steps} spaces";

        var feeSummary = DescribeMoveFeeSummary(action, snapshot);
        return string.IsNullOrWhiteSpace(feeSummary)
            ? moveSummary
            : $"{moveSummary} and paid {feeSummary}";
    }

    private static string DescribeMoveFeeSummary(MoveAction action, RailBaronGameState snapshot)
    {
        if (action.PointsTaken.Count < 2 || action.SelectedSegmentKeys.Count == 0)
        {
            return string.Empty;
        }

        var activePlayerIndex = ResolvePlayerIndex(action, snapshot);
        var movedSegmentCount = Math.Min(action.PointsTaken.Count - 1, action.SelectedSegmentKeys.Count);
        var usedPublicRailroad = false;
        var opposingOwnerIndices = new HashSet<int>();

        for (var index = 0; index < movedSegmentCount; index++)
        {
            var fromNodeId = action.PointsTaken[index];
            var toNodeId = action.PointsTaken[index + 1];

            int railroadIndex;
            try
            {
                railroadIndex = TryParseSelectedSegmentKey(action.SelectedSegmentKeys, index, fromNodeId, toNodeId);
            }
            catch (InvalidOperationException)
            {
                return string.Empty;
            }

            if (!snapshot.RailroadOwnership.TryGetValue(railroadIndex, out var ownerIndex) || ownerIndex is null)
            {
                usedPublicRailroad = true;
                continue;
            }

            if (activePlayerIndex.HasValue && ownerIndex.Value == activePlayerIndex.Value)
            {
                continue;
            }

            opposingOwnerIndices.Add(ownerIndex.Value);
        }

        var feeParts = new List<string>();
        if (usedPublicRailroad)
        {
            feeParts.Add($"{FormatCurrency(1000)} public fees");
        }

        var opponentRate = snapshot.AllRailroadsSold ? 10000 : 5000;
        foreach (var ownerIndex in opposingOwnerIndices.OrderBy(index => index))
        {
            feeParts.Add($"{FormatCurrency(opponentRate)} to {ResolvePlayerName(snapshot, ownerIndex)}");
        }

        return FormatReadableList(feeParts);
    }

    private static int? ResolvePlayerIndex(PlayerAction action, RailBaronGameState snapshot)
    {
        if (action.PlayerIndex.HasValue
            && action.PlayerIndex.Value >= 0
            && action.PlayerIndex.Value < snapshot.Players.Count)
        {
            return action.PlayerIndex.Value;
        }

        var playerIndex = snapshot.Players.FindIndex(player => string.Equals(player.Name, action.PlayerId, StringComparison.Ordinal));
        return playerIndex >= 0 ? playerIndex : null;
    }

    private static string ResolvePlayerName(RailBaronGameState snapshot, int playerIndex)
    {
        return playerIndex >= 0 && playerIndex < snapshot.Players.Count && !string.IsNullOrWhiteSpace(snapshot.Players[playerIndex].Name)
            ? snapshot.Players[playerIndex].Name
            : $"player {playerIndex + 1}";
    }

    private static string FormatReadableList(List<string> items)
    {
        return items.Count switch
        {
            0 => string.Empty,
            1 => items[0],
            2 => string.Concat(items[0], " and ", items[1]),
            _ => string.Concat(string.Join(", ", items.Take(items.Count - 1)), ", and ", items[^1])
        };
    }

    private static string DescribeRailroadSale(string actorName, SellRailroadAction action, RailBaronGameEngine gameEngine)
    {
        var railroad = FindRailroad(gameEngine, action.RailroadIndex);
        if (action.AmountReceived > 0)
        {
            return $"{actorName} sold the {GetRailroadDisplayName(railroad)} railroad for {FormatCurrency(action.AmountReceived)}";
        }

        return $"{actorName} sold the {GetRailroadDisplayName(railroad)} railroad to the bank";
    }

    private static string ResolveActorName(PlayerAction action, RailBaronGameState snapshot)
    {
        var playerState = TryGetPlayerState(action, snapshot);
        if (!string.IsNullOrWhiteSpace(playerState?.Name))
        {
            return playerState.Name;
        }

        return string.IsNullOrWhiteSpace(action.PlayerId)
            ? "Unknown player"
            : action.PlayerId;
    }

    private static Boxcars.Engine.Persistence.PlayerState? TryGetPlayerState(PlayerAction action, RailBaronGameState snapshot)
    {
        if (action.PlayerIndex.HasValue
            && action.PlayerIndex.Value >= 0
            && action.PlayerIndex.Value < snapshot.Players.Count)
        {
            return snapshot.Players[action.PlayerIndex.Value];
        }

        return snapshot.Players.FirstOrDefault(player => string.Equals(player.Name, action.PlayerId, StringComparison.Ordinal));
    }

    private static int ResolveAmountPaid(int amountPaid, int fallbackAmount)
    {
        return amountPaid > 0 ? amountPaid : fallbackAmount;
    }

    private static string GetRailroadDisplayName(Railroad railroad)
    {
        return string.IsNullOrWhiteSpace(railroad.ShortName)
            ? railroad.Name
            : railroad.ShortName;
    }

    private static string FormatCurrency(int amount)
    {
        return amount.ToString("$#,0", CultureInfo.InvariantCulture);
    }

    private static string FormatDiceRoll(Boxcars.Engine.Persistence.DiceResultState? diceResult, bool bonusRollAvailable, RollDiceAction? fallbackAction = null)
    {
        if (diceResult is { RedDie: not null, WhiteDice.Length: >= 2 }
            && diceResult.WhiteDice.All(value => value == 0))
        {
            return string.Concat("Bonus (", diceResult.RedDie.Value.ToString(CultureInfo.InvariantCulture), ")");
        }

        if (diceResult?.WhiteDice is { Length: >= 2 })
        {
            var whiteDiceText = string.Join("+", diceResult.WhiteDice.Select(value => value.ToString(CultureInfo.InvariantCulture)));
            if (bonusRollAvailable && !diceResult.RedDie.HasValue)
            {
                return string.Concat(whiteDiceText, "+(Bonus)");
            }

            return diceResult.RedDie.HasValue
                ? string.Concat(whiteDiceText, "+(", diceResult.RedDie.Value.ToString(CultureInfo.InvariantCulture), ")")
                : whiteDiceText;
        }

        if (fallbackAction is not null && fallbackAction.WhiteDieOne > 0 && fallbackAction.WhiteDieTwo > 0)
        {
            var whiteDiceText = string.Concat(
                fallbackAction.WhiteDieOne.ToString(CultureInfo.InvariantCulture),
                "+",
                fallbackAction.WhiteDieTwo.ToString(CultureInfo.InvariantCulture));

            return fallbackAction.RedDie.HasValue
                ? string.Concat(whiteDiceText, "+(", fallbackAction.RedDie.Value.ToString(CultureInfo.InvariantCulture), ")")
                : whiteDiceText;
        }

        return "0";
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
        SavePlayerRoute(state, action.RouteNodeIds, action.RouteSegmentKeys);
    }

    private static void SavePlayerRoute(RailBaronGameEngine state, MoveAction action)
    {
        SavePlayerRoute(state, action.PointsTaken, action.SelectedSegmentKeys);
    }

    private static void SavePlayerRoute(
        RailBaronGameEngine state,
        IReadOnlyList<string> routeNodeIds,
        IReadOnlyList<string> routeSegmentKeys)
    {
        if (routeNodeIds.Count > 1)
        {
            if (routeNodeIds.Count - 1 > state.CurrentTurn.MovementRemaining)
            {
                throw new InvalidOperationException("Selected route exceeds movement remaining.");
            }

            var selectedRoute = BuildSelectedRoute(state, routeNodeIds, routeSegmentKeys);
            state.SaveRoute(selectedRoute);
            return;
        }

        var suggestedRoute = state.SuggestRoute();
        if (routeNodeIds.Count > 0
            && !routeNodeIds.SequenceEqual(suggestedRoute.NodeIds, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only the suggested route can be saved in this sample implementation.");
        }

        state.SaveRoute(suggestedRoute);
    }

    private static Boxcars.Engine.Domain.Route BuildSelectedRoute(
        RailBaronGameEngine state,
        IReadOnlyList<string> routeNodeIds,
        IReadOnlyList<string> routeSegmentKeys)
    {
        var player = state.CurrentTurn.ActivePlayer;
        var nodeIds = routeNodeIds.ToList();

        var segments = new List<RouteSegment>(Math.Max(0, nodeIds.Count - 1));
        for (var index = 0; index < nodeIds.Count - 1; index++)
        {
            var fromNodeId = nodeIds[index];
            var toNodeId = nodeIds[index + 1];
            var railroadIndex = TryParseSelectedSegmentKey(routeSegmentKeys, index, fromNodeId, toNodeId);

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
