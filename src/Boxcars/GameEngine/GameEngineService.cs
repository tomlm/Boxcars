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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RailBaronGameEngine = global::Boxcars.Engine.Domain.GameEngine;
using RailBaronGameState = global::Boxcars.Engine.Persistence.GameState;

namespace Boxcars.GameEngine;

public sealed class GameEngineService : BackgroundService, IGameEngine
{
    private const int AutomaticTurnFlowStepLimit = 32;

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
    private readonly ConcurrentDictionary<string, byte> _busyGames = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _pendingAutomaticFlowResumes = new(StringComparer.OrdinalIgnoreCase);
    private readonly TaskCompletionSource _mapReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly PurchaseRulesOptions _purchaseRulesOptions;
    private readonly BotOptions _botOptions;
    private readonly GamePresenceService _gamePresenceService;
    private readonly BotTurnService _botTurnService;
    private readonly ILogger<GameEngineService> _logger;
    private long _eventSequence;
    private MapDefinition? _mapDefinition;

    public GameEngineService(
        IWebHostEnvironment webHostEnvironment,
        TableServiceClient tableServiceClient,
        GamePresenceService gamePresenceService,
        BotTurnService botTurnService,
        IOptions<BotOptions> botOptions,
        IOptions<PurchaseRulesOptions> purchaseRulesOptions,
        ILogger<GameEngineService> logger)
    {
        _webHostEnvironment = webHostEnvironment;
        _gamesTable = tableServiceClient.GetTableClient(TableNames.GamesTable);
        _gamePresenceService = gamePresenceService;
        _botTurnService = botTurnService;
        _botOptions = botOptions.Value;
        _purchaseRulesOptions = purchaseRulesOptions.Value;
        _logger = logger;
        _gamePresenceService.PresenceChanged += OnPresenceChanged;
    }

    public GameEngineService(
        IWebHostEnvironment webHostEnvironment,
        TableServiceClient tableServiceClient,
        GamePresenceService gamePresenceService,
        IOptions<BotOptions> botOptions,
        IOptions<PurchaseRulesOptions> purchaseRulesOptions,
        ILogger<GameEngineService> logger)
        : this(
            webHostEnvironment,
            tableServiceClient,
            gamePresenceService,
            CreateFallbackBotTurnService(tableServiceClient, gamePresenceService, purchaseRulesOptions),
            botOptions,
            purchaseRulesOptions,
            logger)
    {
    }

    public event Action<string, RailBaronGameState>? OnStateChanged;
    public event Action<string, GameActionFailure>? OnActionFailed;

    public override void Dispose()
    {
        _gamePresenceService.PresenceChanged -= OnPresenceChanged;
        base.Dispose();
    }

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

        var originalBotAssignmentsJson = gameEntity.BotAssignmentsJson;
        await _botTurnService.EnsureBotSeatAssignmentsAsync(gameEntity, request.Players, request.CreatorUserId, cancellationToken);
        await PersistBotAssignmentsIfChangedAsync(gameEntity, originalBotAssignmentsJson, cancellationToken);

        var snapshot = createdGameEngine.ToSnapshot();
        await PersistEventAsync(gameId, snapshot, "CreateGame", "Game created.", request.CreatorUserId, new
        {
            request.MapFileName,
            request.Players
        }, cancellationToken);

        snapshot = await AdvanceAutomaticTurnFlowAsync(gameEntity, gameId, createdGameEngine, cancellationToken);

