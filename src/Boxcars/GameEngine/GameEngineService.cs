using System.Threading.Channels;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Azure.Data.Tables;
using Boxcars.Data;
using Boxcars.Engine;
using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Domain;
using Boxcars.Identity;
using RailBaronGameEngine = global::Boxcars.Engine.Domain.GameEngine;
using RailBaronGameState = global::Boxcars.Engine.Persistence.GameState;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

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
    private readonly TableClient _gameSnapshotsTable;
    private readonly ConcurrentDictionary<string, RailBaronGameEngine> _gameEngines = new(StringComparer.OrdinalIgnoreCase);
    private readonly TaskCompletionSource _mapReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private MapDefinition? _mapDefinition;

    public GameEngineService(IWebHostEnvironment webHostEnvironment, TableServiceClient tableServiceClient)
    {
        _webHostEnvironment = webHostEnvironment;
        _gameSnapshotsTable = tableServiceClient.GetTableClient(TableNames.GameSnapshotsTable);
    }

    public event Action<string, RailBaronGameState>? OnStateChanged;

    public async Task<string> CreateGameAsync(IReadOnlyList<string> players, GameCreationOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(players);

        await _mapReady.Task.WaitAsync(cancellationToken);

        var gameId = string.IsNullOrWhiteSpace(options?.PreferredGameId)
            ? Guid.NewGuid().ToString("N")
            : options.PreferredGameId;

        ValidateGameId(gameId);

        if (_mapDefinition is null)
        {
            throw new InvalidOperationException("The game map definition has not been initialized yet.");
        }

        var createdGameEngine = CreateGameEngine(players);
        if (!_gameEngines.TryAdd(gameId, createdGameEngine))
        {
            throw new InvalidOperationException($"A game with id '{gameId}' already exists.");
        }

        var snapshot = createdGameEngine.ToSnapshot();
        await PersistSnapshotAsync(gameId, snapshot, "Game created.", "CreateGame", cancellationToken);
        OnStateChanged?.Invoke(gameId, snapshot);
        return gameId;
    }

    public async Task<RailBaronGameState> GetCurrentStateAsync(string gameId, CancellationToken cancellationToken = default)
    {
        ValidateGameId(gameId);
        await _mapReady.Task.WaitAsync(cancellationToken);
        var gameEngine = await GetOrCreateGameEngineAsync(gameId, cancellationToken);
        return gameEngine.ToSnapshot();
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

        var snapshots = await GetSnapshotsOrderedAsync(gameId, cancellationToken);
        if (snapshots.Count < 2)
        {
            return false;
        }

        var previousSnapshotEntity = snapshots[^2];
        var restoredSnapshot = DeserializeSnapshot(previousSnapshotEntity.SnapshotJson);
        var restoredEngine = RestoreGameEngine(restoredSnapshot);

        _gameEngines[gameId] = restoredEngine;

        await PersistSnapshotAsync(
            gameId,
            restoredSnapshot,
            $"Undo applied. Reverted action '{snapshots[^1].ActionType}'.",
            "Undo",
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
                    ProcessTurn(gameEngine, queuedAction.Action);
                    var snapshot = gameEngine.ToSnapshot();
                    await PersistSnapshotAsync(
                        queuedAction.GameId,
                        snapshot,
                        DescribeAction(queuedAction.Action),
                        queuedAction.Action.Kind.ToString(),
                        stoppingToken);
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

    private static void ProcessTurn(RailBaronGameEngine gameEngine, PlayerAction action)
    {
        var activePlayer = gameEngine.CurrentTurn.ActivePlayer;
        if (action is not BidAction
            && !string.Equals(activePlayer.Name, action.PlayerId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Action player '{action.PlayerId}' does not match active player '{activePlayer.Name}'.");
        }

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

        var persistedSnapshotEntity = await GetLatestSnapshotAsync(gameId, cancellationToken);
        if (persistedSnapshotEntity is not null)
        {
            var restoredSnapshot = DeserializeSnapshot(persistedSnapshotEntity.SnapshotJson);
            var restoredGameEngine = RestoreGameEngine(restoredSnapshot);
            return _gameEngines.GetOrAdd(gameId, restoredGameEngine);
        }

        return _gameEngines.GetOrAdd(gameId, _ => CreateGameEngine(DefaultPlayers));
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

    private async Task PersistSnapshotAsync(
        string gameId,
        RailBaronGameState snapshot,
        string changeSummary,
        string actionType,
        CancellationToken cancellationToken)
    {
        var sequenceNumber = await GetNextSequenceNumberAsync(gameId, cancellationToken);
        var entity = new GameSnapshotEntity
        {
            PartitionKey = gameId,
            RowKey = sequenceNumber.ToString("D20", CultureInfo.InvariantCulture),
            ActionType = actionType,
            ChangeSummary = changeSummary,
            SnapshotJson = JsonSerializer.Serialize(snapshot),
            AppliedAtUtc = DateTime.UtcNow
        };

        await _gameSnapshotsTable.AddEntityAsync(entity, cancellationToken);
    }

    private async Task<long> GetNextSequenceNumberAsync(string gameId, CancellationToken cancellationToken)
    {
        var latest = await GetLatestSnapshotAsync(gameId, cancellationToken);
        if (latest is null)
        {
            return 1;
        }

        return long.TryParse(latest.RowKey, out var latestSequenceNumber)
            ? latestSequenceNumber + 1
            : 1;
    }

    private async Task<GameSnapshotEntity?> GetLatestSnapshotAsync(string gameId, CancellationToken cancellationToken)
    {
        GameSnapshotEntity? latest = null;
        await foreach (var snapshot in _gameSnapshotsTable.QueryAsync<GameSnapshotEntity>(
                           entity => entity.PartitionKey == gameId,
                           cancellationToken: cancellationToken))
        {
            if (latest is null || string.CompareOrdinal(snapshot.RowKey, latest.RowKey) > 0)
            {
                latest = snapshot;
            }
        }

        return latest;
    }

    private async Task<List<GameSnapshotEntity>> GetSnapshotsOrderedAsync(string gameId, CancellationToken cancellationToken)
    {
        var snapshots = new List<GameSnapshotEntity>();
        await foreach (var snapshot in _gameSnapshotsTable.QueryAsync<GameSnapshotEntity>(
                           entity => entity.PartitionKey == gameId,
                           cancellationToken: cancellationToken))
        {
            snapshots.Add(snapshot);
        }

        snapshots.Sort(static (left, right) => string.CompareOrdinal(left.RowKey, right.RowKey));
        return snapshots;
    }

    private static RailBaronGameState DeserializeSnapshot(string snapshotJson)
    {
        var snapshot = JsonSerializer.Deserialize<RailBaronGameState>(snapshotJson);
        return snapshot ?? throw new InvalidOperationException("Snapshot payload could not be deserialized.");
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
        var suggestedRoute = state.SuggestRoute();
        if (action.RouteNodeIds.Count > 0
            && !action.RouteNodeIds.SequenceEqual(suggestedRoute.NodeIds, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only the suggested route can be saved in this sample implementation.");
        }

        state.SaveRoute(suggestedRoute);
    }

    private readonly record struct QueuedAction(string GameId, PlayerAction Action);
}
