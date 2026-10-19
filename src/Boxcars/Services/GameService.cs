using Azure;
using Azure.Data.Tables;
using Boxcars.Data;
using Boxcars.Engine.Persistence;
using Boxcars.GameEngine;
using Boxcars.Hubs;
using Boxcars.Identity;
using Microsoft.AspNetCore.SignalR;
using System.Globalization;

namespace Boxcars.Services;

public class GameService
{
    public const string DefaultMapFileName = "U21MAP.RB3";
    private const string EventRowKeyPrefix = "Event_";
    private const string EventRowKeyExclusiveUpperBound = "Event`";

    private readonly TableClient _gamesTable;
    private readonly TableClient _usersTable;
    private readonly IHubContext<DashboardHub> _hubContext;
    private readonly IGameEngine _gameEngine;
    private readonly GameSettingsResolver _gameSettingsResolver;

    public GameService(TableServiceClient tableServiceClient, IHubContext<DashboardHub> hubContext, IGameEngine gameEngine, GameSettingsResolver gameSettingsResolver)
        : this(
            tableServiceClient.GetTableClient(TableNames.GamesTable),
            tableServiceClient.GetTableClient(TableNames.UsersTable),
            hubContext,
            gameEngine,
            gameSettingsResolver)
    {
    }

    public GameService(TableServiceClient tableServiceClient, IHubContext<DashboardHub> hubContext, IGameEngine gameEngine)
        : this(tableServiceClient, hubContext, gameEngine, new GameSettingsResolver())
    {
    }

    public GameService(TableClient gamesTable, TableClient usersTable, IHubContext<DashboardHub> hubContext, IGameEngine gameEngine, GameSettingsResolver gameSettingsResolver)
    {
        _gamesTable = gamesTable;
        _usersTable = usersTable;
        _hubContext = hubContext;
        _gameEngine = gameEngine;
        _gameSettingsResolver = gameSettingsResolver;
    }

    public GameService(TableClient gamesTable, TableClient usersTable, IHubContext<DashboardHub> hubContext, IGameEngine gameEngine)
        : this(gamesTable, usersTable, hubContext, gameEngine, new GameSettingsResolver())
    {
    }

    public async Task<DashboardState> GetDashboardStateAsync(string playerId, CancellationToken cancellationToken)
    {
        var participantGames = await GetActiveGamesForPlayerAsync(playerId, cancellationToken);

        return new DashboardState
        {
            Games = participantGames
                .OrderBy(game => ResolveStateSortOrder(game.State))
                .ThenByDescending(game => game.GameDate ?? game.CreatedAt)
                .ThenByDescending(game => game.CreatedAt)
                .Select(game => new DashboardGameSummary
                {
                    GameId = game.GameId,
                    Name = string.IsNullOrWhiteSpace(game.Name) ? BuildFallbackGameName(game) : game.Name,
                    CreatedAt = game.CreatedAt,
                    GameDate = game.GameDate,
                    State = NormalizePersistedGameState(game.State),
                    IsCreator = string.Equals(game.CreatorId, playerId, StringComparison.OrdinalIgnoreCase),
                    PlayerCount = game.Seats.Count,
                    HumanPlayerCount = game.Seats.Count(seat => !IsBotSeatUserId(seat.PlayerUserId))
                })
                .ToList()
        };
    }

    public async Task<IReadOnlyList<ApplicationUser>> GetAvailablePlayersAsync(CancellationToken cancellationToken)
    {
        var users = new List<ApplicationUser>();
        await foreach (var user in _usersTable.QueryAsync<ApplicationUser>(
                           entity => entity.PartitionKey == "USER",
                           cancellationToken: cancellationToken))
        {
            users.Add(user);
        }

        return users
            .OrderBy(user => user.Nickname)
            .ThenBy(user => user.Email)
            .ToList();
    }

    public async Task<GameActionResult> CreateGameAsync(CreateGameRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return new GameActionResult { Success = false, Reason = "Game name is required." };
        }

        if (request.Players.Count < 2)
        {
            return new GameActionResult { Success = false, Reason = "At least two player slots are required." };
        }

        GameSettings normalizedSettings;
        try
        {
            normalizedSettings = _gameSettingsResolver.Normalize(request.Settings);
        }
        catch (InvalidOperationException exception)
        {
            return new GameActionResult { Success = false, Reason = exception.Message };
        }

        var duplicateUser = request.Players
            .GroupBy(player => player.UserId, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1);

        if (duplicateUser)
        {
            return new GameActionResult { Success = false, Reason = "Each player can only be assigned once." };
        }

        var duplicateColor = request.Players
            .GroupBy(player => player.Color, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1);

        if (duplicateColor)
        {
            return new GameActionResult { Success = false, Reason = "Each color can only be assigned once." };
        }

        var gameId = Guid.NewGuid().ToString("N");