        OnStateChanged?.Invoke(gameId, snapshot);
        return gameId;
    }

    public async Task<RailBaronGameState> GetCurrentStateAsync(string gameId, CancellationToken cancellationToken = default)
    {
        ValidateGameId(gameId);
        await _mapReady.Task.WaitAsync(cancellationToken);
        var gameEngine = await GetOrCreateGameEngineAsync(gameId, cancellationToken);
        var gameEntity = await GetGameEntityAsync(gameId, cancellationToken)
            ?? throw new KeyNotFoundException($"Game '{gameId}' was not found and is considered deleted.");
        return await AdvanceAutomaticTurnFlowAsync(gameEntity, gameId, gameEngine, cancellationToken);
    }

    public bool IsGameBusy(string gameId)
    {
        return !string.IsNullOrWhiteSpace(gameId) && _busyGames.ContainsKey(gameId);
    }

    public ValueTask EnqueueActionAsync(string gameId, PlayerAction action, CancellationToken cancellationToken = default)
    {
        ValidateGameId(gameId);
        ArgumentNullException.ThrowIfNull(action);

        if (!_busyGames.TryAdd(gameId, 0))
        {
            throw new InvalidOperationException("Another action is still being processed for this game. Wait for the board to finish updating and try again.");
        }

        try
        {
            return _actions.Writer.WriteAsync(new QueuedAction(gameId, action), cancellationToken);
        }
        catch
        {
            _busyGames.TryRemove(gameId, out _);
            throw;
        }
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
                RailBaronGameEngine? gameEngine = null;

                try
                {
                    gameEngine = await GetOrCreateGameEngineAsync(queuedAction.GameId, stoppingToken);
                    var gameEntity = await GetGameEntityAsync(queuedAction.GameId, stoppingToken)
                        ?? throw new KeyNotFoundException($"Game '{queuedAction.GameId}' was not found and is considered deleted.");

                    var snapshotBeforeAction = gameEngine.ToSnapshot();
                    ProcessTurn(gameEntity, gameEngine, queuedAction.Action);
                    var snapshot = gameEngine.ToSnapshot();

                    await PersistEventAsync(
                        queuedAction.GameId,
                        snapshot,
                        queuedAction.Action.Kind.ToString(),
                        DescribeAction(gameEntity, queuedAction.Action, snapshotBeforeAction, snapshot, gameEngine),
                        queuedAction.Action.PlayerId,
                        queuedAction.Action,
                        stoppingToken);

                    snapshot = await AdvanceAutomaticTurnFlowAsync(gameEntity, queuedAction.GameId, gameEngine, stoppingToken);

                    OnStateChanged?.Invoke(queuedAction.GameId, snapshot);
                }
                catch (Exception exception)
                {
                    if (gameEngine is not null && IsStaleQueuedAction(gameEngine, queuedAction.Action, exception))
                    {
                        if (_logger.IsEnabled(LogLevel.Information))
                        {
                            var actionKind = queuedAction.Action.Kind;
                            var actionPlayerId = queuedAction.Action.PlayerId;
                            var gameId = queuedAction.GameId;
                            var activePlayerName = gameEngine.CurrentTurn.ActivePlayer.Name;

#pragma warning disable CA1848 // Use the LoggerMessage delegates
                            _logger.LogInformation(
                                "Discarded stale queued action {ActionKind} for player {ActionPlayerId} in game {GameId}; active player is now {ActivePlayerName}.",
                                actionKind,
                                actionPlayerId,
                                gameId,
                                activePlayerName);
#pragma warning restore CA1848 // Use the LoggerMessage delegates
                        }

                        OnStateChanged?.Invoke(queuedAction.GameId, gameEngine.ToSnapshot());
                        continue;
                    }

#pragma warning disable CA1848 // Use the LoggerMessage delegates
                    _logger.LogError(
                        exception,
                        "Failed to process queued action {ActionKind} for game {GameId}.",
                        queuedAction.Action.Kind,
                        queuedAction.GameId);
#pragma warning restore CA1848 // Use the LoggerMessage delegates

                    OnActionFailed?.Invoke(
                        queuedAction.GameId,
                        new GameActionFailure
                        {
                            ActionKind = queuedAction.Action.Kind,
                            Message = BuildActionFailureMessage(queuedAction.Action, exception)
                        });

                    if (_gameEngines.TryGetValue(queuedAction.GameId, out var failedGameEngine))
                    {
                        OnStateChanged?.Invoke(queuedAction.GameId, failedGameEngine.ToSnapshot());
                    }
                }
                finally
                {
                    _busyGames.TryRemove(queuedAction.GameId, out _);
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
        if (action is not (BidAction or AuctionPassAction or AuctionDropOutAction)
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

            case ChooseDestinationRegionAction chooseDestinationRegionAction:
                gameEngine.ChooseDestinationRegion(chooseDestinationRegionAction.SelectedRegionCode);
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

                gameEngine.SubmitAuctionBid(railroadToBid, bidder, bidAction.AmountBid);
                break;

            case AuctionPassAction passAction:
                var passingPlayer = gameEngine.Players.FirstOrDefault(player => string.Equals(player.Name, passAction.PlayerId, StringComparison.Ordinal));
                if (passingPlayer is null)
                {
                    throw new InvalidOperationException($"Auction participant '{passAction.PlayerId}' is not in the game.");
                }

                gameEngine.PassAuctionTurn(FindRailroad(gameEngine, passAction.RailroadIndex), passingPlayer);
                break;

            case AuctionDropOutAction dropOutAction:
                var droppingPlayer = gameEngine.Players.FirstOrDefault(player => string.Equals(player.Name, dropOutAction.PlayerId, StringComparison.Ordinal));
                if (droppingPlayer is null)
                {
                    throw new InvalidOperationException($"Auction participant '{dropOutAction.PlayerId}' is not in the game.");
                }

                gameEngine.DropOutOfAuction(FindRailroad(gameEngine, dropOutAction.RailroadIndex), droppingPlayer);
                break;

            case SellRailroadAction sellRailroadAction:
                var railroadToSell = FindRailroad(gameEngine, sellRailroadAction.RailroadIndex);
                var expectedSalePrice = railroadToSell.PurchasePrice / 2;
                if (sellRailroadAction.AmountReceived != 0
                    && sellRailroadAction.AmountReceived != expectedSalePrice)
                {
                    throw new InvalidOperationException($"Selling to bank requires AmountReceived = 0 or {expectedSalePrice}.");
                }

                gameEngine.SellRailroadToBank(railroadToSell);
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

    private void ValidateActionAuthorization(GameEntity gameEntity, RailBaronGameEngine gameEngine, PlayerAction action)
    {
        var activePlayerIndex = gameEngine.CurrentTurn.ActivePlayer.Index;
        var allowsNonActiveParticipant = action is BidAction or AuctionPassAction or AuctionDropOutAction;
        if (!allowsNonActiveParticipant && action.PlayerIndex.HasValue && action.PlayerIndex.Value != activePlayerIndex)
        {
            throw new InvalidOperationException($"Action player index '{action.PlayerIndex.Value}' does not match active player index '{activePlayerIndex}'.");
        }

        var actionPlayerIndex = action.PlayerIndex
            ?? gameEngine.Players
                .Select((player, index) => new { player, index })
                .Where(entry => string.Equals(entry.player.Name, action.PlayerId, StringComparison.Ordinal))
                .Select(entry => entry.index)
                .DefaultIfEmpty(-1)
                .First();

        if (actionPlayerIndex >= 0
            && actionPlayerIndex < gameEngine.Players.Count
            && !gameEngine.Players[actionPlayerIndex].IsActive)
        {
            throw new InvalidOperationException("Eliminated players may only spectate and cannot perform game actions.");
        }

        var selections = GamePlayerSelectionSerialization.Deserialize(gameEntity.PlayersJson);
        var authorizedPlayerIndex = allowsNonActiveParticipant ? actionPlayerIndex : activePlayerIndex;
        if (authorizedPlayerIndex < 0 || authorizedPlayerIndex >= selections.Count)
        {
            throw new InvalidOperationException("Unable to resolve the acting player's roster binding.");
        }

        var slotUserId = selections[authorizedPlayerIndex].UserId;
        var activeBotAssignment = _botTurnService.GetActiveAssignment(gameEntity, slotUserId);
        var controllerState = _gamePresenceService.ResolveSeatControllerState(gameEntity.GameId, slotUserId, activeBotAssignment);
        var authorized = PlayerControlRules.CanUserControlSlot(controllerState, action.ActorUserId, isPlayerActive: true)
            || PlayerControlRules.CanServerControlSlot(controllerState, action.ActorUserId, _botOptions.ServerActorUserId, isPlayerActive: true);
        if (!authorized)
        {
            throw new InvalidOperationException(allowsNonActiveParticipant
                ? "Only the controlling participant for the acting player may perform this auction action."
                : "Only the controlling participant for the active player may perform this action.");
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
        GameEntity gameEntity,
        string gameId,
        RailBaronGameEngine gameEngine,
        CancellationToken cancellationToken)
    {
        var playerSelections = GamePlayerSelectionSerialization.Deserialize(gameEntity.PlayersJson);
        await _botTurnService.EnsureBotSeatAssignmentsAsync(gameEntity, playerSelections, gameEntity.CreatorId, cancellationToken);

        var snapshot = gameEngine.ToSnapshot();
        var originalBotAssignmentsJson = gameEntity.BotAssignmentsJson;

        for (var step = 0; step < AutomaticTurnFlowStepLimit; step++)
        {
            var automaticAction = await CreateAutomaticTurnActionAsync(gameEntity, gameEngine, cancellationToken);
            if (automaticAction is null)
            {
                await PersistBotAssignmentsIfChangedAsync(gameEntity, originalBotAssignmentsJson, cancellationToken);
                return snapshot;
            }

            var snapshotBeforeAction = snapshot;
            if (automaticAction.BotMetadata is null && string.IsNullOrWhiteSpace(automaticAction.ActorUserId))
            {
                ProcessAutomaticTurnAction(gameEngine, automaticAction);
            }
            else
            {
                ProcessTurn(gameEntity, gameEngine, automaticAction);
            }
            snapshot = gameEngine.ToSnapshot();

            await PersistEventAsync(
                gameId,
                snapshot,
                automaticAction.Kind.ToString(),
                DescribeAction(gameEntity, automaticAction, snapshotBeforeAction, snapshot, gameEngine),
                automaticAction.PlayerId,
                automaticAction,
                cancellationToken);

            OnStateChanged?.Invoke(gameId, snapshot);

            if (automaticAction.BotMetadata is not null && _botOptions.AutomaticActionDelayMilliseconds > 0)
            {
                await Task.Delay(_botOptions.AutomaticActionDelayMilliseconds, cancellationToken);
            }
        }

        await PersistBotAssignmentsIfChangedAsync(gameEntity, originalBotAssignmentsJson, cancellationToken);

        throw new InvalidOperationException($"Automatic turn flow did not stabilize within {AutomaticTurnFlowStepLimit} steps.");
    }

    private async Task<PlayerAction?> CreateAutomaticTurnActionAsync(
        GameEntity gameEntity,
        RailBaronGameEngine gameEngine,
        CancellationToken cancellationToken)
    {
        if (!HasConnectedTableUser(gameEntity))
        {
            return null;
        }

        var builtInAction = CreateAutomaticTurnAction(gameEngine);
        if (builtInAction is not null)
        {
            return builtInAction;
        }

        if (_mapDefinition is null)
        {
            throw new InvalidOperationException("The game map definition has not been initialized yet.");
        }

        if (!IsAiControlledSeatTurn(gameEntity, gameEngine))
        {
            return null;
        }

        return await _botTurnService.CreateBotActionAsync(gameEntity, gameEngine, _mapDefinition, cancellationToken);
    }

    private bool IsAiControlledSeatTurn(GameEntity gameEntity, RailBaronGameEngine gameEngine)
    {
        var selections = GamePlayerSelectionSerialization.Deserialize(gameEntity.PlayersJson);
        var actingPlayerIndex = gameEngine.CurrentTurn.AuctionState?.CurrentBidderPlayerIndex ?? gameEngine.CurrentTurn.ActivePlayer.Index;
        if (actingPlayerIndex < 0 || actingPlayerIndex >= selections.Count)
        {
            return false;
        }

        var slotUserId = selections[actingPlayerIndex].UserId;
        var activeAssignment = _botTurnService.GetActiveAssignment(gameEntity, slotUserId);
        var controllerState = _gamePresenceService.ResolveSeatControllerState(gameEntity.GameId, slotUserId, activeAssignment);
        return PlayerControlRules.IsAiControllerMode(controllerState.ControllerMode);
    }

    private bool HasConnectedTableUser(GameEntity gameEntity)
    {
        var candidateUserIds = new List<string?>();
        foreach (var selection in GamePlayerSelectionSerialization.Deserialize(gameEntity.PlayersJson))
        {
            candidateUserIds.Add(selection.UserId);

            var delegatedControllerUserId = _gamePresenceService.GetDelegatedControllerUserId(gameEntity.GameId, selection.UserId);
            if (!string.IsNullOrWhiteSpace(delegatedControllerUserId))
            {
                candidateUserIds.Add(delegatedControllerUserId);
            }
        }

        return _gamePresenceService.HasAnyConnectedUsers(gameEntity.GameId, candidateUserIds);
    }

    private void OnPresenceChanged(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId)
            || !_pendingAutomaticFlowResumes.TryAdd(gameId, 0))
        {
            return;
        }

        _ = ResumeAutomaticTurnFlowAsync(gameId);
    }

    private async Task ResumeAutomaticTurnFlowAsync(string gameId)
    {
        try
        {
            await _mapReady.Task.WaitAsync(CancellationToken.None);

            var processingAcquired = false;
            for (var attempt = 0; attempt < 20 && !processingAcquired; attempt++)
            {
                processingAcquired = _busyGames.TryAdd(gameId, 0);
                if (!processingAcquired)
                {
                    await Task.Delay(100, CancellationToken.None);
                }
            }

            if (!processingAcquired)
            {
                return;
            }

            try
            {
                var gameEntity = await GetGameEntityAsync(gameId, CancellationToken.None);
                if (gameEntity is null || !HasConnectedTableUser(gameEntity))
                {
                    return;
                }

                var gameEngine = await GetOrCreateGameEngineAsync(gameId, CancellationToken.None);
                var snapshot = await AdvanceAutomaticTurnFlowAsync(gameEntity, gameId, gameEngine, CancellationToken.None);
                OnStateChanged?.Invoke(gameId, snapshot);
            }
            finally
            {
                _busyGames.TryRemove(gameId, out _);
            }
        }
        catch (Exception exception)
        {
#pragma warning disable CA1848 // Use the LoggerMessage delegates
            _logger.LogError(exception, "Failed to resume automatic turn flow for game {GameId} after a presence change.", gameId);
#pragma warning restore CA1848 // Use the LoggerMessage delegates
        }
        finally
        {
            _pendingAutomaticFlowResumes.TryRemove(gameId, out _);
        }
    }

    private async Task PersistBotAssignmentsIfChangedAsync(
        GameEntity gameEntity,
        string originalBotAssignmentsJson,
        CancellationToken cancellationToken)
    {
        if (string.Equals(originalBotAssignmentsJson, gameEntity.BotAssignmentsJson, StringComparison.Ordinal))
        {
            return;
        }

        var updateEntity = new TableEntity(gameEntity.PartitionKey, gameEntity.RowKey)
        {
            [nameof(GameEntity.BotAssignmentsJson)] = gameEntity.BotAssignmentsJson
        };

        await _gamesTable.UpdateEntityAsync(updateEntity, ResolveIfMatchETag(gameEntity), TableUpdateMode.Merge, cancellationToken);

        var refreshedGameEntity = await GetGameEntityAsync(gameEntity.GameId, cancellationToken);
        if (refreshedGameEntity is null)
        {
            return;
        }

        gameEntity.ETag = refreshedGameEntity.ETag;
        gameEntity.Timestamp = refreshedGameEntity.Timestamp;
        gameEntity.BotAssignmentsJson = refreshedGameEntity.BotAssignmentsJson;
    }

    private static Azure.ETag ResolveIfMatchETag(GameEntity gameEntity)
    {
        return string.IsNullOrWhiteSpace(gameEntity.ETag.ToString())
            ? Azure.ETag.All
            : gameEntity.ETag;
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

    private string DescribeAction(GameEntity gameEntity, PlayerAction action, RailBaronGameState snapshotBeforeAction, RailBaronGameState snapshotAfterAction, RailBaronGameEngine gameEngine)
    {
        var actorName = ResolveActorName(action, snapshotBeforeAction);

        var description = action switch
        {
            PickDestinationAction => DescribeDestinationPick(actorName, action, snapshotAfterAction),
            ChooseDestinationRegionAction chooseDestinationRegionAction => DescribeDestinationRegionChoice(actorName, chooseDestinationRegionAction, snapshotAfterAction),
            RollDiceAction => $"{actorName} rolled {FormatDiceRoll(snapshotAfterAction.Turn.DiceResult, snapshotAfterAction.Turn.BonusRollAvailable, action as RollDiceAction)}",
            ChooseRouteAction => string.Empty,
            MoveAction moveAction => DescribeMove(actorName, moveAction, snapshotBeforeAction),
            PurchaseRailroadAction purchaseAction => $"{actorName} bought the {GetRailroadDisplayName(FindRailroad(gameEngine, purchaseAction.RailroadIndex))} railroad for {FormatCurrency(ResolveAmountPaid(purchaseAction.AmountPaid, FindRailroad(gameEngine, purchaseAction.RailroadIndex).PurchasePrice))}",
            StartAuctionAction auctionAction => DescribeAuctionAction(
                auctionAction,
                snapshotBeforeAction,
                snapshotAfterAction,
                gameEngine,
                $"{actorName} started an auction for the {GetRailroadDisplayName(FindRailroad(gameEngine, auctionAction.RailroadIndex))} railroad"),
            BidAction bidAction => DescribeAuctionAction(
                bidAction,
                snapshotBeforeAction,
                snapshotAfterAction,
                gameEngine,
                $"{actorName} bid {FormatCurrency(bidAction.AmountBid)} for the {GetRailroadDisplayName(FindRailroad(gameEngine, bidAction.RailroadIndex))} railroad"),
            AuctionPassAction passAction => DescribeAuctionAction(
                passAction,
                snapshotBeforeAction,
                snapshotAfterAction,
                gameEngine,
                $"{actorName} passed in the auction for the {GetRailroadDisplayName(FindRailroad(gameEngine, passAction.RailroadIndex))} railroad"),
            AuctionDropOutAction dropOutAction => DescribeAuctionAction(
                dropOutAction,
                snapshotBeforeAction,
                snapshotAfterAction,
                gameEngine,
                $"{actorName} dropped out of the auction for the {GetRailroadDisplayName(FindRailroad(gameEngine, dropOutAction.RailroadIndex))} railroad"),
            SellRailroadAction sellAction => DescribeRailroadSale(actorName, sellAction, gameEngine),
            BuyEngineAction buyEngineAction => $"{actorName} bought a {buyEngineAction.EngineType} for {FormatCurrency(buyEngineAction.AmountPaid)}",
            DeclinePurchaseAction => $"{actorName} declined the purchase opportunity",
            EndTurnAction => $"{actorName} ended their turn",
            _ => $"{actorName} performed {action.Kind}"
        };

        return string.Concat(description, BuildActionAttributionSuffix(gameEntity, action, snapshotBeforeAction));
    }

    private string BuildActionAttributionSuffix(GameEntity gameEntity, PlayerAction action, RailBaronGameState snapshotBeforeAction)
    {
        if (action.BotMetadata is not null)
        {
            return BuildBotActionSuffix(action.BotMetadata);
        }

        if (string.IsNullOrWhiteSpace(action.ActorUserId))
        {
            return string.Empty;
        }

        var selections = GamePlayerSelectionSerialization.Deserialize(gameEntity.PlayersJson);
        var slotUserId = ResolveActingSlotUserId(selections, action, snapshotBeforeAction);
        if (string.IsNullOrWhiteSpace(slotUserId)
            || string.Equals(slotUserId, action.ActorUserId, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var activeAssignment = _botTurnService.GetActiveAssignment(gameEntity, slotUserId);
        var controllerState = _gamePresenceService.ResolveSeatControllerState(gameEntity.GameId, slotUserId, activeAssignment);
        if (!string.Equals(controllerState.ControllerMode, SeatControllerModes.HumanDelegated, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var controllerDisplayName = ResolveParticipantDisplayName(selections, action.ActorUserId);
        return string.IsNullOrWhiteSpace(controllerDisplayName)
            ? string.Empty
            : string.Concat(" [", controllerDisplayName, "]");
    }

    private static string BuildBotActionSuffix(BotRecordedActionMetadata metadata)
    {
        if (string.Equals(metadata.ControllerMode, SeatControllerModes.AiBotSeat, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var suffixLabel = string.Equals(metadata.ControllerMode, SeatControllerModes.AiGhost, StringComparison.OrdinalIgnoreCase)
            ? "AUTO"
            : !string.IsNullOrWhiteSpace(metadata.BotName)
                ? metadata.BotName
                : "AUTO";
        var suffix = string.Concat(" [", suffixLabel);
        if (!string.IsNullOrWhiteSpace(metadata.FallbackReason))
        {
            suffix = string.Concat(suffix, "; ", metadata.FallbackReason);
        }

        return string.Concat(suffix, "]");
    }

    private static string? ResolveActingSlotUserId(IReadOnlyList<GamePlayerSelection> selections, PlayerAction action, RailBaronGameState snapshot)
    {
        if (action.PlayerIndex.HasValue
            && action.PlayerIndex.Value >= 0
            && action.PlayerIndex.Value < selections.Count)
        {
            return selections[action.PlayerIndex.Value].UserId;
        }

        var slotIndex = snapshot.Players.FindIndex(player => string.Equals(player.Name, action.PlayerId, StringComparison.Ordinal));
        if (slotIndex >= 0 && slotIndex < selections.Count)
        {
            return selections[slotIndex].UserId;
        }

        var selection = selections.FirstOrDefault(player => string.Equals(player.DisplayName, action.PlayerId, StringComparison.OrdinalIgnoreCase));
        return selection?.UserId;
    }

    private static string ResolveParticipantDisplayName(IReadOnlyList<GamePlayerSelection> selections, string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return string.Empty;
        }

        var participant = selections.FirstOrDefault(selection => string.Equals(selection.UserId, userId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(participant?.DisplayName))
        {
            return participant.DisplayName;
        }

        return userId;
    }

    private static string BuildActionFailureMessage(PlayerAction action, Exception exception)
    {
        if (action is MoveAction && string.Equals(exception.Message, "Segment reuse violation.", StringComparison.Ordinal))
        {
            return "That route reuses a track segment. Choose a route that does not travel the same segment twice in one trip.";
        }

        return string.IsNullOrWhiteSpace(exception.Message)
            ? $"Unable to process {action.Kind}."
            : exception.Message;
    }

    private static bool IsStaleQueuedAction(RailBaronGameEngine gameEngine, PlayerAction action, Exception exception)
    {
        if (action.BotMetadata is not null || string.IsNullOrWhiteSpace(action.ActorUserId))
        {
            return false;
        }

        if (exception is not InvalidOperationException)
        {
            return false;
        }

        if (action is BidAction or AuctionPassAction or AuctionDropOutAction)
        {
            return false;
        }

        var activePlayer = gameEngine.CurrentTurn.ActivePlayer;
        return !string.Equals(activePlayer.Name, action.PlayerId, StringComparison.Ordinal)
            || (action.PlayerIndex.HasValue && action.PlayerIndex.Value != activePlayer.Index);
    }

    private static string DescribeDestinationPick(string actorName, PlayerAction action, RailBaronGameState snapshot)
    {
        if (string.Equals(snapshot.Turn.Phase, nameof(TurnPhase.RegionChoice), StringComparison.OrdinalIgnoreCase)
            && snapshot.Turn.PendingRegionChoice is not null)
        {
            return $"{actorName} must choose a replacement destination region.";
        }

        var destinationName = TryGetPlayerState(action, snapshot)?.DestinationCityName;
        return string.IsNullOrWhiteSpace(destinationName)
            ? $"{actorName} drew a new destination"
            : $"{actorName} has a new destination: {destinationName}";
    }

    private static string DescribeDestinationRegionChoice(string actorName, ChooseDestinationRegionAction action, RailBaronGameState snapshot)
    {
        var destinationName = TryGetPlayerState(action, snapshot)?.DestinationCityName;
        if (string.IsNullOrWhiteSpace(destinationName))
        {
            return string.Equals(snapshot.Turn.Phase, nameof(TurnPhase.EndTurn), StringComparison.OrdinalIgnoreCase)
                ? $"{actorName} chose {action.SelectedRegionCode} as the replacement destination region and lost the turn after redrawing the current city."
                : $"{actorName} chose {action.SelectedRegionCode} as the replacement destination region.";
        }

        return $"{actorName} chose {action.SelectedRegionCode} as the replacement destination region and received {destinationName}.";
    }

    private static string DescribeMove(string actorName, MoveAction action, RailBaronGameState snapshot)
    {
        var moveAmount = Math.Max(0, action.PointsTaken.Count - 1);
        var isBonusMove = snapshot.Turn.DiceResult?.WhiteDice is { Length: >= 2 } whiteDice
            && whiteDice.All(value => value == 0)
            && snapshot.Turn.DiceResult.RedDie.HasValue
            && snapshot.Turn.MovementAllowance == snapshot.Turn.DiceResult.RedDie.Value;
        var spaceLabel = moveAmount == 1 ? "space" : "spaces";

        return isBonusMove
            ? $"{actorName} moved {moveAmount} bonus {spaceLabel}"
            : $"{actorName} moved {moveAmount} {spaceLabel}";
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
        var amountReceived = action.AmountReceived > 0
            ? action.AmountReceived
            : railroad.PurchasePrice / 2;
        if (amountReceived > 0)
        {
            return $"{actorName} sold the {GetRailroadDisplayName(railroad)} railroad to the bank for {FormatCurrency(amountReceived)}";
        }

        return $"{actorName} sold the {GetRailroadDisplayName(railroad)} railroad to the bank";
    }

    private static string DescribeAuctionAction(
        PlayerAction action,
        RailBaronGameState snapshotBeforeAction,
        RailBaronGameState snapshotAfterAction,
        RailBaronGameEngine gameEngine,
        string defaultDescription)
    {
        var railroadIndex = action switch
        {
            StartAuctionAction startAuctionAction => startAuctionAction.RailroadIndex,
            BidAction bidAction => bidAction.RailroadIndex,
            AuctionPassAction passAction => passAction.RailroadIndex,
            AuctionDropOutAction dropOutAction => dropOutAction.RailroadIndex,
            _ => -1
        };

        if (railroadIndex < 0)
        {
            return defaultDescription;
        }

        var railroad = FindRailroad(gameEngine, railroadIndex);

        var sellerPlayerIndex = snapshotBeforeAction.Turn.Auction?.SellerPlayerIndex ?? ResolvePlayerIndex(action, snapshotBeforeAction);
        var sellerOwnedRailroadBeforeAction = sellerPlayerIndex.HasValue
            && sellerPlayerIndex.Value >= 0
            && sellerPlayerIndex.Value < snapshotBeforeAction.Players.Count
            && snapshotBeforeAction.Players[sellerPlayerIndex.Value].OwnedRailroadIndices.Contains(railroadIndex);

        if (string.Equals(snapshotBeforeAction.Turn.Phase, nameof(TurnPhase.UseFees), StringComparison.OrdinalIgnoreCase)
            && sellerOwnedRailroadBeforeAction
            && railroad.Owner is null)
        {
            return string.Concat(
                defaultDescription,
                "; no bids remained, so ",
                GetRailroadDisplayName(railroad),
                " was sold to the bank for ",
                FormatCurrency(railroad.PurchasePrice / 2));
        }

        var auctionBeforeAction = snapshotBeforeAction.Turn.Auction;
        if (auctionBeforeAction is null || auctionBeforeAction.RailroadIndex != railroadIndex)
        {
            return defaultDescription;
        }

        if (railroad.Owner is null || railroad.Owner.Index == auctionBeforeAction.SellerPlayerIndex)
        {
            return defaultDescription;
        }

        var winningBid = action switch
        {
            BidAction bidAction => bidAction.AmountBid,
            AuctionPassAction or AuctionDropOutAction => auctionBeforeAction.CurrentBid,
            _ => 0
        };

        if (winningBid <= 0)
        {
            return defaultDescription;
        }

        var sellerName = string.IsNullOrWhiteSpace(auctionBeforeAction.SellerPlayerName)
            ? ResolvePlayerName(snapshotAfterAction, auctionBeforeAction.SellerPlayerIndex)
            : auctionBeforeAction.SellerPlayerName;

        return $"{railroad.Owner.Name} bought the {GetRailroadDisplayName(railroad)} railroad for {FormatCurrency(winningBid)}; {FormatCurrency(winningBid)} was transferred to {sellerName}.";
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

    private static BotTurnService CreateFallbackBotTurnService(
        TableServiceClient tableServiceClient,
        GamePresenceService gamePresenceService,
        IOptions<PurchaseRulesOptions> purchaseRulesOptions)
    {
        var botOptions = Options.Create(new BotOptions());
        return new BotTurnService(
            new UserDirectoryService(tableServiceClient),
            new BotDecisionPromptBuilder(),
                new OpenAiBotClient(new NoOpHttpClientFactory(), botOptions, NullLogger<OpenAiBotClient>.Instance),
            gamePresenceService,
            new NetworkCoverageService(),
            botOptions,
                purchaseRulesOptions,
                NullLogger<BotTurnService>.Instance);
    }

    private sealed class NoOpHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient();
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
        var usesBaseRateRailroad = false;
        var opposingOwnerIndices = new HashSet<int>();

        foreach (var segment in segments)
        {
            var railroad = state.Railroads.FirstOrDefault(candidate => candidate.Index == segment.RailroadIndex);
            if (railroad is null || railroad.Owner is null)
            {
                usesBaseRateRailroad = true;
                continue;
            }

            if (railroad.Owner == player)
            {
                usesBaseRateRailroad = true;
                continue;
            }

            opposingOwnerIndices.Add(railroad.Owner.Index);
        }

        var bankFee = usesBaseRateRailroad ? 1000 : 0;
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
