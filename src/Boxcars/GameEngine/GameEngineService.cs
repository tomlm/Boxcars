using System.Collections.Concurrent;
using System.Globalization;
using System.Threading.Channels;
using Azure;
using Azure.Data.Tables;
using Boxcars.Data;
using Boxcars.Engine;
using Boxcars.Data.Maps;
using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Domain;
using Boxcars.Identity;
using Boxcars.Services;
using Boxcars.Services.Maps;
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
    private const string EventRowKeyPrefix = "Event_";
    private const string EventRowKeyExclusiveUpperBound = "Event`";

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
    private readonly BotOptions _botOptions;
    private readonly GameSettingsResolver _gameSettingsResolver;
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
        GameSettingsResolver gameSettingsResolver,
        IOptions<BotOptions> botOptions,
        ILogger<GameEngineService> logger)
        : this(
            webHostEnvironment,
            tableServiceClient.GetTableClient(TableNames.GamesTable),
            gamePresenceService,
            botTurnService,
            gameSettingsResolver,
            botOptions,
            logger)
    {
    }

    public GameEngineService(
        IWebHostEnvironment webHostEnvironment,
        TableServiceClient tableServiceClient,
        GamePresenceService gamePresenceService,
        BotTurnService botTurnService,
        IOptions<BotOptions> botOptions,
        ILogger<GameEngineService> logger)
        : this(
            webHostEnvironment,
            tableServiceClient,
            gamePresenceService,
            botTurnService,
            new GameSettingsResolver(),
            botOptions,
            logger)
    {
    }

    public GameEngineService(
        IWebHostEnvironment webHostEnvironment,
        TableClient gamesTable,
        GamePresenceService gamePresenceService,
        BotTurnService botTurnService,
        GameSettingsResolver gameSettingsResolver,
        IOptions<BotOptions> botOptions,
        ILogger<GameEngineService> logger)
    {
        _webHostEnvironment = webHostEnvironment;
        _gamesTable = gamesTable;
        _gamePresenceService = gamePresenceService;
        _botTurnService = botTurnService;
        _botOptions = botOptions.Value;
        _gameSettingsResolver = gameSettingsResolver;
        _logger = logger;
        _gamePresenceService.PresenceChanged += OnPresenceChanged;
    }

    public GameEngineService(
        IWebHostEnvironment webHostEnvironment,
        TableClient gamesTable,
        GamePresenceService gamePresenceService,
        BotTurnService botTurnService,
        IOptions<BotOptions> botOptions,
        ILogger<GameEngineService> logger)
        : this(
            webHostEnvironment,
            gamesTable,
            gamePresenceService,
            botTurnService,
            new GameSettingsResolver(),
            botOptions,
            logger)
    {
    }

    public GameEngineService(
        IWebHostEnvironment webHostEnvironment,
        TableServiceClient tableServiceClient,
        GamePresenceService gamePresenceService,
        GameSettingsResolver gameSettingsResolver,
        IOptions<BotOptions> botOptions,
        ILogger<GameEngineService> logger)
        : this(
            webHostEnvironment,
            tableServiceClient,
            gamePresenceService,
            CreateFallbackBotTurnService(tableServiceClient, gamePresenceService),
            gameSettingsResolver,
            botOptions,
            logger)
    {
    }

    public GameEngineService(
        IWebHostEnvironment webHostEnvironment,
        TableServiceClient tableServiceClient,
        GamePresenceService gamePresenceService,
        IOptions<BotOptions> botOptions,
        ILogger<GameEngineService> logger)
        : this(
            webHostEnvironment,
            tableServiceClient,
            gamePresenceService,
            new GameSettingsResolver(),
            botOptions,
            logger)
    {
    }

    public event Action<string, GameStateUpdate>? OnStateChanged;
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
        var normalizedSettings = _gameSettingsResolver.Normalize(request.Settings);

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
            CityProbabilityOverrides = request.CityProbabilityOverrides,
            RailroadPriceOverrides = request.RailroadPriceOverrides,
            Seats = request.Players
                .Select((player, seatIndex) => new GameSeatDefinition
                {
                    SeatIndex = seatIndex,
                    PlayerUserId = player.UserId,
                    DisplayName = player.DisplayName,
                    Color = player.Color
                })
                .ToList()
        };
        _gameSettingsResolver.Apply(gameEntity, normalizedSettings);

        var createdGameEngine = CreateGameEngine(gameEntity, players, normalizedSettings);
        if (!_gameEngines.TryAdd(gameId, createdGameEngine))
        {
            throw new InvalidOperationException($"A game with id '{gameId}' already exists.");
        }

        var playerStates = GameSeatStateProjection.BuildTransientStates(gameEntity, createdGameEngine.ToSnapshot()).ToList();

        await _botTurnService.EnsureBotSeatControlStatesAsync(gameId, playerStates, request.CreatorUserId, cancellationToken);

        await _gamesTable.AddEntityAsync(gameEntity, cancellationToken);

        var snapshot = createdGameEngine.ToSnapshot();
        ApplySeatAndControlMetadata(snapshot, gameEntity, playerStates);
        await PersistEventAsync(gameId, snapshot, "CreateGame", "Game created.", request.CreatorUserId, new
        {
            request.MapFileName,
            request.Players
        }, cancellationToken);

        snapshot = await AdvanceAutomaticTurnFlowAsync(gameEntity, gameId, createdGameEngine, cancellationToken);

        PublishStateChanged(gameId, snapshot);
        return gameId;
    }

    public async Task<RailBaronGameState> GetCurrentStateAsync(string gameId, CancellationToken cancellationToken = default)
    {
        ValidateGameId(gameId);
        await _mapReady.Task.WaitAsync(cancellationToken);
        var gameEngine = await GetOrCreateGameEngineAsync(gameId, cancellationToken);
        var gameEntity = await GetGameEntityAsync(gameId, cancellationToken)
            ?? throw new KeyNotFoundException($"Game '{gameId}' was not found and is considered deleted.");
        var snapshot = await AdvanceAutomaticTurnFlowAsync(gameEntity, gameId, gameEngine, cancellationToken);
        var playerStates = await GetGameSeatStatesAsync(gameId, cancellationToken);
        ApplySeatAndControlMetadata(snapshot, gameEntity, playerStates);
        return snapshot;
    }

    public async Task SynchronizeStateAsync(string gameId, RailBaronGameState state, CancellationToken cancellationToken = default)
    {
        ValidateGameId(gameId);
        ArgumentNullException.ThrowIfNull(state);

        await _mapReady.Task.WaitAsync(cancellationToken);
        var gameEntity = await GetGameEntityAsync(gameId, cancellationToken)
            ?? throw new KeyNotFoundException($"Game '{gameId}' was not found and is considered deleted.");
        var resolvedSettings = _gameSettingsResolver.Resolve(gameEntity);

        if (_gameEngines.TryGetValue(gameId, out var existingGameEngine)
            && existingGameEngine.Settings != resolvedSettings.Settings)
        {
            throw new InvalidOperationException("Game settings are immutable and no longer match the persisted game record.");
        }

        _gameEngines[gameId] = RestoreGameEngine(gameEntity, state, resolvedSettings.Settings);
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
        if (!_busyGames.TryAdd(gameId, 0))
        {
            throw new InvalidOperationException("Another action is still being processed for this game. Wait for the board to finish updating and try again.");
        }

        try
        {
            await _mapReady.Task.WaitAsync(cancellationToken);

            var events = await GetEventsOrderedAsync(gameId, cancellationToken);
            if (events.Count < 2)
            {
                return false;
            }

            var previousEvent = events[^2];
            var restoredSnapshot = GameEventSerialization.DeserializeSnapshot(previousEvent.SerializedGameState);
            var gameEntity = await GetGameEntityAsync(gameId, cancellationToken)
                ?? throw new KeyNotFoundException($"Game '{gameId}' was not found and is considered deleted.");
            var resolvedSettings = _gameSettingsResolver.Resolve(gameEntity);
        var restoredEngine = RestoreGameEngine(gameEntity, restoredSnapshot, resolvedSettings.Settings);

            _gameEngines[gameId] = restoredEngine;

            var persistedUndoEvent = await PersistEventAsync(
                gameId,
                restoredSnapshot,
                "Undo",
                $"Undo applied. Reverted action '{events[^1].EventKind}'.",
                previousEvent.CreatedBy,
                new { RevertedEvent = events[^1].RowKey },
                cancellationToken);

            await RebuildPlayerStatisticsAsync(gameId, events.Take(Math.Max(0, events.Count - 1)).ToList(), cancellationToken);

            PublishStateChanged(
                gameId,
                restoredSnapshot,
                GameService.BuildTimelineItems(persistedUndoEvent, previousGameEvent: null, resolvedSettings.Settings.AnnouncingCash));
            return true;
        }
        finally
        {
            _busyGames.TryRemove(gameId, out _);
        }
    }

    public async Task<bool> UndoToEventAsync(
        string gameId,
        string targetEventRowKey,
        string targetDescription,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ValidateGameId(gameId);
        if (string.IsNullOrWhiteSpace(targetEventRowKey))
        {
            throw new ArgumentException("A target history event is required.", nameof(targetEventRowKey));
        }

        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new ArgumentException("An acting user is required.", nameof(actorUserId));
        }

        if (!_busyGames.TryAdd(gameId, 0))
        {
            throw new InvalidOperationException("Another action is still being processed for this game. Wait for the board to finish updating and try again.");
        }

        try
        {
            await _mapReady.Task.WaitAsync(cancellationToken);

            var events = await GetEventsOrderedAsync(gameId, cancellationToken);
            var targetIndex = events.FindIndex(gameEvent => string.Equals(gameEvent.RowKey, targetEventRowKey, StringComparison.Ordinal));
            if (targetIndex < 0)
            {
                return false;
            }

            var targetEvent = events[targetIndex];
            var restoredSnapshot = GameEventSerialization.DeserializeSnapshot(targetEvent.SerializedGameState);
            var gameEntity = await GetGameEntityAsync(gameId, cancellationToken)
                ?? throw new KeyNotFoundException($"Game '{gameId}' was not found and is considered deleted.");
            var resolvedSettings = _gameSettingsResolver.Resolve(gameEntity);
            var playerStates = await GetGameSeatStatesAsync(gameId, cancellationToken);
            ApplySeatAndControlMetadata(restoredSnapshot, gameEntity, playerStates);

            _gameEngines[gameId] = RestoreGameEngine(gameEntity, restoredSnapshot, resolvedSettings.Settings);

            var deletedEvents = events.Skip(targetIndex + 1).ToList();
            foreach (var deletedEvent in deletedEvents)
            {
                await _gamesTable.DeleteEntityAsync(deletedEvent.PartitionKey, deletedEvent.RowKey, cancellationToken: cancellationToken);
            }

            var selections = ResolveSeatSelections(gameEntity, playerStates);
            if (!selections.Any(selection => string.Equals(selection.UserId, actorUserId, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Only a player in this game can reset the timeline.");
            }

            var actingPlayerIndex = selections
                .Select((selection, index) => new { selection.UserId, Index = index })
                .FirstOrDefault(entry => string.Equals(entry.UserId, actorUserId, StringComparison.OrdinalIgnoreCase))
                ?.Index;
            var actorDisplayName = ResolveParticipantDisplayName(selections, actorUserId);
            if (string.IsNullOrWhiteSpace(actorDisplayName))
            {
                actorDisplayName = actorUserId;
            }

            var normalizedTargetDescription = string.IsNullOrWhiteSpace(targetDescription)
                ? targetEvent.ChangeSummary
                : targetDescription.Trim();
            if (string.IsNullOrWhiteSpace(normalizedTargetDescription))
            {
                normalizedTargetDescription = targetEvent.EventKind;
            }

            var cheatSummary = $"{actorDisplayName} decided to cheat and reset the game back to '{normalizedTargetDescription}'";
            var persistedCheatEvent = await PersistEventAsync(
                gameId,
                restoredSnapshot,
                "Cheat",
                cheatSummary,
                actorUserId,
                new
                {
                    TargetEventRowKey = targetEventRowKey,
                    TargetDescription = normalizedTargetDescription,
                    DeletedEventRowKeys = deletedEvents.Select(gameEvent => gameEvent.RowKey).ToArray()
                },
                cancellationToken,
                actingUserId: actorUserId,
                actingPlayerIndex: actingPlayerIndex);

            var timelineEvents = events.Take(targetIndex + 1).ToList();
            timelineEvents.Add(persistedCheatEvent);

            var finalSnapshot = await AdvanceAutomaticTurnFlowAsync(gameEntity, gameId, _gameEngines[gameId], cancellationToken);
            timelineEvents = await GetEventsOrderedAsync(gameId, cancellationToken);

            await RebuildPlayerStatisticsAsync(gameId, timelineEvents, cancellationToken);

            PublishStateChanged(
                gameId,
                finalSnapshot,
                BuildTimelineItemsForEvents(timelineEvents, resolvedSettings.Settings.AnnouncingCash),
                replaceTimeline: true);
            return true;
        }
        finally
        {
            _busyGames.TryRemove(gameId, out _);
        }
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
                    var announcingCash = _gameSettingsResolver.Resolve(gameEntity).Settings.AnnouncingCash;
                    var playerStates = await GetGameSeatStatesAsync(queuedAction.GameId, stoppingToken);

                    var snapshotBeforeAction = gameEngine.ToSnapshot();
                    ApplySeatAndControlMetadata(snapshotBeforeAction, gameEntity, playerStates);
                    await ProcessTurnAsync(gameEntity, playerStates, gameEngine, queuedAction.Action, stoppingToken);
                    var snapshot = gameEngine.ToSnapshot();
                    ApplyStatisticsDelta(gameEngine, _mapDefinition, gameEngine.Settings, snapshotBeforeAction, snapshot);
                    snapshot = gameEngine.ToSnapshot();
                    ApplySeatAndControlMetadata(snapshot, gameEntity, playerStates);

                    var persistedGameEvent = await PersistEventAsync(
                        queuedAction.GameId,
                        snapshot,
                        queuedAction.Action.Kind.ToString(),
                        DescribeAction(gameEntity, playerStates, queuedAction.Action, snapshotBeforeAction, snapshot, gameEngine),
                        queuedAction.Action.PlayerId,
                        queuedAction.Action,
                        stoppingToken);

                    PublishStateChanged(
                        queuedAction.GameId,
                        snapshot,
                        BuildLiveTimelineItems(persistedGameEvent, snapshotBeforeAction, announcingCash));

                    await AdvanceAutomaticTurnFlowAsync(gameEntity, queuedAction.GameId, gameEngine, stoppingToken);
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

                        PublishStateChanged(queuedAction.GameId, gameEngine.ToSnapshot());
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
                        PublishStateChanged(queuedAction.GameId, failedGameEngine.ToSnapshot());
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

    private async Task ProcessTurnAsync(
        GameEntity gameEntity,
        List<GameSeatState> playerStates,
        RailBaronGameEngine gameEngine,
        PlayerAction action,
        CancellationToken cancellationToken)
    {
        var activePlayer = gameEngine.CurrentTurn.ActivePlayer;
        if (action is not (BidAction or AuctionPassAction or AuctionDropOutAction)
            && !string.Equals(activePlayer.Name, action.PlayerId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Action player '{action.PlayerId}' does not match active player '{activePlayer.Name}'.");
        }

        await ValidateActionAuthorizationAsync(gameEntity, playerStates, gameEngine, action, cancellationToken);

        switch (action)
        {
            case ChooseHomeCityAction chooseHomeCityAction:
                gameEngine.ChooseHomeCity(chooseHomeCityAction.SelectedCityName);
                break;

            case ResolveHomeSwapAction resolveHomeSwapAction:
                gameEngine.ResolveHomeSwap(resolveHomeSwapAction.SwapHomeAndDestination);
                break;

            case PickDestinationAction:
                gameEngine.DrawDestination();
                break;

            case DeclareAction:
                gameEngine.Declare();
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
                    gameEngine.Settings);
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

    private Task ValidateActionAuthorizationAsync(
        GameEntity gameEntity,
        List<GameSeatState> playerStates,
        RailBaronGameEngine gameEngine,
        PlayerAction action,
        CancellationToken cancellationToken)
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

        var selections = ResolveSeatSelections(gameEntity, playerStates);
        var authorizedPlayerIndex = allowsNonActiveParticipant ? actionPlayerIndex : activePlayerIndex;
        if (authorizedPlayerIndex < 0 || authorizedPlayerIndex >= selections.Count)
        {
            throw new InvalidOperationException("Unable to resolve the acting player's roster binding.");
        }

        var slotUserId = selections[authorizedPlayerIndex].UserId;
        var activePlayerState = _botTurnService.FindActiveSeatState(playerStates, slotUserId);
        var controllerState = _gamePresenceService.ResolveSeatControllerState(gameEntity.GameId, slotUserId, activePlayerState);
        var authorized = PlayerControlRules.CanUserControlSlot(controllerState, action.ActorUserId, isPlayerActive: true)
            || PlayerControlRules.CanServerControlSlot(controllerState, action.ActorUserId, _botOptions.ServerActorUserId, isPlayerActive: true);
        if (!authorized)
        {
            throw new InvalidOperationException(allowsNonActiveParticipant
                ? "Only the controlling participant for the acting player may perform this auction action."
                : "Only the controlling participant for the active player may perform this action.");
        }

        return Task.CompletedTask;
    }

    private async Task<RailBaronGameEngine> GetOrCreateGameEngineAsync(string gameId, CancellationToken cancellationToken)
    {
        if (_mapDefinition is null)
        {
            throw new InvalidOperationException("The game map definition has not been initialized yet.");
        }

        if (_gameEngines.TryGetValue(gameId, out var inMemoryGameEngine))
        {
            return inMemoryGameEngine;
        }

        var gameEntity = await GetGameEntityAsync(gameId, cancellationToken);
        if (gameEntity is null)
        {
            _gameEngines.TryRemove(gameId, out _);
            throw new KeyNotFoundException($"Game '{gameId}' was not found and is considered deleted.");
        }

        var playerStates = await GetGameSeatStatesAsync(gameId, cancellationToken);
        var players = ResolveSeatSelections(gameEntity, playerStates)
            .Select(selection => string.IsNullOrWhiteSpace(selection.DisplayName) ? selection.UserId : selection.DisplayName)
            .ToList();
        var resolvedSettings = _gameSettingsResolver.Resolve(gameEntity);

        var initializedGameEngine = CreateGameEngine(gameEntity, players, resolvedSettings.Settings);

        var persistedEvent = await GetLatestEventAsync(gameId, cancellationToken);
        if (persistedEvent is not null && !string.IsNullOrWhiteSpace(persistedEvent.SerializedGameState))
        {
            var restoredSnapshot = GameEventSerialization.DeserializeSnapshot(persistedEvent.SerializedGameState);
            initializedGameEngine = RestoreGameEngine(gameEntity, restoredSnapshot, resolvedSettings.Settings);
        }

        var loadedGameEngine = _gameEngines.GetOrAdd(gameId, initializedGameEngine);
        if (loadedGameEngine.Settings != resolvedSettings.Settings)
        {
            throw new InvalidOperationException("Game settings are immutable and no longer match the persisted game record.");
        }

        return loadedGameEngine;
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

    private RailBaronGameEngine CreateGameEngine(GameEntity gameEntity, IReadOnlyList<string> players, Engine.Persistence.GameSettings settings)
    {
        var mapDefinition = ResolveMapDefinition(gameEntity);
        var normalizedPlayers = players
            .Where(player => !string.IsNullOrWhiteSpace(player))
            .Select(player => player.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        while (normalizedPlayers.Count < 2)
        {
            normalizedPlayers.Add($"Open Seat {normalizedPlayers.Count + 1}");
        }

        return new RailBaronGameEngine(mapDefinition, normalizedPlayers, new DefaultRandomProvider(), settings);
    }

    private RailBaronGameEngine RestoreGameEngine(GameEntity gameEntity, RailBaronGameState snapshot, Engine.Persistence.GameSettings settings)
    {
        return RailBaronGameEngine.FromSnapshot(snapshot, ResolveMapDefinition(gameEntity), new DefaultRandomProvider(), settings);
    }

    private MapDefinition ResolveMapDefinition(GameEntity gameEntity)
    {
        if (_mapDefinition is null)
        {
            throw new InvalidOperationException("The game map definition has not been initialized yet.");
        }

        return MapProbabilityService.ApplyCityProbabilities(_mapDefinition, gameEntity.CityProbabilityOverrides, gameEntity.RailroadPriceOverrides);
    }

    private async Task<GameEventEntity> PersistEventAsync(
        string gameId,
        RailBaronGameState snapshot,
        string eventKind,
        string changeSummary,
        string createdBy,
        object eventData,
        CancellationToken cancellationToken,
        string? actingUserId = null,
        int? actingPlayerIndex = null)
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
            ActingUserId = string.IsNullOrWhiteSpace(actingUserId)
                ? eventData is PlayerAction playerAction ? playerAction.ActorUserId : string.Empty
                : actingUserId,
            ActingPlayerIndex = actingPlayerIndex ?? (eventData is PlayerAction actingAction ? actingAction.PlayerIndex : null)
        };

        await _gamesTable.AddEntityAsync(entity, cancellationToken);

        return entity;
    }

    private async Task<RailBaronGameState> AdvanceAutomaticTurnFlowAsync(
        GameEntity gameEntity,
        string gameId,
        RailBaronGameEngine gameEngine,
        CancellationToken cancellationToken)
    {
        var playerStates = await GetGameSeatStatesAsync(gameId, cancellationToken);
        var originalPlayerStates = playerStates
            .Select(GameSeatStateProjection.Clone)
            .ToList();

        await _botTurnService.EnsureBotSeatControlStatesAsync(gameId, playerStates, gameEntity.CreatorId, cancellationToken);

        var snapshot = gameEngine.ToSnapshot();
        ApplySeatAndControlMetadata(snapshot, gameEntity, playerStates);
        var announcingCash = _gameSettingsResolver.Resolve(gameEntity).Settings.AnnouncingCash;

        for (var step = 0; step < AutomaticTurnFlowStepLimit; step++)
        {
            var allAiAuctionAction = await TryResolveAllAiAuctionAsync(gameEntity, playerStates, gameEngine, cancellationToken);
            if (allAiAuctionAction is not null)
            {
                var snapshotBeforeResolution = snapshot;
                snapshot = gameEngine.ToSnapshot();
                ApplySeatAndControlMetadata(snapshot, gameEntity, playerStates);
                ApplyStatisticsDelta(gameEngine, _mapDefinition, gameEngine.Settings, snapshotBeforeResolution, snapshot);
                snapshot = gameEngine.ToSnapshot();
                ApplySeatAndControlMetadata(snapshot, gameEntity, playerStates);

                var persistedGameEvent = await PersistEventAsync(
                    gameId,
                    snapshot,
                    allAiAuctionAction.Kind.ToString(),
                    DescribeAction(gameEntity, playerStates, allAiAuctionAction, snapshotBeforeResolution, snapshot, gameEngine),
                    allAiAuctionAction.PlayerId,
                    allAiAuctionAction,
                    cancellationToken);

                PublishStateChanged(gameId, snapshot, BuildLiveTimelineItems(persistedGameEvent, snapshotBeforeResolution, announcingCash));
                continue;
            }

            var automaticAction = await CreateAutomaticTurnActionAsync(gameEntity, playerStates, gameEngine, cancellationToken);
            if (automaticAction is null)
            {
                await PersistSeatStateControlChangesAsync(originalPlayerStates, playerStates, cancellationToken);
                return snapshot;
            }

            var snapshotBeforeAction = snapshot;
            if (automaticAction.BotMetadata is null && string.IsNullOrWhiteSpace(automaticAction.ActorUserId))
            {
                ProcessAutomaticTurnAction(gameEngine, automaticAction);
            }
            else
            {
                await ProcessTurnAsync(gameEntity, playerStates, gameEngine, automaticAction, cancellationToken);
            }
            snapshot = gameEngine.ToSnapshot();
            ApplySeatAndControlMetadata(snapshot, gameEntity, playerStates);
            ApplyStatisticsDelta(gameEngine, _mapDefinition, gameEngine.Settings, snapshotBeforeAction, snapshot);
            snapshot = gameEngine.ToSnapshot();
            ApplySeatAndControlMetadata(snapshot, gameEntity, playerStates);

            var persistedAutomaticGameEvent = await PersistEventAsync(
                gameId,
                snapshot,
                automaticAction.Kind.ToString(),
                DescribeAction(gameEntity, playerStates, automaticAction, snapshotBeforeAction, snapshot, gameEngine),
                automaticAction.PlayerId,
                automaticAction,
                cancellationToken);

            PublishStateChanged(gameId, snapshot, BuildLiveTimelineItems(persistedAutomaticGameEvent, snapshotBeforeAction, announcingCash));

            if (automaticAction.BotMetadata is not null && _botOptions.AutomaticActionDelayMilliseconds > 0)
            {
                await Task.Delay(_botOptions.AutomaticActionDelayMilliseconds, cancellationToken);
            }
        }

        await PersistSeatStateControlChangesAsync(originalPlayerStates, playerStates, cancellationToken);

        throw new InvalidOperationException($"Automatic turn flow did not stabilize within {AutomaticTurnFlowStepLimit} steps.");
    }

    private async Task<PlayerAction?> TryResolveAllAiAuctionAsync(
        GameEntity gameEntity,
        List<GameSeatState> playerStates,
        RailBaronGameEngine gameEngine,
        CancellationToken cancellationToken)
    {
        if (gameEngine.CurrentTurn.AuctionState is null)
        {
            return null;
        }

        if (_mapDefinition is null)
        {
            throw new InvalidOperationException("The game map definition has not been initialized yet.");
        }

        return await _botTurnService.TryResolveAllAiAuctionAsync(gameEntity.GameId, playerStates, gameEngine, _mapDefinition, cancellationToken);
    }

    private async Task<PlayerAction?> CreateAutomaticTurnActionAsync(
        GameEntity gameEntity,
        List<GameSeatState> playerStates,
        RailBaronGameEngine gameEngine,
        CancellationToken cancellationToken)
    {
        if (!HasConnectedTableUser(gameEntity.GameId, playerStates))
        {
            return null;
        }

        if (gameEngine.CurrentTurn.Phase == TurnPhase.DrawDestination)
        {
            if (gameEngine.CurrentTurn.ActivePlayer.Cash < gameEngine.Settings.WinningCash)
            {
                return CreatePickDestinationAction(gameEngine.CurrentTurn.ActivePlayer);
            }

            if (!IsAiControlledSeatTurn(gameEntity, playerStates, gameEngine))
            {
                return null;
            }

            if (_mapDefinition is null)
            {
                throw new InvalidOperationException("The game map definition has not been initialized yet.");
            }

            return ShouldAiDeclareForHome(gameEngine, _mapDefinition, gameEngine.CurrentTurn.ActivePlayer)
                ? CreateDeclareAction(gameEngine.CurrentTurn.ActivePlayer)
                : CreatePickDestinationAction(gameEngine.CurrentTurn.ActivePlayer);
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

        if (!IsAiControlledSeatTurn(gameEntity, playerStates, gameEngine))
        {
            return null;
        }

        return await _botTurnService.CreateBotActionAsync(gameEntity.GameId, playerStates, gameEngine, _mapDefinition, cancellationToken);
    }

    private bool IsAiControlledSeatTurn(GameEntity gameEntity, List<GameSeatState> playerStates, RailBaronGameEngine gameEngine)
    {
        var actingPlayerIndex = gameEngine.CurrentTurn.AuctionState?.CurrentBidderPlayerIndex ?? gameEngine.CurrentTurn.ActivePlayer.Index;
        if (actingPlayerIndex < 0 || actingPlayerIndex >= playerStates.Count)
        {
            return false;
        }

        var slotUserId = playerStates[actingPlayerIndex].PlayerUserId;
        var activePlayerState = _botTurnService.FindActiveSeatState(playerStates, slotUserId);
        var controllerState = _gamePresenceService.ResolveSeatControllerState(gameEntity.GameId, slotUserId, activePlayerState);
        return PlayerControlRules.IsAiControlledMode(controllerState.ControllerMode);
    }

    private bool HasConnectedTableUser(string gameId, IReadOnlyList<GameSeatState> playerStates)
    {
        var candidateUserIds = new List<string?>();
        foreach (var playerState in playerStates)
        {
            candidateUserIds.Add(playerState.PlayerUserId);

            var delegatedControllerUserId = _gamePresenceService.GetDelegatedControllerUserId(gameId, playerState.PlayerUserId);
            if (!string.IsNullOrWhiteSpace(delegatedControllerUserId))
            {
                candidateUserIds.Add(delegatedControllerUserId);
            }
        }

        return _gamePresenceService.HasAnyConnectedUsers(gameId, candidateUserIds);
    }

    private void OnPresenceChanged(GamePresenceChange change)
    {
        if (change.MetadataChanged
            || string.IsNullOrWhiteSpace(change.GameId)
            || !_pendingAutomaticFlowResumes.TryAdd(change.GameId, 0))
        {
            return;
        }

        _ = ResumeAutomaticTurnFlowAsync(change.GameId);
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
                if (gameEntity is null)
                {
                    return;
                }

                var playerStates = await GetGameSeatStatesAsync(gameId, CancellationToken.None);
                if (!HasConnectedTableUser(gameId, playerStates))
                {
                    return;
                }

                var gameEngine = await GetOrCreateGameEngineAsync(gameId, CancellationToken.None);
                var snapshot = await AdvanceAutomaticTurnFlowAsync(gameEntity, gameId, gameEngine, CancellationToken.None);
                PublishStateChanged(gameId, snapshot);
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

    private static Task PersistSeatStateControlChangesAsync(
        List<GameSeatState> originalPlayerStates,
        List<GameSeatState> updatedPlayerStates,
        CancellationToken cancellationToken)
    {
        _ = originalPlayerStates;
        _ = updatedPlayerStates;
        _ = cancellationToken;
        return Task.CompletedTask;
    }

    private static Task PersistSeatStateControlChangeAsync(
        GameSeatState originalPlayerState,
        GameSeatState updatedPlayerState,
        CancellationToken cancellationToken)
    {
        _ = originalPlayerState;
        _ = updatedPlayerState;
        _ = cancellationToken;
        return Task.CompletedTask;
    }

    private static TableEntity BuildSeatStateControlUpdateEntity(
        GameSeatState persistedPlayerState,
        GameSeatState updatedPlayerState)
    {
        return new TableEntity(persistedPlayerState.PartitionKey, persistedPlayerState.RowKey)
        {
            [nameof(GameSeatState.ControllerMode)] = updatedPlayerState.ControllerMode,
            [nameof(GameSeatState.ControllerUserId)] = updatedPlayerState.ControllerUserId,
            [nameof(GameSeatState.AuctionPlanTurnNumber)] = updatedPlayerState.AuctionPlanTurnNumber,
            [nameof(GameSeatState.AuctionPlanRailroadIndex)] = updatedPlayerState.AuctionPlanRailroadIndex,
            [nameof(GameSeatState.AuctionPlanStartingPrice)] = updatedPlayerState.AuctionPlanStartingPrice,
            [nameof(GameSeatState.AuctionPlanMaximumBid)] = updatedPlayerState.AuctionPlanMaximumBid,
            [nameof(GameSeatState.BotControlActivatedUtc)] = updatedPlayerState.BotControlActivatedUtc,
            [nameof(GameSeatState.BotControlClearedUtc)] = updatedPlayerState.BotControlClearedUtc,
            [nameof(GameSeatState.BotControlStatus)] = updatedPlayerState.BotControlStatus,
            [nameof(GameSeatState.BotControlClearReason)] = updatedPlayerState.BotControlClearReason
        };
    }

    private static void ApplySeatAndControlMetadata(
        RailBaronGameState snapshot,
        GameEntity gameEntity,
        List<GameSeatState> playerStates)
    {
        _ = gameEntity;

        for (var index = 0; index < snapshot.Players.Count; index++)
        {
            var playerState = index < playerStates.Count ? playerStates[index] : null;
            snapshot.Players[index].Control = GameSeatStateProjection.BuildControlState(playerState);
        }
    }

    private static IReadOnlyList<GamePlayerSelection> ResolveSeatSelections(
        GameEntity gameEntity,
        IReadOnlyList<GameSeatState> playerStates)
    {
        if (gameEntity.Seats.Count > 0)
        {
            return gameEntity.Seats
                .OrderBy(seat => seat.SeatIndex)
                .Select(seat => new GamePlayerSelection
                {
                    UserId = seat.PlayerUserId,
                    DisplayName = seat.DisplayName,
                    Color = seat.Color
                })
                .ToList();
        }

        return GameSeatStateProjection.BuildSeatSelections(playerStates);
    }

    private static void ApplyStatisticsDelta(
        RailBaronGameEngine gameEngine,
        MapDefinition? mapDefinition,
        Boxcars.Engine.Persistence.GameSettings settings,
        RailBaronGameState snapshotBeforeAction,
        RailBaronGameState snapshotAfterAction)
    {
        ApplyStatisticsDelta(
            gameEngine.Players.ToDictionary(player => player.Index),
            mapDefinition,
            settings,
            snapshotBeforeAction,
            snapshotAfterAction);
    }

    private static Task RebuildPlayerStatisticsAsync(
        string gameId,
        List<GameEventEntity> sourceEvents,
        CancellationToken cancellationToken)
    {
        _ = gameId;
        _ = sourceEvents;
        _ = cancellationToken;
        return Task.CompletedTask;
    }

    private static void ApplyStatisticsDelta(
        Dictionary<int, Player> playersBySeatIndex,
        MapDefinition? mapDefinition,
        Boxcars.Engine.Persistence.GameSettings settings,
        RailBaronGameState snapshotBeforeAction,
        RailBaronGameState snapshotAfterAction)
    {
        if (TryResolvePrimaryRoll(snapshotBeforeAction, snapshotAfterAction, out var rollPlayerIndex, out var engineType, out var rollTotal))
        {
            IncrementEngineRollStatistics(playersBySeatIndex, rollPlayerIndex, engineType, rollTotal);
        }

        if (TryResolveBonusRoll(snapshotBeforeAction, snapshotAfterAction, out var bonusPlayerIndex, out var bonusRollTotal))
        {
            IncrementBonusRollStatistics(playersBySeatIndex, bonusPlayerIndex, bonusRollTotal);
        }

        if (snapshotAfterAction.Turn.ArrivalResolution is not null
            && !AreArrivalResolutionsEquivalent(snapshotBeforeAction.Turn.ArrivalResolution, snapshotAfterAction.Turn.ArrivalResolution))
        {
            AddToPlayerStatistic(playersBySeatIndex, snapshotAfterAction.Turn.ArrivalResolution.PlayerIndex, state =>
            {
                state.TotalPayoffsCollected += Math.Max(0, snapshotAfterAction.Turn.ArrivalResolution.PayoutAmount);
            });
        }

        if (snapshotBeforeAction.Turn.Auction is null && snapshotAfterAction.Turn.Auction is not null)
        {
            AddToPlayerStatistic(playersBySeatIndex, snapshotAfterAction.Turn.Auction.SellerPlayerIndex, state =>
            {
                state.RailroadsAuctionedCount++;
            });
        }

        if (snapshotBeforeAction.Turn.Auction is not null
            && snapshotAfterAction.Turn.Auction is not null
            && snapshotAfterAction.Turn.Auction.CurrentBid > snapshotBeforeAction.Turn.Auction.CurrentBid
            && snapshotAfterAction.Turn.Auction.LastBidderPlayerIndex is int bidderPlayerIndex)
        {
            AddToPlayerStatistic(playersBySeatIndex, bidderPlayerIndex, state =>
            {
                state.AuctionBidsPlaced++;
            });
        }

        ApplyDestinationAssignmentChanges(playersBySeatIndex, mapDefinition, snapshotBeforeAction, snapshotAfterAction);

        ApplyRailroadOwnershipChanges(playersBySeatIndex, snapshotBeforeAction, snapshotAfterAction);
    }
    private static void ApplyDestinationAssignmentChanges(
        Dictionary<int, Player> playerStatesBySeatIndex,
        MapDefinition? mapDefinition,
        RailBaronGameState snapshotBeforeAction,
        RailBaronGameState snapshotAfterAction)
    {
        foreach (var destinationAssignment in ResolveAssignedDestinations(snapshotBeforeAction, snapshotAfterAction))
        {
            var isFriendlyDestination = IsFriendlyDestination(mapDefinition, snapshotAfterAction, destinationAssignment.PlayerIndex, destinationAssignment.DestinationCityName);
            var destinationLogEntry = isFriendlyDestination
                ? destinationAssignment.DestinationCityName
                : string.Concat(destinationAssignment.DestinationCityName, "*");

            AddToPlayerStatistic(playerStatesBySeatIndex, destinationAssignment.PlayerIndex, state =>
            {
                state.DestinationCount++;
                if (!isFriendlyDestination)
                {
                    state.UnfriendlyDestinationCount++;
                }

                state.DestinationLogEntries.Add(destinationLogEntry);
            });
        }
    }

    private static void ApplyRailroadOwnershipChanges(
        Dictionary<int, Player> playerStatesBySeatIndex,
        RailBaronGameState snapshotBeforeAction,
        RailBaronGameState snapshotAfterAction)
    {
        foreach (var railroadIndex in snapshotBeforeAction.RailroadOwnership.Keys
                     .Concat(snapshotAfterAction.RailroadOwnership.Keys)
                     .Distinct())
        {
            var previousOwnerIndex = snapshotBeforeAction.RailroadOwnership.GetValueOrDefault(railroadIndex);
            var nextOwnerIndex = snapshotAfterAction.RailroadOwnership.GetValueOrDefault(railroadIndex);
            if (previousOwnerIndex == nextOwnerIndex)
            {
                continue;
            }

            var railroadFaceValue = RailBaronGameEngine.GetRailroadPurchasePrice(railroadIndex);

            if (nextOwnerIndex.HasValue)
            {
                var amountPaid = ResolveRailroadPurchaseAmount(snapshotBeforeAction, railroadIndex, railroadFaceValue);
                AddToPlayerStatistic(playerStatesBySeatIndex, nextOwnerIndex.Value, state =>
                {
                    state.RailroadsPurchasedCount++;
                    state.TotalRailroadFaceValuePurchased += railroadFaceValue;
                    state.TotalRailroadAmountPaid += amountPaid;
                    if (previousOwnerIndex.HasValue)
                    {
                        state.AuctionWins++;
                    }
                });
            }
            else if (previousOwnerIndex.HasValue)
            {
                AddToPlayerStatistic(playerStatesBySeatIndex, previousOwnerIndex.Value, state =>
                {
                    state.RailroadsSoldToBankCount++;
                });
            }
        }
    }

    private static int ResolveRailroadPurchaseAmount(RailBaronGameState snapshotBeforeAction, int railroadIndex, int railroadFaceValue)
    {
        if (snapshotBeforeAction.Turn.Auction is not null
            && snapshotBeforeAction.Turn.Auction.RailroadIndex == railroadIndex
            && snapshotBeforeAction.Turn.Auction.CurrentBid > 0)
        {
            return snapshotBeforeAction.Turn.Auction.CurrentBid;
        }

        return railroadFaceValue;
    }

    private static List<AssignedDestinationStatistic> ResolveAssignedDestinations(
        RailBaronGameState snapshotBeforeAction,
        RailBaronGameState snapshotAfterAction)
    {
        var playerCount = Math.Min(snapshotBeforeAction.Players.Count, snapshotAfterAction.Players.Count);
        var assignedDestinations = new List<AssignedDestinationStatistic>(capacity: 1);

        for (var playerIndex = 0; playerIndex < playerCount; playerIndex++)
        {
            var previousDestination = snapshotBeforeAction.Players[playerIndex].DestinationCityName;
            var nextDestination = snapshotAfterAction.Players[playerIndex].DestinationCityName;

            if (string.IsNullOrWhiteSpace(nextDestination)
                || string.Equals(previousDestination, nextDestination, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            assignedDestinations.Add(new AssignedDestinationStatistic(playerIndex, nextDestination));
        }

        return assignedDestinations;
    }

    private static bool IsFriendlyDestination(
        MapDefinition? mapDefinition,
        RailBaronGameState snapshotAfterAction,
        int playerIndex,
        string destinationCityName)
    {
        if (mapDefinition is null)
        {
            return true;
        }

        var destinationCity = mapDefinition.Cities.FirstOrDefault(city =>
            string.Equals(city.Name, destinationCityName, StringComparison.OrdinalIgnoreCase));
        if (destinationCity?.MapDotIndex is not int dotIndex)
        {
            return true;
        }

        var regionIndex = mapDefinition.Regions.FirstOrDefault(region =>
            string.Equals(region.Code, destinationCity.RegionCode, StringComparison.OrdinalIgnoreCase))?.Index;
        if (!regionIndex.HasValue)
        {
            return true;
        }

        var servingRailroadIndices = mapDefinition.RailroadRouteSegments
            .Where(segment => SegmentTouchesCity(segment, regionIndex.Value, dotIndex))
            .Select(segment => segment.RailroadIndex)
            .Distinct()
            .ToList();
        if (servingRailroadIndices.Count == 0)
        {
            return false;
        }

        return servingRailroadIndices.Any(railroadIndex =>
        {
            var ownerIndex = snapshotAfterAction.RailroadOwnership.GetValueOrDefault(railroadIndex);
            return !ownerIndex.HasValue || ownerIndex.Value == playerIndex;
        });
    }

    private static bool SegmentTouchesCity(RailroadRouteSegmentDefinition segment, int regionIndex, int dotIndex)
    {
        return (segment.StartRegionIndex == regionIndex && segment.StartDotIndex == dotIndex)
            || (segment.EndRegionIndex == regionIndex && segment.EndDotIndex == dotIndex);
    }

    private static void IncrementEngineRollStatistics(Dictionary<int, Player> playerStatesBySeatIndex, int playerIndex, string engineType, int rollTotal)
    {
        AddToPlayerStatistic(playerStatesBySeatIndex, playerIndex, state =>
        {
            state.TurnsTaken++;

            if (string.Equals(engineType, LocomotiveType.Freight.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                state.FreightTurnCount++;
                state.FreightRollTotal += Math.Max(0, rollTotal);
                return;
            }

            if (string.Equals(engineType, LocomotiveType.Express.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                state.ExpressTurnCount++;
                state.ExpressRollTotal += Math.Max(0, rollTotal);
                return;
            }

            if (string.Equals(engineType, LocomotiveType.Superchief.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                state.SuperchiefTurnCount++;
                state.SuperchiefRollTotal += Math.Max(0, rollTotal);
            }
        });
    }

    private static void IncrementBonusRollStatistics(Dictionary<int, Player> playerStatesBySeatIndex, int playerIndex, int rollTotal)
    {
        AddToPlayerStatistic(playerStatesBySeatIndex, playerIndex, state =>
        {
            state.BonusRollCount++;
            state.BonusRollTotal += Math.Max(0, rollTotal);
        });
    }

    private static void AddToPlayerStatistic(Dictionary<int, Player> playerStatesBySeatIndex, int playerIndex, Action<Player> applyUpdate)
    {
        if (playerIndex < 0 || !playerStatesBySeatIndex.TryGetValue(playerIndex, out var playerState))
        {
            return;
        }

        applyUpdate(playerState);
    }

    private static HashSet<int> ResolveAffectedStatisticPlayerIndices(
        RailBaronGameState snapshotBeforeAction,
        RailBaronGameState snapshotAfterAction)
    {
        var affectedPlayerIndices = new HashSet<int>();

        if (TryResolvePrimaryRoll(snapshotBeforeAction, snapshotAfterAction, out var rollPlayerIndex, out _, out _))
        {
            affectedPlayerIndices.Add(rollPlayerIndex);
        }

        if (TryResolveBonusRoll(snapshotBeforeAction, snapshotAfterAction, out var bonusPlayerIndex, out _))
        {
            affectedPlayerIndices.Add(bonusPlayerIndex);
        }

        if (snapshotAfterAction.Turn.ArrivalResolution is not null
            && !AreArrivalResolutionsEquivalent(snapshotBeforeAction.Turn.ArrivalResolution, snapshotAfterAction.Turn.ArrivalResolution))
        {
            affectedPlayerIndices.Add(snapshotAfterAction.Turn.ArrivalResolution.PlayerIndex);
        }

        if (snapshotBeforeAction.Turn.PendingFeeAmount > 0
            && snapshotAfterAction.Turn.PendingFeeAmount == 0
            && snapshotAfterAction.Turn.ForcedSale?.EliminationTriggered != true)
        {
            affectedPlayerIndices.Add(snapshotBeforeAction.ActivePlayerIndex);

            foreach (var feeTransfer in ResolveFeeTransfers(snapshotBeforeAction, Boxcars.Engine.Persistence.GameSettings.Default))
            {
                if (feeTransfer.RecipientPlayerIndex.HasValue)
                {
                    affectedPlayerIndices.Add(feeTransfer.RecipientPlayerIndex.Value);
                }
            }
        }

        if (snapshotBeforeAction.Turn.Auction is null && snapshotAfterAction.Turn.Auction is not null)
        {
            affectedPlayerIndices.Add(snapshotAfterAction.Turn.Auction.SellerPlayerIndex);
        }

        if (snapshotBeforeAction.Turn.Auction is not null
            && snapshotAfterAction.Turn.Auction is not null
            && snapshotAfterAction.Turn.Auction.CurrentBid > snapshotBeforeAction.Turn.Auction.CurrentBid
            && snapshotAfterAction.Turn.Auction.LastBidderPlayerIndex is int bidderPlayerIndex)
        {
            affectedPlayerIndices.Add(bidderPlayerIndex);
        }

        foreach (var railroadIndex in snapshotBeforeAction.RailroadOwnership.Keys
                     .Concat(snapshotAfterAction.RailroadOwnership.Keys)
                     .Distinct())
        {
            var previousOwnerIndex = snapshotBeforeAction.RailroadOwnership.GetValueOrDefault(railroadIndex);
            var nextOwnerIndex = snapshotAfterAction.RailroadOwnership.GetValueOrDefault(railroadIndex);
            if (previousOwnerIndex == nextOwnerIndex)
            {
                continue;
            }

            if (previousOwnerIndex.HasValue)
            {
                affectedPlayerIndices.Add(previousOwnerIndex.Value);
            }

            if (nextOwnerIndex.HasValue)
            {
                affectedPlayerIndices.Add(nextOwnerIndex.Value);
            }
        }

        foreach (var destinationAssignment in ResolveAssignedDestinations(snapshotBeforeAction, snapshotAfterAction))
        {
            affectedPlayerIndices.Add(destinationAssignment.PlayerIndex);
        }

        affectedPlayerIndices.RemoveWhere(playerIndex => playerIndex < 0);
        return affectedPlayerIndices;
    }

    private static bool TryResolvePrimaryRoll(
        RailBaronGameState snapshotBeforeAction,
        RailBaronGameState snapshotAfterAction,
        out int playerIndex,
        out string engineType,
        out int rollTotal)
    {
        playerIndex = snapshotBeforeAction.ActivePlayerIndex;
        engineType = string.Empty;
        rollTotal = 0;

        var whiteDice = snapshotAfterAction.Turn.DiceResult?.WhiteDice;
        if (whiteDice is not { Length: >= 2 }
            || whiteDice.Sum() <= 0
            || AreDiceResultsEquivalent(snapshotBeforeAction.Turn.DiceResult, snapshotAfterAction.Turn.DiceResult)
            || playerIndex < 0
            || playerIndex >= snapshotBeforeAction.Players.Count)
        {
            return false;
        }

        engineType = snapshotBeforeAction.Players[playerIndex].LocomotiveType;
        rollTotal = whiteDice.Sum() + (snapshotAfterAction.Turn.DiceResult?.RedDie ?? 0);
        return true;
    }

    private static bool TryResolveBonusRoll(
        RailBaronGameState snapshotBeforeAction,
        RailBaronGameState snapshotAfterAction,
        out int playerIndex,
        out int rollTotal)
    {
        playerIndex = snapshotBeforeAction.ActivePlayerIndex;
        rollTotal = 0;

        var whiteDice = snapshotAfterAction.Turn.DiceResult?.WhiteDice;
        if (whiteDice is not { Length: >= 2 }
            || whiteDice.Any(value => value != 0)
            || snapshotAfterAction.Turn.DiceResult?.RedDie is not int redDie
            || !snapshotBeforeAction.Turn.BonusRollAvailable
            || AreDiceResultsEquivalent(snapshotBeforeAction.Turn.DiceResult, snapshotAfterAction.Turn.DiceResult))
        {
            return false;
        }

        rollTotal = redDie;
        return true;
    }

    private static bool AreArrivalResolutionsEquivalent(Boxcars.Engine.Persistence.ArrivalResolutionState? left, Boxcars.Engine.Persistence.ArrivalResolutionState? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.PlayerIndex == right.PlayerIndex
            && left.PayoutAmount == right.PayoutAmount
            && string.Equals(left.DestinationCityName, right.DestinationCityName, StringComparison.Ordinal);
    }

    private static bool AreDiceResultsEquivalent(Boxcars.Engine.Persistence.DiceResultState? left, Boxcars.Engine.Persistence.DiceResultState? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.RedDie == right.RedDie
            && left.WhiteDice.SequenceEqual(right.WhiteDice);
    }

    private static List<FeeTransferStatistic> ResolveFeeTransfers(RailBaronGameState snapshotBeforeAction, Boxcars.Engine.Persistence.GameSettings settings)
    {
        if (snapshotBeforeAction.ActivePlayerIndex < 0
            || snapshotBeforeAction.ActivePlayerIndex >= snapshotBeforeAction.Players.Count)
        {
            return [];
        }

        var rider = snapshotBeforeAction.Players[snapshotBeforeAction.ActivePlayerIndex];
        var opponentRate = RailBaronGameEngine.GetUnfriendlyFee(settings, snapshotBeforeAction.AllRailroadsSold);
        var feeBuckets = new Dictionary<int, FeeBucketStatistic>();
        var usesPublicFee = false;
        var usesPrivateFee = false;

        foreach (var railroadIndex in snapshotBeforeAction.Turn.RailroadsRiddenThisTurn)
        {
            var ownerIndex = snapshotBeforeAction.RailroadOwnership.GetValueOrDefault(railroadIndex);
            if (!ownerIndex.HasValue)
            {
                usesPublicFee = true;
                continue;
            }

            if (ownerIndex.Value == snapshotBeforeAction.ActivePlayerIndex)
            {
                usesPrivateFee = true;
                continue;
            }

            var ownerKey = ownerIndex.Value;

            if (!feeBuckets.TryGetValue(ownerKey, out var bucket))
            {
                bucket = new FeeBucketStatistic(ownerIndex, requiresFullOwnerRate: false);
                feeBuckets[ownerKey] = bucket;
            }

            if (snapshotBeforeAction.Turn.RailroadsRequiringFullOwnerRateThisTurn.Contains(railroadIndex))
            {
                bucket.RequiresFullOwnerRate = true;
            }
        }

        var transfers = feeBuckets.Values
            .Select(bucket => new FeeTransferStatistic(
                rider.Name,
                bucket.OwnerPlayerIndex,
                bucket.RequiresFullOwnerRate ? opponentRate : RailBaronGameEngine.GetPrivateFee(settings)))
            .Where(transfer => transfer.Amount > 0)
            .ToList();

        if (usesPublicFee)
        {
            transfers.Add(new FeeTransferStatistic(rider.Name, null, RailBaronGameEngine.GetPublicFee(settings)));
        }

        if (usesPrivateFee)
        {
            transfers.Add(new FeeTransferStatistic(rider.Name, null, RailBaronGameEngine.GetPrivateFee(settings)));
        }

        return transfers;
    }

    private sealed class FeeBucketStatistic
    {
        public FeeBucketStatistic(int? ownerPlayerIndex, bool requiresFullOwnerRate)
        {
            OwnerPlayerIndex = ownerPlayerIndex;
            RequiresFullOwnerRate = requiresFullOwnerRate;
        }

        public int? OwnerPlayerIndex { get; }

        public bool RequiresFullOwnerRate { get; set; }
    }

    private readonly record struct FeeTransferStatistic(string PayerPlayerName, int? RecipientPlayerIndex, int Amount);

    private readonly record struct AssignedDestinationStatistic(int PlayerIndex, string DestinationCityName);

    private static Azure.ETag ResolveIfMatchETag(GameEntity gameEntity)
    {
        return string.IsNullOrWhiteSpace(gameEntity.ETag.ToString())
            ? Azure.ETag.All
            : gameEntity.ETag;
    }

    private static Azure.ETag ResolvePlayerStateIfMatchETag(GameSeatState playerState)
    {
        return string.IsNullOrWhiteSpace(playerState.ETag.ToString())
            ? Azure.ETag.All
            : playerState.ETag;
    }

    private static bool TryGetResponseETag(Response response, out Azure.ETag etag)
    {
        if (response.Headers.TryGetValue("ETag", out var etagValue)
            && !string.IsNullOrWhiteSpace(etagValue))
        {
            etag = new Azure.ETag(etagValue);
            return true;
        }

        etag = default;
        return false;
    }

    private async Task<List<GameSeatState>> GetGameSeatStatesAsync(string gameId, CancellationToken cancellationToken)
    {
        var gameEntity = await GetGameEntityAsync(gameId, cancellationToken);
        if (gameEntity is null)
        {
            return [];
        }

        var latestEvent = await GetLatestEventAsync(gameId, cancellationToken);
        var snapshot = latestEvent is not null && !string.IsNullOrWhiteSpace(latestEvent.SerializedGameState)
            ? GameEventSerialization.DeserializeSnapshot(latestEvent.SerializedGameState)
            : null;

        return GameSeatStateProjection.BuildTransientStates(gameEntity, snapshot).ToList();
    }

    private async Task<GameSeatState?> GetGameSeatStateAsync(string gameId, int seatIndex, CancellationToken cancellationToken)
    {
        var playerStates = await GetGameSeatStatesAsync(gameId, cancellationToken);
        return playerStates.FirstOrDefault(playerState => playerState.SeatIndex == seatIndex);
    }

    private async Task<Boxcars.Engine.Persistence.GameSettings> ResolveStatisticsSettingsAsync(string gameId, CancellationToken cancellationToken)
    {
        try
        {
            var gameEntity = await GetGameEntityAsync(gameId, cancellationToken);
            return gameEntity is null
                ? Boxcars.Engine.Persistence.GameSettings.Default
                : _gameSettingsResolver.Resolve(gameEntity).Settings;
        }
        catch (NotSupportedException)
        {
            return Boxcars.Engine.Persistence.GameSettings.Default;
        }
    }

    private static bool AreBotControlColumnsEqual(GameSeatState left, GameSeatState right)
    {
        return string.Equals(left.ControllerMode, right.ControllerMode, StringComparison.Ordinal)
            && string.Equals(left.ControllerUserId, right.ControllerUserId, StringComparison.Ordinal)
            && left.AuctionPlanTurnNumber == right.AuctionPlanTurnNumber
            && left.AuctionPlanRailroadIndex == right.AuctionPlanRailroadIndex
            && left.AuctionPlanStartingPrice == right.AuctionPlanStartingPrice
            && left.AuctionPlanMaximumBid == right.AuctionPlanMaximumBid
            && left.BotControlActivatedUtc == right.BotControlActivatedUtc
            && left.BotControlClearedUtc == right.BotControlClearedUtc
            && string.Equals(left.BotControlStatus, right.BotControlStatus, StringComparison.Ordinal)
            && string.Equals(left.BotControlClearReason, right.BotControlClearReason, StringComparison.Ordinal);
    }

    private void PublishStateChanged(
        string gameId,
        RailBaronGameState state,
        IReadOnlyList<EventTimelineItem>? timelineItems = null,
        bool replaceTimeline = false)
    {
        OnStateChanged?.Invoke(gameId, new GameStateUpdate(state, timelineItems ?? [], replaceTimeline));
    }

    private static List<EventTimelineItem> BuildLiveTimelineItems(GameEventEntity gameEvent, RailBaronGameState? previousSnapshot, int announcingCash)
    {
        var previousGameEvent = previousSnapshot is null
            ? null
            : new GameEventEntity
            {
                SerializedGameState = GameEventSerialization.SerializeSnapshot(previousSnapshot)
            };

        return GameService.BuildTimelineItems(gameEvent, previousGameEvent, announcingCash);
    }

    private static List<EventTimelineItem> BuildTimelineItemsForEvents(IReadOnlyList<GameEventEntity> orderedEvents, int announcingCash)
    {
        var timelineItems = new List<EventTimelineItem>();
        GameEventEntity? previousGameEvent = null;
        foreach (var gameEvent in orderedEvents)
        {
            timelineItems.AddRange(GameService.BuildTimelineItems(gameEvent, previousGameEvent, announcingCash));
            previousGameEvent = gameEvent;
        }

        return timelineItems;
    }

    private async Task<GameEventEntity?> GetLatestEventAsync(string gameId, CancellationToken cancellationToken)
    {
        var filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {gameId} and RowKey ge {EventRowKeyPrefix} and RowKey lt {EventRowKeyExclusiveUpperBound}");
        GameEventEntity? latest = null;
        await foreach (var gameEvent in _gamesTable.QueryAsync<GameEventEntity>(
                           filter: filter,
                           cancellationToken: cancellationToken))
        {
            if (latest is null || string.CompareOrdinal(gameEvent.RowKey, latest.RowKey) > 0)
            {
                latest = gameEvent;
            }
        }

        return latest;
    }

    private async Task<List<GameEventEntity>> GetEventsOrderedAsync(string gameId, CancellationToken cancellationToken)
    {
        var filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {gameId} and RowKey ge {EventRowKeyPrefix} and RowKey lt {EventRowKeyExclusiveUpperBound}");
        var events = new List<GameEventEntity>();
        await foreach (var gameEvent in _gamesTable.QueryAsync<GameEventEntity>(
                           filter: filter,
                           cancellationToken: cancellationToken))
        {
            events.Add(gameEvent);
        }

        events.Sort(static (left, right) => string.CompareOrdinal(left.RowKey, right.RowKey));
        return events;
    }

    private string DescribeAction(
        GameEntity gameEntity,
        IReadOnlyList<GameSeatState> playerStates,
        PlayerAction action,
        RailBaronGameState snapshotBeforeAction,
        RailBaronGameState snapshotAfterAction,
        RailBaronGameEngine gameEngine)
    {
        var actorName = ResolveActorName(action, snapshotBeforeAction);

        var description = action switch
        {
            ChooseHomeCityAction chooseHomeCityAction => $"{actorName} chose {chooseHomeCityAction.SelectedCityName} as the home city",
            ResolveHomeSwapAction resolveHomeSwapAction => resolveHomeSwapAction.SwapHomeAndDestination
                ? $"{actorName} swapped the initial home and first destination"
                : $"{actorName} kept the initial home city",
            PickDestinationAction => DescribeDestinationPick(actorName, action, snapshotAfterAction),
            DeclareAction => DescribeDeclaration(actorName, action, snapshotAfterAction),
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

        return string.Concat(description, BuildActionAttributionSuffix(gameEntity, playerStates, action, snapshotBeforeAction));
    }

    private string BuildActionAttributionSuffix(
        GameEntity gameEntity,
        IReadOnlyList<GameSeatState> playerStates,
        PlayerAction action,
        RailBaronGameState snapshotBeforeAction)
    {
        if (action.BotMetadata is not null)
        {
            return BuildBotActionSuffix(action.BotMetadata);
        }

        if (string.IsNullOrWhiteSpace(action.ActorUserId))
        {
            return string.Empty;
        }

        var selections = ResolveSeatSelections(gameEntity, playerStates);
        var slotUserId = ResolveActingSlotUserId(selections, action, snapshotBeforeAction);
        if (string.IsNullOrWhiteSpace(slotUserId)
            || string.Equals(slotUserId, action.ActorUserId, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var activePlayerState = _botTurnService.FindActiveSeatState(playerStates, slotUserId);
        var controllerState = _gamePresenceService.ResolveSeatControllerState(gameEntity.GameId, slotUserId, activePlayerState);
        if (!SeatControllerModes.IsDelegated(controllerState.ControllerMode))
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
        if (metadata.IsBotPlayer)
        {
            return string.Empty;
        }

        var suffixLabel = SeatControllerModes.IsAiControlled(metadata.ControllerMode)
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

    private static string DescribeDeclaration(string actorName, PlayerAction action, RailBaronGameState snapshot)
    {
        var playerState = TryGetPlayerState(action, snapshot);
        if (playerState is null)
        {
            return $"{actorName} declared for home.";
        }

        return string.IsNullOrWhiteSpace(playerState.AlternateDestinationCityName)
            ? $"{actorName} declared for home."
            : $"{actorName} declared for home and set aside alternate destination {playerState.AlternateDestinationCityName}.";
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

        if (sellerOwnedRailroadBeforeAction && railroad.Owner is null)
        {
            return $"{GetRailroadDisplayName(railroad)} was sold to the bank for {FormatCurrency(railroad.PurchasePrice / 2)}";
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
        if (gameEngine.GameStatus != Boxcars.Engine.Domain.GameStatus.InProgress)
        {
            return null;
        }

        var activePlayer = gameEngine.CurrentTurn.ActivePlayer;

        return gameEngine.CurrentTurn.Phase switch
        {
            TurnPhase.DrawDestination => activePlayer.Cash < gameEngine.Settings.WinningCash
                ? CreatePickDestinationAction(activePlayer)
                : null,
            TurnPhase.Roll => new RollDiceAction
            {
                PlayerId = activePlayer.Name,
                PlayerIndex = activePlayer.Index,
                ActorUserId = string.Empty,
                WhiteDieOne = 0,
                WhiteDieTwo = 0
            },
            TurnPhase.EndTurn => new EndTurnAction
            {
                PlayerId = activePlayer.Name,
                PlayerIndex = activePlayer.Index,
                ActorUserId = string.Empty
            },
            _ => null
        };
    }

    private static DeclareAction CreateDeclareAction(Player activePlayer)
    {
        return new DeclareAction
        {
            PlayerId = activePlayer.Name,
            PlayerIndex = activePlayer.Index,
            ActorUserId = string.Empty
        };
    }

    private static PickDestinationAction CreatePickDestinationAction(Player activePlayer)
    {
        return new PickDestinationAction
        {
            PlayerId = activePlayer.Name,
            PlayerIndex = activePlayer.Index,
            ActorUserId = string.Empty
        };
    }

    private static bool ShouldAiDeclareForHome(RailBaronGameEngine gameEngine, MapDefinition mapDefinition, Player activePlayer)
    {
        if (activePlayer.Cash < gameEngine.Settings.WinningCash)
        {
            return false;
        }

        var currentNodeId = TryGetCityNodeId(mapDefinition, activePlayer.CurrentCity);
        if (string.IsNullOrWhiteSpace(currentNodeId))
        {
            return false;
        }

        var homeNodeId = TryGetCityNodeId(mapDefinition, activePlayer.HomeCity);
        if (string.IsNullOrWhiteSpace(homeNodeId))
        {
            return false;
        }

        var routeService = new MapRouteService();
        var routeContext = routeService.BuildContext(mapDefinition);
        var shortestRoute = routeService.FindShortestSelection(routeContext, currentNodeId, homeNodeId);
        if (shortestRoute is null || shortestRoute.Segments.Count > 15)
        {
            return false;
        }

        var cheapestRoute = routeService.FindCheapestSuggestion(
            routeContext,
            new RouteSuggestionRequest
            {
                PlayerId = activePlayer.Name,
                StartNodeId = currentNodeId,
                DestinationNodeId = homeNodeId,
                MovementType = activePlayer.LocomotiveType == LocomotiveType.Superchief ? PlayerMovementType.ThreeDie : PlayerMovementType.TwoDie,
                MovementCapacity = activePlayer.LocomotiveType == LocomotiveType.Superchief ? 3 : 2,
                PlayerColor = string.Empty,
                ResolveRailroadOwnership = railroadIndex => ResolveRailroadOwnershipCategory(gameEngine, activePlayer, railroadIndex)
            });

        return cheapestRoute.Status == RouteSuggestionStatus.Success
            && activePlayer.Cash - cheapestRoute.TotalCost >= gameEngine.Settings.WinningCash;
    }

    private static RailroadOwnershipCategory ResolveRailroadOwnershipCategory(
        RailBaronGameEngine gameEngine,
        Player activePlayer,
        int railroadIndex)
    {
        var railroad = gameEngine.Railroads.FirstOrDefault(candidate => candidate.Index == railroadIndex);
        if (railroad?.Owner is null)
        {
            return RailroadOwnershipCategory.Public;
        }

        return railroad.Owner == activePlayer
            ? RailroadOwnershipCategory.Friendly
            : RailroadOwnershipCategory.Unfriendly;
    }

    private static string? TryGetCityNodeId(MapDefinition mapDefinition, CityDefinition city)
    {
        if (!city.MapDotIndex.HasValue)
        {
            return null;
        }

        var region = mapDefinition.Regions.FirstOrDefault(candidate =>
            string.Equals(candidate.Code, city.RegionCode, StringComparison.OrdinalIgnoreCase));

        return region is null
            ? null
            : MapRouteService.NodeKey(region.Index, city.MapDotIndex.Value);
    }

    private static void ProcessAutomaticTurnAction(RailBaronGameEngine gameEngine, PlayerAction action)
    {
        switch (action)
        {
            case DeclareAction:
                gameEngine.Declare();
                break;

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
        GamePresenceService gamePresenceService)
    {
        var botOptions = Options.Create(new BotOptions());
        return new BotTurnService(
            new UserDirectoryService(tableServiceClient),
            new BotDecisionPromptBuilder(),
            new OpenAiBotClient(new NoOpHttpClientFactory(), botOptions, NullLogger<OpenAiBotClient>.Instance),
            gamePresenceService,
            new NetworkCoverageService(),
            botOptions,
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