        try
        {
            var createdGameId = await _gameEngine.CreateGameAsync(request with
                {
                    CreatorUserId = request.CreatorUserId,
                    Name = request.Name.Trim(),
                    Settings = normalizedSettings
                },
                new GameCreationOptions { PreferredGameId = gameId },
                cancellationToken);

            await _hubContext.Clients.All.SendAsync("DashboardStateRefreshed", cancellationToken);

            return new GameActionResult { Success = true, GameId = createdGameId };
        }
        catch (Exception exception)
        {
            return new GameActionResult { Success = false, Reason = exception.Message };
        }
    }

    public Task<GameActionResult> JoinGameAsync(string playerId, string gameId, CancellationToken cancellationToken)
        => JoinGameCoreAsync(playerId, gameId, cancellationToken);

    public async Task<GameActionResult> StartGameAsync(string playerId, string gameId, CancellationToken cancellationToken)
    {
        var game = await GetGameAsync(gameId, cancellationToken);
        if (game is null)
        {
            return new GameActionResult { Success = false, Reason = "Game was not found." };
        }

        if (!string.Equals(game.CreatorId, playerId, StringComparison.OrdinalIgnoreCase))
        {
            return new GameActionResult { Success = false, Reason = "Only the game creator can start the game." };
        }

        if (!string.Equals(NormalizePersistedGameState(game.State), PersistedGameStates.Lobby, StringComparison.Ordinal))
        {
            return new GameActionResult { Success = false, Reason = "Game has already started." };
        }

        try
        {
            await _gameEngine.StartGameAsync(gameId, cancellationToken);
            await _hubContext.Clients.All.SendAsync("DashboardStateRefreshed", cancellationToken);
            return new GameActionResult { Success = true, GameId = gameId };
        }
        catch (Exception exception)
        {
            return new GameActionResult { Success = false, Reason = exception.Message };
        }
    }

    public async Task<GameEntity?> GetGameAsync(string gameId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _gamesTable.GetEntityAsync<GameEntity>(gameId, "GAME", cancellationToken: cancellationToken);
            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<GameSeatState>> GetGameSeatStatesAsync(string gameId, CancellationToken cancellationToken)
    {
        var game = await GetGameAsync(gameId, cancellationToken);
        if (game is null)
        {
            return [];
        }

        var snapshot = await GetLatestSnapshotAsync(gameId, cancellationToken);
        return GameSeatStateProjection.BuildStates(game, snapshot);
    }

    public async Task<GameSeatState?> GetGameSeatStateAsync(string gameId, int seatIndex, CancellationToken cancellationToken)
    {
        var seatStates = await GetGameSeatStatesAsync(gameId, cancellationToken);
        return seatStates.FirstOrDefault(seatState => seatState.SeatIndex == seatIndex);
    }

    public async Task<GameUpdateResult> UpdateSeatStatesAsync(string gameId, IReadOnlyList<GameSeatState> seatStates, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentNullException.ThrowIfNull(seatStates);

        try
        {
            var persistedGame = await GetGameAsync(gameId, cancellationToken);
            if (persistedGame is null)
            {
                return GameUpdateResult.Failed("Game is no longer active.");
            }

            var snapshot = await _gameEngine.GetCurrentStateAsync(gameId, cancellationToken);

            var proposedBySeatIndex = seatStates.ToDictionary(seatState => seatState.SeatIndex);
            foreach (var playerSnapshot in snapshot.Players.Select((player, index) => (player, index)))
            {
                if (!proposedBySeatIndex.TryGetValue(playerSnapshot.index, out var proposedPlayerState))
                {
                    continue;
                }

                playerSnapshot.player.Control = GameSeatStateProjection.CloneControl(proposedPlayerState.Control);
            }

            await PersistControlStateSnapshotAsync(
                persistedGame,
                snapshot,
                seatStates,
                createdBy: string.Empty,
                cancellationToken);

            var updatedGame = await GetGameAsync(gameId, cancellationToken);
            var updatedPlayerStates = GameSeatStateProjection.BuildStates(persistedGame, snapshot);
            if (updatedGame is null)
            {
                return GameUpdateResult.Failed("Game is no longer active.");
            }

            if (!AreImmutableGameSettingsEqual(persistedGame, updatedGame))
            {
                return GameUpdateResult.Conflict("Game settings changed during a post-start update. Refresh and try again.");
            }

            return GameUpdateResult.Success(updatedGame, updatedPlayerStates);
        }
        catch (RequestFailedException ex) when (ex.Status == 412)
        {
            return GameUpdateResult.Conflict("The game changed before the seat state change could be saved. Refresh and try again.");
        }
    }

    private static bool AreSeatStateMutableColumnsEqual(GameSeatState left, GameSeatState right)
    {
        return AreBotControlColumnsEqual(left, right);
    }

    private async Task<GameState?> GetLatestSnapshotAsync(string gameId, CancellationToken cancellationToken)
    {
        var latestEvent = await GetLatestEventAsync(gameId, cancellationToken);
        return latestEvent is null || string.IsNullOrWhiteSpace(latestEvent.SerializedGameState)
            ? null
            : GameEventSerialization.DeserializeSnapshot(latestEvent.SerializedGameState);
    }

    private async Task<GameEventEntity?> GetLatestEventAsync(string gameId, CancellationToken cancellationToken)
    {
        var latest = (GameEventEntity?)null;
        var filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {gameId} and RowKey ge {EventRowKeyPrefix} and RowKey lt {EventRowKeyExclusiveUpperBound}");

        await foreach (var gameEvent in _gamesTable.QueryAsync<GameEventEntity>(filter: filter, cancellationToken: cancellationToken))
        {
            if (latest is null || string.CompareOrdinal(gameEvent.RowKey, latest.RowKey) > 0)
            {
                latest = gameEvent;
            }
        }

        return latest;
    }

    private async Task PersistControlStateSnapshotAsync(
        GameEntity game,
        GameState snapshot,
        IReadOnlyList<GameSeatState> playerStates,
        string createdBy,
        CancellationToken cancellationToken)
    {
        _ = playerStates;

        var tick = DateTime.UtcNow.Ticks;
        var rowKey = $"Event_{tick:D20}_{Guid.NewGuid():N}";
        var entity = new GameEventEntity
        {
            PartitionKey = game.GameId,
            RowKey = rowKey,
            GameId = game.GameId,
            EventKind = "SeatControlUpdated",
            EventData = "{}",
            PreviewRouteNodeIdsJson = "[]",
            PreviewRouteSegmentKeysJson = "[]",
            SerializedGameState = GameEventSerialization.SerializeSnapshot(snapshot),
            ChangeSummary = string.Empty,
            OccurredUtc = DateTimeOffset.UtcNow,
            CreatedBy = createdBy,
            ActingUserId = createdBy,
            ActingPlayerIndex = null
        };

        await _gamesTable.AddEntityAsync(entity, cancellationToken);
    }

    private static bool AreImmutableGameSettingsEqual(GameEntity left, GameEntity right)
    {
        return left.StartingCash == right.StartingCash
            && left.AnnouncingCash == right.AnnouncingCash
            && left.WinningCash == right.WinningCash
            && left.RoverCash == right.RoverCash
            && left.PublicFee == right.PublicFee
            && left.PrivateFee == right.PrivateFee
            && left.UnfriendlyFee1 == right.UnfriendlyFee1
            && left.UnfriendlyFee2 == right.UnfriendlyFee2
            && left.HomeSwapping == right.HomeSwapping
            && left.HomeCityChoice == right.HomeCityChoice
            && left.KeepCashSecret == right.KeepCashSecret
            && string.Equals(left.StartEngine, right.StartEngine, StringComparison.Ordinal)
            && left.SuperchiefPrice == right.SuperchiefPrice
            && left.ExpressPrice == right.ExpressPrice
            && left.MinimumRailroadPrice == right.MinimumRailroadPrice
            && left.SettingsSchemaVersion == right.SettingsSchemaVersion;
    }

    private static bool AreBotControlColumnsEqual(GameSeatState left, GameSeatState right)
    {
        return string.Equals(left.Control.ControllerMode, right.Control.ControllerMode, StringComparison.Ordinal)
            && left.Control.AuctionPlanTurnNumber == right.Control.AuctionPlanTurnNumber
            && left.Control.AuctionPlanRailroadIndex == right.Control.AuctionPlanRailroadIndex
            && left.Control.AuctionPlanStartingPrice == right.Control.AuctionPlanStartingPrice
            && left.Control.AuctionPlanMaximumBid == right.Control.AuctionPlanMaximumBid
            && left.Control.BotControlActivatedUtc == right.Control.BotControlActivatedUtc
            && left.Control.BotControlClearedUtc == right.Control.BotControlClearedUtc
            && string.Equals(left.Control.BotControlStatus, right.Control.BotControlStatus, StringComparison.Ordinal)
            && string.Equals(left.Control.BotControlClearReason, right.Control.BotControlClearReason, StringComparison.Ordinal);
    }

    public async Task<IReadOnlyList<EventTimelineItem>> GetGameEventsAsync(string gameId, CancellationToken cancellationToken)
    {
        return await GetGameEventsAsync(gameId, lastSeenEventRowKey: null, cancellationToken);
    }

    public async Task<IReadOnlyList<EventTimelineItem>> GetRecentGameEventsAsync(string gameId, int maxEvents, CancellationToken cancellationToken)
    {
        if (maxEvents <= 0)
        {
            return [];
        }

        var events = await GetGameEventsAsync(gameId, lastSeenEventRowKey: null, cancellationToken);
        return events.TakeLast(maxEvents).ToList();
    }

    public async Task<IReadOnlyList<EventTimelineItem>> GetGameEventsAsync(string gameId, string? lastSeenEventRowKey, CancellationToken cancellationToken)
    {
        var game = await GetGameAsync(gameId, cancellationToken);
        var settings = game is null
            ? GameSettings.Default
            : _gameSettingsResolver.Resolve(game).Settings;
        var orderedEvents = new List<GameEventEntity>();
        var normalizedLastSeenEventRowKey = string.IsNullOrWhiteSpace(lastSeenEventRowKey)
            ? null
            : lastSeenEventRowKey.Trim();
        var inclusiveLowerRowKey = string.IsNullOrWhiteSpace(normalizedLastSeenEventRowKey)
            ? EventRowKeyPrefix
            : normalizedLastSeenEventRowKey;
        var filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {gameId} and RowKey ge {inclusiveLowerRowKey} and RowKey lt {EventRowKeyExclusiveUpperBound}");

        await foreach (var gameEvent in _gamesTable.QueryAsync<GameEventEntity>(
                           filter: filter,
                           cancellationToken: cancellationToken))
        {
            orderedEvents.Add(gameEvent);
        }

        orderedEvents.Sort(static (left, right) => string.CompareOrdinal(left.RowKey, right.RowKey));

        var events = new List<EventTimelineItem>();
        GameEventEntity? previousGameEvent = null;
        foreach (var gameEvent in orderedEvents)
        {
            events.AddRange(BuildTimelineItems(gameEvent, previousGameEvent, settings));
            previousGameEvent = gameEvent;
        }

        if (string.IsNullOrWhiteSpace(normalizedLastSeenEventRowKey))
        {
            return events;
        }

        return events
            .Where(item => !string.Equals(GetTimelineEventRowKey(item), normalizedLastSeenEventRowKey, StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>Every saved game state for the game, oldest first.</summary>
    public async Task<IReadOnlyList<GameState>> GetGameStateHistoryAsync(string gameId, CancellationToken cancellationToken)
    {
        var filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {gameId} and RowKey ge {EventRowKeyPrefix} and RowKey lt {EventRowKeyExclusiveUpperBound}");
        var events = new List<GameEventEntity>();
        await foreach (var gameEvent in _gamesTable.QueryAsync<GameEventEntity>(
                           filter: filter,
                           select: [nameof(GameEventEntity.RowKey), nameof(GameEventEntity.SerializedGameState)],
                           cancellationToken: cancellationToken))
        {
            events.Add(gameEvent);
        }

        events.Sort(static (left, right) => string.CompareOrdinal(left.RowKey, right.RowKey));
        return events
            .Where(gameEvent => !string.IsNullOrWhiteSpace(gameEvent.SerializedGameState))
            .Select(gameEvent => GameEventSerialization.DeserializeSnapshot(gameEvent.SerializedGameState))
            .ToList();
    }

    private static string GetTimelineEventRowKey(EventTimelineItem item)
    {
        var separatorIndex = item.EventId.LastIndexOf(':');
        return separatorIndex >= 0
            ? item.EventId[..separatorIndex]
            : item.EventId;
    }

    internal static List<EventTimelineItem> BuildTimelineItems(GameEventEntity gameEvent, GameEventEntity? previousGameEvent)
    {
        return BuildTimelineItems(gameEvent, previousGameEvent, GameSettings.Default);
    }

    internal static List<EventTimelineItem> BuildTimelineItems(GameEventEntity gameEvent, GameEventEntity? previousGameEvent, GameSettings settings)
    {
        if (MatchesEventKind(gameEvent.EventKind, "ChooseRoute")
            || MatchesEventKind(gameEvent.EventKind, "SeatControlUpdated"))
        {
            return [];
        }

        var snapshot = TryDeserializeSnapshot(gameEvent.SerializedGameState);
        if (snapshot is null)
        {
            return [CreateTimelineItem(gameEvent, gameEvent.RowKey, ResolveTimelineKind(gameEvent.EventKind), gameEvent.ChangeSummary)];
        }

        var previousSnapshot = TryDeserializeSnapshot(previousGameEvent?.SerializedGameState ?? string.Empty);
        var playerAction = GameEventSerialization.DeserializePlayerAction(gameEvent.EventKind, gameEvent.EventData);
        var timelineItems = new List<EventTimelineItem>();
        var actingPlayer = ResolveActingPlayer(snapshot, gameEvent.ActingPlayerIndex);
        var actingPlayerName = ResolveActingPlayerName(gameEvent, snapshot, actingPlayer);

        switch (NormalizeEventKind(gameEvent.EventKind))
        {
            case "PickDestination":
                if (string.Equals(snapshot.Turn.Phase, "RegionChoice", StringComparison.OrdinalIgnoreCase)
                    && snapshot.Turn.PendingRegionChoice is not null)
                {
                    timelineItems.Add(CreateTimelineItem(
                        gameEvent,
                        $"{gameEvent.RowKey}:destination-region-choice",
                        EventTimelineKind.NewDestination,
                            string.IsNullOrWhiteSpace(gameEvent.ChangeSummary)
                            ? $"{actingPlayerName} must choose a replacement destination region."
                                : gameEvent.ChangeSummary,
                            playerAction));
                    break;
                }

                timelineItems.Add(CreateTimelineItem(
                    gameEvent,
                    $"{gameEvent.RowKey}:destination",
                    EventTimelineKind.NewDestination,
                    string.IsNullOrWhiteSpace(actingPlayer?.DestinationCityName)
                        ? $"{actingPlayerName} has a new destination."
                        : $"{actingPlayerName} has a new destination: {actingPlayer.DestinationCityName}",
                    playerAction,
                    isPlayByPlay: true));
                break;

            case "ChooseDestinationRegion":
                timelineItems.Add(CreateTimelineItem(
                    gameEvent,
                    $"{gameEvent.RowKey}:destination",
                    EventTimelineKind.NewDestination,
                    string.IsNullOrWhiteSpace(gameEvent.ChangeSummary)
                        ? string.IsNullOrWhiteSpace(actingPlayer?.DestinationCityName)
                            ? $"{actingPlayerName} chose a replacement destination region."
                            : $"{actingPlayerName} has a new destination: {actingPlayer.DestinationCityName}"
                        : gameEvent.ChangeSummary,
                    playerAction,
                    isPlayByPlay: true));
                break;

            case "Declare":
                timelineItems.Add(CreateTimelineItem(
                    gameEvent,
                    $"{gameEvent.RowKey}:declare",
                    EventTimelineKind.Declaration,
                    string.IsNullOrWhiteSpace(gameEvent.ChangeSummary)
                        ? $"{actingPlayerName} declared for home."
                        : gameEvent.ChangeSummary,
                    playerAction,
                    isPlayByPlay: true));
                break;

            case "RollDice":
                timelineItems.Add(CreateTimelineItem(
                    gameEvent,
                    $"{gameEvent.RowKey}:roll",
                    EventTimelineKind.DiceRoll,
                    DiceRollDescriptions.DescribeRoll(actingPlayerName, snapshot.Turn.DiceResult, snapshot.Turn.BonusRollAvailable),
                    playerAction,
                    isPlayByPlay: true));
                break;

            case "Move":
                timelineItems.Add(CreateTimelineItem(
                    gameEvent,
                    $"{gameEvent.RowKey}:move",
                    EventTimelineKind.Move,
                    string.IsNullOrWhiteSpace(gameEvent.ChangeSummary)
                        ? $"{actingPlayerName} moved."
                        : gameEvent.ChangeSummary,
                    playerAction));

                if (snapshot.Turn.ArrivalResolution is null)
                {
                    break;
                }

                timelineItems.Add(CreateTimelineItem(
                    gameEvent,
                    $"{gameEvent.RowKey}:arrival",
                    EventTimelineKind.Arrival,
                        DescribeArrival(actingPlayerName, snapshot.Turn.ArrivalResolution),
                        playerAction,
                        isPlayByPlay: true));

                if (snapshot.Turn.ArrivalResolution.PurchaseOpportunityAvailable)
                {
                    timelineItems.Add(CreateTimelineItem(
                        gameEvent,
                        $"{gameEvent.RowKey}:purchase",
                        EventTimelineKind.PurchaseOpportunity,
                        $"{actingPlayerName} may buy a railroad or locomotive before ending the turn.",
                        playerAction));
                }

                break;

            case "EndTurn":
                var (payFeesDescription, payFeesIsPlayByPlay) = DescribePayFees(gameEvent, previousSnapshot);
                if (!string.IsNullOrWhiteSpace(payFeesDescription))
                {
                    timelineItems.Add(CreateTimelineItem(
                        gameEvent,
                        $"{gameEvent.RowKey}:fees",
                        EventTimelineKind.PayFees,
                        payFeesDescription,
                        playerAction,
                        isPlayByPlay: payFeesIsPlayByPlay));
                }
                break;

            case "SellRailroad":
                timelineItems.Add(CreateTimelineItem(
                    gameEvent,
                    $"{gameEvent.RowKey}:sale",
                    EventTimelineKind.PayFees,
                    string.IsNullOrWhiteSpace(gameEvent.ChangeSummary)
                        ? $"{actingPlayerName} sold a railroad to raise cash for fees."
                        : gameEvent.ChangeSummary,
                    playerAction,
                    isPlayByPlay: true));
                break;

            case "StartAuction":
                timelineItems.Add(CreateTimelineItem(
                    gameEvent,
                    $"{gameEvent.RowKey}:auction-start",
                    EventTimelineKind.PayFees,
                    string.IsNullOrWhiteSpace(gameEvent.ChangeSummary)
                        ? $"{actingPlayerName} started a railroad auction."
                        : gameEvent.ChangeSummary,
                    playerAction,
                    isPlayByPlay: true));
                break;

            case "Bid":
                timelineItems.Add(CreateTimelineItem(
                    gameEvent,
                    $"{gameEvent.RowKey}:auction-bid",
                    EventTimelineKind.PayFees,
                    string.IsNullOrWhiteSpace(gameEvent.ChangeSummary)
                        ? $"{actingPlayerName} placed an auction bid."
                        : gameEvent.ChangeSummary,
                    playerAction,
                    isPlayByPlay: true));
                break;

            case "AuctionPass":
                timelineItems.Add(CreateTimelineItem(
                    gameEvent,
                    $"{gameEvent.RowKey}:auction-pass",
                    EventTimelineKind.PayFees,
                    string.IsNullOrWhiteSpace(gameEvent.ChangeSummary)
                        ? $"{actingPlayerName} passed in the auction."
                        : gameEvent.ChangeSummary,
                    playerAction,
                    isPlayByPlay: true));
                break;

            case "AuctionDropOut":
                timelineItems.Add(CreateTimelineItem(
                    gameEvent,
                    $"{gameEvent.RowKey}:auction-dropout",
                    EventTimelineKind.PayFees,
                    string.IsNullOrWhiteSpace(gameEvent.ChangeSummary)
                        ? $"{actingPlayerName} dropped out of the auction."
                        : gameEvent.ChangeSummary,
                    playerAction,
                    isPlayByPlay: true));
                break;

            case "AuctionResolved":
                timelineItems.Add(CreateTimelineItem(
                    gameEvent,
                    $"{gameEvent.RowKey}:auction-resolved",
                    EventTimelineKind.PayFees,
                    gameEvent.ChangeSummary,
                    playerAction,
                    isPlayByPlay: true));
                break;

            case "PurchaseRailroad":
                timelineItems.Add(CreateTimelineItem(
                    gameEvent,
                    $"{gameEvent.RowKey}:purchase",
                    EventTimelineKind.Purchase,
                    string.IsNullOrWhiteSpace(gameEvent.ChangeSummary) ? "A railroad was purchased." : gameEvent.ChangeSummary,
                    playerAction,
                    isPlayByPlay: true));
                break;

            case "BuyEngine":
            case "BuySuperchief":
                timelineItems.Add(CreateTimelineItem(
                    gameEvent,
                    $"{gameEvent.RowKey}:purchase",
                    EventTimelineKind.Purchase,
                    string.IsNullOrWhiteSpace(gameEvent.ChangeSummary) ? "A locomotive was upgraded." : gameEvent.ChangeSummary,
                    playerAction,
                    isPlayByPlay: true));
                break;

            case "DeclinePurchase":
                timelineItems.Add(CreateTimelineItem(
                    gameEvent,
                    $"{gameEvent.RowKey}:decline",
                    EventTimelineKind.DeclinedPurchase,
                    string.IsNullOrWhiteSpace(gameEvent.ChangeSummary)
                        ? $"{actingPlayerName} declined the purchase opportunity"
                        : gameEvent.ChangeSummary,
                    playerAction));
                break;
        }

        var roverItems = BuildRoverTimelineItems(gameEvent, snapshot, previousSnapshot, settings.RoverCash, playerAction);
        var cashAnnouncementItems = BuildCashAnnouncementTimelineItems(gameEvent, snapshot, previousSnapshot, settings.AnnouncingCash, playerAction);

        if (timelineItems.Count == 0)
        {
            timelineItems.Add(CreateTimelineItem(
                gameEvent,
                gameEvent.RowKey,
                ResolveTimelineKind(gameEvent.EventKind),
                gameEvent.ChangeSummary,
                playerAction));
        }

        timelineItems.AddRange(roverItems);
        timelineItems.AddRange(cashAnnouncementItems);

        // The engine rolls the bonus inside a move or after a purchase decision, so it gets its own entry.
        if (DiceRollDescriptions.TryGetBonusRoll(previousSnapshot, snapshot, out var bonusRoll))
        {
            timelineItems.Add(CreateTimelineItem(
                gameEvent,
                $"{gameEvent.RowKey}:bonus-roll",
                EventTimelineKind.DiceRoll,
                DiceRollDescriptions.DescribeBonusRoll(actingPlayerName, bonusRoll, previousSnapshot!.Turn.DiceResult),
                playerAction,
                isPlayByPlay: true));
        }
        return timelineItems;
    }

    private static bool MatchesEventKind(string? eventKind, string expectedKind)
    {
        return string.Equals(NormalizeEventKind(eventKind), expectedKind, StringComparison.Ordinal);
    }

    private static string NormalizeEventKind(string? eventKind)
    {
        if (string.IsNullOrWhiteSpace(eventKind))
        {
            return string.Empty;
        }

        return eventKind.EndsWith("Action", StringComparison.Ordinal)
            ? eventKind[..^"Action".Length]
            : eventKind;
    }

    private static EventTimelineItem CreateTimelineItem(
        GameEventEntity gameEvent,
        string eventId,
        EventTimelineKind eventKind,
        string? description,
        PlayerAction? playerAction = null,
        int? actingPlayerIndexOverride = null,
        bool isPlayByPlay = false)
    {
        var moveAction = playerAction as MoveAction;
        return new EventTimelineItem
        {
            EventId = eventId,
            EventKind = eventKind,
            IsPlayByPlay = isPlayByPlay,
            Description = string.IsNullOrWhiteSpace(description)
                ? gameEvent.EventKind
                : description,
            OccurredUtc = gameEvent.OccurredUtc,
            ActingPlayerIndex = actingPlayerIndexOverride ?? gameEvent.ActingPlayerIndex,
            ActingUserId = playerAction?.ActorUserId ?? gameEvent.ActingUserId,
            IsAiAction = playerAction?.IsServerAuthoredAiAction == true,
            IsBotPlayer = playerAction?.BotMetadata?.IsBotPlayer == true,
            BotDefinitionId = playerAction?.BotMetadata?.BotDefinitionId ?? string.Empty,
            BotName = playerAction?.BotMetadata?.BotName ?? string.Empty,
            BotControllerMode = playerAction?.BotMetadata?.ControllerMode ?? string.Empty,
            BotDecisionSource = playerAction?.BotMetadata?.DecisionSource ?? string.Empty,
            BotFallbackReason = playerAction?.BotMetadata?.FallbackReason ?? string.Empty,
            MovedNodeIds = moveAction?.PointsTaken ?? [],
            MovedSegmentKeys = moveAction?.SelectedSegmentKeys ?? []
        };
    }

    private static EventTimelineKind ResolveTimelineKind(string? eventKind)
    {
        return NormalizeEventKind(eventKind) switch
        {
            "PickDestination" => EventTimelineKind.NewDestination,
            "ChooseDestinationRegion" => EventTimelineKind.NewDestination,
            "Declare" => EventTimelineKind.Declaration,
            "RollDice" => EventTimelineKind.DiceRoll,
            "Move" => EventTimelineKind.Move,
            "EndTurn" => EventTimelineKind.PayFees,
            "SellRailroad" => EventTimelineKind.PayFees,
            "StartAuction" => EventTimelineKind.PayFees,
            "Bid" => EventTimelineKind.PayFees,
            "AuctionPass" => EventTimelineKind.PayFees,
            "AuctionDropOut" => EventTimelineKind.PayFees,
            "AuctionResolved" => EventTimelineKind.PayFees,
            "PurchaseRailroad" => EventTimelineKind.Purchase,
            "BuyEngine" => EventTimelineKind.Purchase,
            "BuySuperchief" => EventTimelineKind.Purchase,
            "DeclinePurchase" => EventTimelineKind.DeclinedPurchase,
            _ => EventTimelineKind.Other
        };
    }

    private static List<EventTimelineItem> BuildCashAnnouncementTimelineItems(
        GameEventEntity gameEvent,
        GameState snapshot,
        GameState? previousSnapshot,
        int announcingCash,
        PlayerAction? playerAction)
    {
        if (previousSnapshot is null)
        {
            return [];
        }

        var threshold = Math.Max(1, announcingCash);
        var playerCount = Math.Min(snapshot.Players.Count, previousSnapshot.Players.Count);
        var timelineItems = new List<EventTimelineItem>();

        for (var playerIndex = 0; playerIndex < playerCount; playerIndex++)
        {
            var previousPlayer = previousSnapshot.Players[playerIndex];
            var currentPlayer = snapshot.Players[playerIndex];
            if (previousPlayer.Cash >= threshold || currentPlayer.Cash < threshold)
            {
                continue;
            }

            var playerName = ResolvePlayerName(snapshot, playerIndex);
            timelineItems.Add(CreateTimelineItem(
                gameEvent,
                $"{gameEvent.RowKey}:cash-announcement:{playerIndex}",
                EventTimelineKind.CashAnnouncement,
                $"Player {playerName} announces they have ${currentPlayer.Cash:N0}.",
                playerAction,
                playerIndex,
                isPlayByPlay: true));
        }

        return timelineItems;
    }

    private static List<EventTimelineItem> BuildRoverTimelineItems(
        GameEventEntity gameEvent,
        GameState snapshot,
        GameState? previousSnapshot,
        int roverCash,
        PlayerAction? playerAction)
    {
        if (previousSnapshot is null)
        {
            return [];
        }

        var playerCount = Math.Min(snapshot.Players.Count, previousSnapshot.Players.Count);
        var timelineItems = new List<EventTimelineItem>();

        for (var playerIndex = 0; playerIndex < playerCount; playerIndex++)
        {
            var previousPlayer = previousSnapshot.Players[playerIndex];
            var currentPlayer = snapshot.Players[playerIndex];
            if (!previousPlayer.HasDeclared || currentPlayer.HasDeclared)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(previousPlayer.AlternateDestinationCityName)
                && string.Equals(currentPlayer.DestinationCityName, previousPlayer.AlternateDestinationCityName, StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(currentPlayer.AlternateDestinationCityName))
            {
                var roveredPlayerName = ResolvePlayerName(snapshot, playerIndex);

                // Only the moving player rovers, and a mover who undeclares themselves (by falling below the winning
                // cash) hasn't been rovered. The amount is the game's rover cash: the cash change over the move also
                // nets in any fees the rover paid the rovered player.
                var roveringPlayerIndex = previousSnapshot.ActivePlayerIndex;
                if (roveringPlayerIndex != playerIndex && roveringPlayerIndex >= 0 && roveringPlayerIndex < playerCount)
                {
                    timelineItems.Add(CreateTimelineItem(
                        gameEvent,
                        $"{gameEvent.RowKey}:rover:{playerIndex}",
                        EventTimelineKind.PayFees,
                        $"{ResolvePlayerName(snapshot, roveringPlayerIndex)} rovered {roveredPlayerName} for ${roverCash:N0}.",
                        playerAction,
                        roveringPlayerIndex,
                        isPlayByPlay: true));
                }

                timelineItems.Add(CreateTimelineItem(
                    gameEvent,
                    $"{gameEvent.RowKey}:rover-alternate:{playerIndex}",
                    EventTimelineKind.NewDestination,
                    $"{roveredPlayerName} must go to alternate destination {currentPlayer.DestinationCityName}.",
                    playerAction,
                    playerIndex,
                    isPlayByPlay: true));
            }
        }

        return timelineItems;
    }

    private static GameState? TryDeserializeSnapshot(string serializedGameState)
    {
        if (string.IsNullOrWhiteSpace(serializedGameState))
        {
            return null;
        }

        try
        {
            return GameEventSerialization.DeserializeSnapshot(serializedGameState);
        }
        catch
        {
            return null;
        }
    }

    private static PlayerState? ResolveActingPlayer(GameState snapshot, int? actingPlayerIndex)
    {
        var playerIndex = actingPlayerIndex ?? snapshot.ActivePlayerIndex;
        return playerIndex >= 0 && playerIndex < snapshot.Players.Count
            ? snapshot.Players[playerIndex]
            : null;
    }

    /// <summary>The acting player's name placeholder (see <see cref="PlayerNameTokens"/>), or who made the event.</summary>
    private static string ResolveActingPlayerName(GameEventEntity gameEvent, GameState snapshot, PlayerState? actingPlayer)
    {
        if (actingPlayer is not null)
        {
            return PlayerNameTokens.For(snapshot.Players.IndexOf(actingPlayer));
        }

        if (!string.IsNullOrWhiteSpace(gameEvent.CreatedBy))
        {
            return gameEvent.CreatedBy;
        }

        if (!string.IsNullOrWhiteSpace(gameEvent.ActingUserId))
        {
            return gameEvent.ActingUserId;
        }

        return "Unknown player";
    }

    private static string DescribeArrival(string actingPlayerName, ArrivalResolutionState arrivalResolution)
    {
        if (arrivalResolution.PayoutAmount > 0)
        {
            return $"{actingPlayerName} reached {arrivalResolution.DestinationCityName} and collected ${arrivalResolution.PayoutAmount:N0}.";
        }

        return $"{actingPlayerName} reached {arrivalResolution.DestinationCityName}.";
    }

    /// <summary>
    /// Describes the fees paid at the end of a turn. Bankruptcies and payments to another player are play-by-play;
    /// bank-only fees are history only.
    /// </summary>
    private static (string? Description, bool IsPlayByPlay) DescribePayFees(GameEventEntity gameEvent, GameState? previousSnapshot)
    {
        if (previousSnapshot is null)
        {
            return (null, false);
        }

        var actingPlayer = ResolveActingPlayer(previousSnapshot, gameEvent.ActingPlayerIndex);
        var actingPlayerName = ResolveActingPlayerName(gameEvent, previousSnapshot, actingPlayer);

        if (previousSnapshot.Turn.ForcedSale?.EliminationTriggered == true || actingPlayer?.IsBankrupt == true)
        {
            return ($"{actingPlayerName} could not cover the required fees and is now spectating.", true);
        }

        var feeSummary = DescribeFeeSummary(previousSnapshot, gameEvent.ActingPlayerIndex, out var paidAnotherPlayer);
        return string.IsNullOrWhiteSpace(feeSummary)
            ? (null, false)
            : ($"{actingPlayerName} paid {feeSummary}.", paidAnotherPlayer);
    }

    private static string DescribeFeeSummary(GameState snapshot, int? actingPlayerIndex, out bool paidAnotherPlayer)
    {
        paidAnotherPlayer = false;
        var resolvedPlayerIndex = actingPlayerIndex ?? snapshot.ActivePlayerIndex;
        if (resolvedPlayerIndex < 0 || resolvedPlayerIndex >= snapshot.Players.Count)
        {
            return string.Empty;
        }

        var activePlayerState = snapshot.Players[resolvedPlayerIndex];
        var railroadIndices = snapshot.Turn.RailroadsRiddenThisTurn;
        if (railroadIndices.Count == 0)
        {
            return string.Empty;
        }

        var fullRateRailroads = snapshot.Turn.RailroadsRequiringFullOwnerRateThisTurn.ToHashSet();
        var usedBaseRateRailroad = false;
        var opposingOwnerRates = new Dictionary<int, bool>();

        foreach (var railroadIndex in railroadIndices)
        {
            if (!snapshot.RailroadOwnership.TryGetValue(railroadIndex, out var ownerIndex) || ownerIndex is null)
            {
                usedBaseRateRailroad = true;
                continue;
            }

            if (ownerIndex.Value == resolvedPlayerIndex)
            {
                usedBaseRateRailroad = true;
                continue;
            }

            var requiresFullOwnerRate = fullRateRailroads.Contains(railroadIndex);
            if (!opposingOwnerRates.TryGetValue(ownerIndex.Value, out var existingRequiresFullOwnerRate))
            {
                opposingOwnerRates[ownerIndex.Value] = requiresFullOwnerRate;
            }
            else
            {
                opposingOwnerRates[ownerIndex.Value] = existingRequiresFullOwnerRate || requiresFullOwnerRate;
            }
        }

        var feeParts = new List<string>();
        if (usedBaseRateRailroad)
        {
            feeParts.Add("$1,000 fees");
        }

        paidAnotherPlayer = opposingOwnerRates.Count > 0;
        var opponentRate = snapshot.AllRailroadsSold ? 10000 : 5000;
        foreach (var ownerRate in opposingOwnerRates.OrderBy(entry => entry.Key))
        {
            var amount = ownerRate.Value ? opponentRate : 1000;
            feeParts.Add($"${amount:N0} to {ResolvePlayerName(snapshot, ownerRate.Key)}");
        }

        return FormatReadableList(feeParts);
    }

    /// <summary>
    /// The player's name placeholder, filled in with their seat's current name when shown (see
    /// <see cref="PlayerNameTokens"/>).
    /// </summary>
    private static string ResolvePlayerName(GameState snapshot, int playerIndex)
    {
        return playerIndex >= 0 && playerIndex < snapshot.Players.Count
            ? PlayerNameTokens.For(playerIndex)
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

    public async Task<GameActionResult> DeleteGameAsync(string playerId, string gameId, CancellationToken cancellationToken)
    {
        var game = await GetGameAsync(gameId, cancellationToken);
        if (game is null)
        {
            return new GameActionResult { Success = false, Reason = "Game is no longer active." };
        }

        var rowsToDelete = new List<(string PartitionKey, string RowKey, ETag ETag)>();
        await foreach (var tableEntity in _gamesTable.QueryAsync<TableEntity>(
                           entity => entity.PartitionKey == gameId,
                           cancellationToken: cancellationToken))
        {
            rowsToDelete.Add((tableEntity.PartitionKey, tableEntity.RowKey, tableEntity.ETag));
        }

        foreach (var row in rowsToDelete)
        {
            await _gamesTable.DeleteEntityAsync(row.PartitionKey, row.RowKey, row.ETag, cancellationToken);
        }

        await _hubContext.Clients.All.SendAsync("DashboardStateRefreshed", cancellationToken);

        return new GameActionResult { Success = true, GameId = gameId };
    }

    private async Task<List<GameEntity>> GetAllGamesAsync(CancellationToken cancellationToken)
    {
        var games = new List<GameEntity>();

        await foreach (var game in _gamesTable.QueryAsync<GameEntity>(
                           entity => entity.RowKey == "GAME",
                           cancellationToken: cancellationToken))
        {
            game.GameId = string.IsNullOrWhiteSpace(game.GameId) ? game.PartitionKey : game.GameId;
            games.Add(game);
        }

        return games;
    }

    private async Task<HashSet<string>> GetGameIdsForPlayerAsync(string playerId, CancellationToken cancellationToken)
    {
        var gameIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in await GetGamesForPlayerAsync(playerId, cancellationToken))
        {
            gameIds.Add(game.GameId);
        }

        return gameIds;
    }

    /// <summary>
    /// Carries a player's new name and color into the seats of their games that are still going, which keep their own
    /// copy from when the player joined. A color another seat in that game already uses is left alone there. Returns
    /// the games that changed.
    /// </summary>
    public async Task<IReadOnlyList<string>> ApplyProfileToSeatsAsync(string playerId, string? displayName, string? color, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return [];
        }

        var changedGameIds = new List<string>();
        foreach (var activeGame in await GetActiveGamesForPlayerAsync(playerId, cancellationToken))
        {
            // Re-read just before saving, and try again once if someone else saved in between.
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var game = await GetGameAsync(activeGame.GameId, cancellationToken);
                if (game is null)
                {
                    break;
                }

                var seats = game.Seats.ToList();
                var changed = false;
                for (var index = 0; index < seats.Count; index++)
                {
                    var seat = seats[index];
                    if (!string.Equals(seat.PlayerUserId, playerId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var newDisplayName = string.IsNullOrWhiteSpace(displayName) ? seat.DisplayName : displayName.Trim();
                    var colorTaken = seats.Any(other => other.SeatIndex != seat.SeatIndex
                        && string.Equals(other.Color, color, StringComparison.OrdinalIgnoreCase));
                    var newColor = string.IsNullOrWhiteSpace(color) || colorTaken ? seat.Color : color;
                    if (!string.Equals(newDisplayName, seat.DisplayName, StringComparison.Ordinal)
                        || !string.Equals(newColor, seat.Color, StringComparison.Ordinal))
                    {
                        seats[index] = seat with { DisplayName = newDisplayName, Color = newColor };
                        changed = true;
                    }
                }

                if (!changed)
                {
                    break;
                }

                game.Seats = seats;
                try
                {
                    await _gamesTable.UpdateEntityAsync(game, game.ETag, TableUpdateMode.Merge, cancellationToken);
                    changedGameIds.Add(game.GameId);
                    break;
                }
                catch (RequestFailedException exception) when (exception.Status == 412)
                {
                }
            }
        }

        return changedGameIds;
    }

    private async Task<IReadOnlyList<GameEntity>> GetGamesForPlayerAsync(string playerId, CancellationToken cancellationToken)
    {
        var games = await GetAllGamesAsync(cancellationToken);
        return games
            .Where(game => game.Seats.Any(seat => string.Equals(seat.PlayerUserId, playerId, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private async Task<IReadOnlyList<GameEntity>> GetActiveGamesForPlayerAsync(string playerId, CancellationToken cancellationToken)
    {
        var participantGames = await GetGamesForPlayerAsync(playerId, cancellationToken);
        var activeGames = new List<GameEntity>(participantGames.Count);

        foreach (var game in participantGames)
        {
            if (await IsGameActiveAsync(game, cancellationToken))
            {
                activeGames.Add(game);
            }
        }

        return activeGames;
    }

    private async Task<GameActionResult> JoinGameCoreAsync(string playerId, string gameId, CancellationToken cancellationToken)
    {
        var game = await GetGameAsync(gameId, cancellationToken);
        if (game is null)
        {
            return new GameActionResult
            {
                Success = false,
                Reason = "Game is no longer available."
            };
        }

        if (!game.Seats.Any(seat => string.Equals(seat.PlayerUserId, playerId, StringComparison.OrdinalIgnoreCase)))
        {
            return new GameActionResult
            {
                Success = false,
                Reason = "You are not a participant in this game."
            };
        }

        if (!await IsGameActiveAsync(game, cancellationToken))
        {
            return new GameActionResult
            {
                Success = false,
                Reason = "This game is no longer active."
            };
        }

        return new GameActionResult
        {
            Success = true,
            GameId = game.GameId
        };
    }

    private async Task<bool> IsGameActiveAsync(GameEntity game, CancellationToken cancellationToken)
    {
        if (string.Equals(NormalizePersistedGameState(game.State), PersistedGameStates.Lobby, StringComparison.Ordinal))
        {
            return true;
        }

        var latestEvent = await GetLatestEventAsync(game.GameId, cancellationToken);
        if (latestEvent is null || string.IsNullOrWhiteSpace(latestEvent.SerializedGameState))
        {
            return true;
        }

        var latestSnapshot = GameEventSerialization.DeserializeSnapshot(latestEvent.SerializedGameState);
        if (!string.Equals(latestSnapshot.GameStatus, Boxcars.Engine.Domain.GameStatus.Completed.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Show finished games on the dashboard if they completed today
        if (latestEvent.Timestamp.HasValue && latestEvent.Timestamp.Value.Date == DateTimeOffset.UtcNow.Date)
        {
            return true;
        }

        return false;
    }

    private static string BuildFallbackGameName(GameEntity game)
    {
        var gameId = string.IsNullOrWhiteSpace(game.GameId) ? game.PartitionKey : game.GameId;
        return gameId.Length <= 8 ? $"Game {gameId}" : $"Game {gameId[..8]}";
    }

    private static int ResolveStateSortOrder(string? state)
    {
        return string.Equals(NormalizePersistedGameState(state), PersistedGameStates.Lobby, StringComparison.Ordinal)
            ? 0
            : 1;
    }

    private static string NormalizePersistedGameState(string? state)
    {
        return string.Equals(state, PersistedGameStates.Playing, StringComparison.OrdinalIgnoreCase)
            ? PersistedGameStates.Playing
            : PersistedGameStates.Lobby;
    }

    private static bool IsBotSeatUserId(string? userId)
    {
        return !string.IsNullOrWhiteSpace(userId)
            && (userId.Contains("bot", StringComparison.OrdinalIgnoreCase)
                || userId.StartsWith("ai-", StringComparison.OrdinalIgnoreCase));
    }
}

public class DashboardState
{
    public List<DashboardGameSummary> Games { get; set; } = new();
}

public class GameActionResult
{
    public bool Success { get; set; }
    public string? GameId { get; set; }
    public string? Reason { get; set; }
}

public sealed record GameUpdateResult
{
    public bool Succeeded { get; init; }
    public bool IsConflict { get; init; }
    public string? ErrorMessage { get; init; }
    public GameEntity? Game { get; init; }
    public IReadOnlyList<GameSeatState> PlayerStates { get; init; } = [];

    public static GameUpdateResult Success(GameEntity game, IReadOnlyList<GameSeatState>? playerStates = null) => new()
    {
        Succeeded = true,
        Game = game,
        PlayerStates = playerStates ?? []
    };

    public static GameUpdateResult Conflict(string message) => new()
    {
        IsConflict = true,
        ErrorMessage = message
    };

    public static GameUpdateResult Failed(string message) => new()
    {
        ErrorMessage = message
    };
}
