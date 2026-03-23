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
        var games = await GetAllGamesAsync(cancellationToken);
        var activeGameIds = await GetGameIdsForPlayerAsync(playerId, cancellationToken);

        var activeGame = games
            .OrderByDescending(game => game.CreatedAt)
            .FirstOrDefault(game => activeGameIds.Contains(game.GameId));

        return new DashboardState
        {
            HasActiveGame = activeGame is not null,
            ActiveGameId = activeGame?.GameId,
            JoinableGames = []
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
            var createdGameId = await _gameEngine.CreateGameAsync(request with { CreatorUserId = request.CreatorUserId, Settings = normalizedSettings },
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
    {
        return Task.FromResult(new GameActionResult
        {
            Success = false,
            Reason = "Joining existing games is not supported in this flow."
        });
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

    public async Task<IReadOnlyList<GamePlayerStateEntity>> GetGamePlayerStatesAsync(string gameId, CancellationToken cancellationToken)
    {
        var playerStates = new List<GamePlayerStateEntity>();
        var filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {gameId} and RowKey ge {GamePlayerStateEntity.RowKeyPrefix} and RowKey lt {GamePlayerStateEntity.RowKeyExclusiveUpperBound}");

        await foreach (var playerState in _gamesTable.QueryAsync<GamePlayerStateEntity>(
                           filter: filter,
                           cancellationToken: cancellationToken))
        {
            playerStates.Add(playerState);
        }

        playerStates.Sort(static (left, right) => left.SeatIndex.CompareTo(right.SeatIndex));
        return playerStates;
    }

    public async Task<GamePlayerStateEntity?> GetGamePlayerStateAsync(string gameId, int seatIndex, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _gamesTable.GetEntityAsync<GamePlayerStateEntity>(
                gameId,
                GamePlayerStateEntity.BuildRowKey(seatIndex),
                cancellationToken: cancellationToken);

            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<GameUpdateResult> UpdatePlayerStatesAsync(string gameId, IReadOnlyList<GamePlayerStateEntity> playerStates, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentNullException.ThrowIfNull(playerStates);

        try
        {
            var persistedGame = await GetGameAsync(gameId, cancellationToken);
            if (persistedGame is null)
            {
                return GameUpdateResult.Failed("Game is no longer active.");
            }

            var persistedPlayerStates = await GetGamePlayerStatesAsync(gameId, cancellationToken);
            if (persistedPlayerStates.Count == 0)
            {
                return GameUpdateResult.Failed("Game player state rows were not found.");
            }

            var proposedBySeatIndex = playerStates.ToDictionary(playerState => playerState.SeatIndex);
            foreach (var persistedPlayerState in persistedPlayerStates)
            {
                if (!proposedBySeatIndex.TryGetValue(persistedPlayerState.SeatIndex, out var proposedPlayerState))
                {
                    continue;
                }

                if (ArePlayerStateMutableColumnsEqual(persistedPlayerState, proposedPlayerState))
                {
                    continue;
                }

                var updateEntity = new TableEntity(persistedPlayerState.PartitionKey, persistedPlayerState.RowKey)
                {
                    [nameof(GamePlayerStateEntity.ControllerMode)] = proposedPlayerState.ControllerMode,
                    [nameof(GamePlayerStateEntity.ControllerUserId)] = proposedPlayerState.ControllerUserId,
                    [nameof(GamePlayerStateEntity.BotDefinitionId)] = proposedPlayerState.BotDefinitionId,
                    [nameof(GamePlayerStateEntity.AuctionPlanTurnNumber)] = proposedPlayerState.AuctionPlanTurnNumber,
                    [nameof(GamePlayerStateEntity.AuctionPlanRailroadIndex)] = proposedPlayerState.AuctionPlanRailroadIndex,
                    [nameof(GamePlayerStateEntity.AuctionPlanStartingPrice)] = proposedPlayerState.AuctionPlanStartingPrice,
                    [nameof(GamePlayerStateEntity.AuctionPlanMaximumBid)] = proposedPlayerState.AuctionPlanMaximumBid,
                    [nameof(GamePlayerStateEntity.BotControlActivatedUtc)] = proposedPlayerState.BotControlActivatedUtc,
                    [nameof(GamePlayerStateEntity.BotControlClearedUtc)] = proposedPlayerState.BotControlClearedUtc,
                    [nameof(GamePlayerStateEntity.BotControlStatus)] = proposedPlayerState.BotControlStatus,
                    [nameof(GamePlayerStateEntity.BotControlClearReason)] = proposedPlayerState.BotControlClearReason,
                    [nameof(GamePlayerStateEntity.TurnsTaken)] = proposedPlayerState.TurnsTaken,
                    [nameof(GamePlayerStateEntity.FreightTurnCount)] = proposedPlayerState.FreightTurnCount,
                    [nameof(GamePlayerStateEntity.FreightRollTotal)] = proposedPlayerState.FreightRollTotal,
                    [nameof(GamePlayerStateEntity.ExpressTurnCount)] = proposedPlayerState.ExpressTurnCount,
                    [nameof(GamePlayerStateEntity.ExpressRollTotal)] = proposedPlayerState.ExpressRollTotal,
                    [nameof(GamePlayerStateEntity.SuperchiefTurnCount)] = proposedPlayerState.SuperchiefTurnCount,
                    [nameof(GamePlayerStateEntity.SuperchiefRollTotal)] = proposedPlayerState.SuperchiefRollTotal,
                    [nameof(GamePlayerStateEntity.BonusRollCount)] = proposedPlayerState.BonusRollCount,
                    [nameof(GamePlayerStateEntity.BonusRollTotal)] = proposedPlayerState.BonusRollTotal,
                    [nameof(GamePlayerStateEntity.TotalPayoffsCollected)] = proposedPlayerState.TotalPayoffsCollected,
                    [nameof(GamePlayerStateEntity.TotalFeesPaid)] = proposedPlayerState.TotalFeesPaid,
                    [nameof(GamePlayerStateEntity.TotalFeesCollected)] = proposedPlayerState.TotalFeesCollected,
                    [nameof(GamePlayerStateEntity.TotalRailroadFaceValuePurchased)] = proposedPlayerState.TotalRailroadFaceValuePurchased,
                    [nameof(GamePlayerStateEntity.TotalRailroadAmountPaid)] = proposedPlayerState.TotalRailroadAmountPaid,
                    [nameof(GamePlayerStateEntity.AuctionWins)] = proposedPlayerState.AuctionWins,
                    [nameof(GamePlayerStateEntity.AuctionBidsPlaced)] = proposedPlayerState.AuctionBidsPlaced,
                    [nameof(GamePlayerStateEntity.RailroadsPurchasedCount)] = proposedPlayerState.RailroadsPurchasedCount,
                    [nameof(GamePlayerStateEntity.RailroadsAuctionedCount)] = proposedPlayerState.RailroadsAuctionedCount,
                    [nameof(GamePlayerStateEntity.RailroadsSoldToBankCount)] = proposedPlayerState.RailroadsSoldToBankCount,
                    [nameof(GamePlayerStateEntity.DestinationCount)] = proposedPlayerState.DestinationCount,
                    [nameof(GamePlayerStateEntity.UnfriendlyDestinationCount)] = proposedPlayerState.UnfriendlyDestinationCount,
                    [nameof(GamePlayerStateEntity.DestinationLog)] = proposedPlayerState.DestinationLog
                };

                await _gamesTable.UpdateEntityAsync(updateEntity, persistedPlayerState.ETag, TableUpdateMode.Merge, cancellationToken);
            }

            var updatedGame = await GetGameAsync(gameId, cancellationToken);
            var updatedPlayerStates = await GetGamePlayerStatesAsync(gameId, cancellationToken);
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

    private static bool ArePlayerStateMutableColumnsEqual(GamePlayerStateEntity left, GamePlayerStateEntity right)
    {
        return AreBotControlColumnsEqual(left, right)
            && AreStatisticsColumnsEqual(left, right);
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
            && left.SettingsSchemaVersion == right.SettingsSchemaVersion;
    }

    private static bool AreBotControlColumnsEqual(GamePlayerStateEntity left, GamePlayerStateEntity right)
    {
        return string.Equals(left.ControllerMode, right.ControllerMode, StringComparison.Ordinal)
            && string.Equals(left.ControllerUserId, right.ControllerUserId, StringComparison.Ordinal)
            && string.Equals(left.BotDefinitionId, right.BotDefinitionId, StringComparison.Ordinal)
            && left.AuctionPlanTurnNumber == right.AuctionPlanTurnNumber
            && left.AuctionPlanRailroadIndex == right.AuctionPlanRailroadIndex
            && left.AuctionPlanStartingPrice == right.AuctionPlanStartingPrice
            && left.AuctionPlanMaximumBid == right.AuctionPlanMaximumBid
            && left.BotControlActivatedUtc == right.BotControlActivatedUtc
            && left.BotControlClearedUtc == right.BotControlClearedUtc
            && string.Equals(left.BotControlStatus, right.BotControlStatus, StringComparison.Ordinal)
            && string.Equals(left.BotControlClearReason, right.BotControlClearReason, StringComparison.Ordinal);
    }

    private static bool AreStatisticsColumnsEqual(GamePlayerStateEntity left, GamePlayerStateEntity right)
    {
        return left.TurnsTaken == right.TurnsTaken
            && left.FreightTurnCount == right.FreightTurnCount
            && left.FreightRollTotal == right.FreightRollTotal
            && left.ExpressTurnCount == right.ExpressTurnCount
            && left.ExpressRollTotal == right.ExpressRollTotal
            && left.SuperchiefTurnCount == right.SuperchiefTurnCount
            && left.SuperchiefRollTotal == right.SuperchiefRollTotal
            && left.BonusRollCount == right.BonusRollCount
            && left.BonusRollTotal == right.BonusRollTotal
            && left.TotalPayoffsCollected == right.TotalPayoffsCollected
            && left.TotalFeesPaid == right.TotalFeesPaid
            && left.TotalFeesCollected == right.TotalFeesCollected
            && left.TotalRailroadFaceValuePurchased == right.TotalRailroadFaceValuePurchased
            && left.TotalRailroadAmountPaid == right.TotalRailroadAmountPaid
            && left.AuctionWins == right.AuctionWins
            && left.AuctionBidsPlaced == right.AuctionBidsPlaced
            && left.RailroadsPurchasedCount == right.RailroadsPurchasedCount
            && left.RailroadsAuctionedCount == right.RailroadsAuctionedCount
                && left.RailroadsSoldToBankCount == right.RailroadsSoldToBankCount
                && left.DestinationCount == right.DestinationCount
                && left.UnfriendlyDestinationCount == right.UnfriendlyDestinationCount
                && string.Equals(left.DestinationLog, right.DestinationLog, StringComparison.Ordinal);
    }

    public async Task<IReadOnlyList<EventTimelineItem>> GetGameEventsAsync(string gameId, CancellationToken cancellationToken)
    {
        return await GetGameEventsAsync(gameId, lastSeenEventRowKey: null, cancellationToken);
    }

    public async Task<IReadOnlyList<EventTimelineItem>> GetGameEventsAsync(string gameId, string? lastSeenEventRowKey, CancellationToken cancellationToken)
    {
        var game = await GetGameAsync(gameId, cancellationToken);
        var announcingCash = game is null
            ? GameSettings.Default.AnnouncingCash
            : _gameSettingsResolver.Resolve(game).Settings.AnnouncingCash;
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
            events.AddRange(BuildTimelineItems(gameEvent, previousGameEvent, announcingCash));
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

    private static string GetTimelineEventRowKey(EventTimelineItem item)
    {
        var separatorIndex = item.EventId.LastIndexOf(':');
        return separatorIndex >= 0
            ? item.EventId[..separatorIndex]
            : item.EventId;
    }

    internal static List<EventTimelineItem> BuildTimelineItems(GameEventEntity gameEvent, GameEventEntity? previousGameEvent)
    {
        return BuildTimelineItems(gameEvent, previousGameEvent, GameSettings.Default.AnnouncingCash);
    }

    internal static List<EventTimelineItem> BuildTimelineItems(GameEventEntity gameEvent, GameEventEntity? previousGameEvent, int announcingCash)
    {
        if (MatchesEventKind(gameEvent.EventKind, "ChooseRoute"))
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
        var actingPlayerName = ResolveActingPlayerName(gameEvent, actingPlayer);

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
                    playerAction));
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
                    playerAction));
                break;

            case "Declare":
                timelineItems.Add(CreateTimelineItem(
                    gameEvent,
                    $"{gameEvent.RowKey}:declare",
                    EventTimelineKind.NewDestination,
                    string.IsNullOrWhiteSpace(gameEvent.ChangeSummary)
                        ? $"{actingPlayerName} declared for home."
                        : gameEvent.ChangeSummary,
                    playerAction));
                break;

            case "RollDice":
                timelineItems.Add(CreateTimelineItem(
                    gameEvent,
                    $"{gameEvent.RowKey}:roll",
                    EventTimelineKind.DiceRoll,
                    $"{actingPlayerName} rolled {FormatDiceRoll(snapshot.Turn.DiceResult)}",
                    playerAction));
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
                        playerAction));

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
                var payFeesDescription = DescribePayFees(gameEvent, previousSnapshot);
                if (!string.IsNullOrWhiteSpace(payFeesDescription))
                {
                    timelineItems.Add(CreateTimelineItem(
                        gameEvent,
                        $"{gameEvent.RowKey}:fees",
                        EventTimelineKind.PayFees,
                        payFeesDescription,
                        playerAction));
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
                    playerAction));
                break;

            case "StartAuction":
                timelineItems.Add(CreateTimelineItem(
                    gameEvent,
                    $"{gameEvent.RowKey}:auction-start",
                    EventTimelineKind.PayFees,
                    string.IsNullOrWhiteSpace(gameEvent.ChangeSummary)
                        ? $"{actingPlayerName} started a railroad auction."
                        : gameEvent.ChangeSummary,
                    playerAction));
                break;

            case "Bid":
                timelineItems.Add(CreateTimelineItem(
                    gameEvent,
                    $"{gameEvent.RowKey}:auction-bid",
                    EventTimelineKind.PayFees,
                    string.IsNullOrWhiteSpace(gameEvent.ChangeSummary)
                        ? $"{actingPlayerName} placed an auction bid."
                        : gameEvent.ChangeSummary,
                    playerAction));
                break;

            case "AuctionPass":
                timelineItems.Add(CreateTimelineItem(
                    gameEvent,
                    $"{gameEvent.RowKey}:auction-pass",
                    EventTimelineKind.PayFees,
                    string.IsNullOrWhiteSpace(gameEvent.ChangeSummary)
                        ? $"{actingPlayerName} passed in the auction."
                        : gameEvent.ChangeSummary,
                    playerAction));
                break;

            case "AuctionDropOut":
                timelineItems.Add(CreateTimelineItem(
                    gameEvent,
                    $"{gameEvent.RowKey}:auction-dropout",
                    EventTimelineKind.PayFees,
                    string.IsNullOrWhiteSpace(gameEvent.ChangeSummary)
                        ? $"{actingPlayerName} dropped out of the auction."
                        : gameEvent.ChangeSummary,
                    playerAction));
                break;

            case "PurchaseRailroad":
                timelineItems.Add(CreateTimelineItem(
                    gameEvent,
                    $"{gameEvent.RowKey}:purchase",
                    EventTimelineKind.Purchase,
                    string.IsNullOrWhiteSpace(gameEvent.ChangeSummary) ? "A railroad was purchased." : gameEvent.ChangeSummary,
                    playerAction));
                break;

            case "BuyEngine":
            case "BuySuperchief":
                timelineItems.Add(CreateTimelineItem(
                    gameEvent,
                    $"{gameEvent.RowKey}:purchase",
                    EventTimelineKind.Purchase,
                    string.IsNullOrWhiteSpace(gameEvent.ChangeSummary) ? "A locomotive was upgraded." : gameEvent.ChangeSummary,
                    playerAction));
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

        var roverItems = BuildRoverTimelineItems(gameEvent, snapshot, previousSnapshot, playerAction);
        var cashAnnouncementItems = BuildCashAnnouncementTimelineItems(gameEvent, snapshot, previousSnapshot, announcingCash, playerAction);

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
        int? actingPlayerIndexOverride = null)
    {
        return new EventTimelineItem
        {
            EventId = eventId,
            EventKind = eventKind,
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
            BotFallbackReason = playerAction?.BotMetadata?.FallbackReason ?? string.Empty
        };
    }

    private static EventTimelineKind ResolveTimelineKind(string? eventKind)
    {
        return NormalizeEventKind(eventKind) switch
        {
            "PickDestination" => EventTimelineKind.NewDestination,
            "ChooseDestinationRegion" => EventTimelineKind.NewDestination,
            "Declare" => EventTimelineKind.NewDestination,
            "RollDice" => EventTimelineKind.DiceRoll,
            "Move" => EventTimelineKind.Move,
            "EndTurn" => EventTimelineKind.PayFees,
            "SellRailroad" => EventTimelineKind.PayFees,
            "StartAuction" => EventTimelineKind.PayFees,
            "Bid" => EventTimelineKind.PayFees,
            "AuctionPass" => EventTimelineKind.PayFees,
            "AuctionDropOut" => EventTimelineKind.PayFees,
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

            var playerName = !string.IsNullOrWhiteSpace(currentPlayer.Name)
                ? currentPlayer.Name
                : ResolvePlayerName(snapshot, playerIndex);
            timelineItems.Add(CreateTimelineItem(
                gameEvent,
                $"{gameEvent.RowKey}:cash-announcement:{playerIndex}",
                EventTimelineKind.CashAnnouncement,
                $"Player {playerName} announces they have ${currentPlayer.Cash:N0}.",
                playerAction,
                playerIndex));
        }

        return timelineItems;
    }

    private static List<EventTimelineItem> BuildRoverTimelineItems(
        GameEventEntity gameEvent,
        GameState snapshot,
        GameState? previousSnapshot,
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
                var roverAmount = previousPlayer.Cash - currentPlayer.Cash;
                var roveredPlayerName = !string.IsNullOrWhiteSpace(currentPlayer.Name)
                    ? currentPlayer.Name
                    : ResolvePlayerName(snapshot, playerIndex);
                var roveringPlayerIndex = -1;
                if (roverAmount > 0)
                {
                    for (var candidateIndex = 0; candidateIndex < playerCount; candidateIndex++)
                    {
                        if (candidateIndex == playerIndex)
                        {
                            continue;
                        }

                        var previousCandidate = previousSnapshot.Players[candidateIndex];
                        var currentCandidate = snapshot.Players[candidateIndex];
                        if (currentCandidate.Cash - previousCandidate.Cash == roverAmount)
                        {
                            roveringPlayerIndex = candidateIndex;
                            break;
                        }
                    }
                }

                var roveringPlayerName = roveringPlayerIndex >= 0
                    ? ResolvePlayerName(snapshot, roveringPlayerIndex)
                    : ResolveActingPlayerName(gameEvent, ResolveActingPlayer(snapshot, gameEvent.ActingPlayerIndex));

                if (roverAmount > 0)
                {
                    timelineItems.Add(CreateTimelineItem(
                        gameEvent,
                        $"{gameEvent.RowKey}:rover:{playerIndex}",
                        EventTimelineKind.PayFees,
                        $"{roveringPlayerName} rovered {roveredPlayerName} for ${roverAmount:N0}.",
                        playerAction,
                        roveringPlayerIndex >= 0 ? roveringPlayerIndex : gameEvent.ActingPlayerIndex));
                }

                timelineItems.Add(CreateTimelineItem(
                    gameEvent,
                    $"{gameEvent.RowKey}:rover-alternate:{playerIndex}",
                    EventTimelineKind.NewDestination,
                    $"{roveredPlayerName} must go to to alternate destination {currentPlayer.DestinationCityName}.",
                    playerAction,
                    playerIndex));
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

    private static string ResolveActingPlayerName(GameEventEntity gameEvent, PlayerState? actingPlayer)
    {
        if (!string.IsNullOrWhiteSpace(actingPlayer?.Name))
        {
            return actingPlayer.Name;
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

    private static string? DescribePayFees(GameEventEntity gameEvent, GameState? previousSnapshot)
    {
        if (previousSnapshot is null)
        {
            return null;
        }

        var actingPlayer = ResolveActingPlayer(previousSnapshot, gameEvent.ActingPlayerIndex);
        var actingPlayerName = ResolveActingPlayerName(gameEvent, actingPlayer);

        if (previousSnapshot.Turn.ForcedSale?.EliminationTriggered == true || actingPlayer?.IsBankrupt == true)
        {
            return $"{actingPlayerName} could not cover the required fees and is now spectating.";
        }

        var feeSummary = DescribeFeeSummary(previousSnapshot, gameEvent.ActingPlayerIndex);
        return string.IsNullOrWhiteSpace(feeSummary)
            ? null
            : $"{actingPlayerName} paid {feeSummary}.";
    }

    private static string DescribeFeeSummary(GameState snapshot, int? actingPlayerIndex)
    {
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

        var opponentRate = snapshot.AllRailroadsSold ? 10000 : 5000;
        foreach (var ownerRate in opposingOwnerRates.OrderBy(entry => entry.Key))
        {
            var amount = ownerRate.Value ? opponentRate : 1000;
            feeParts.Add($"${amount:N0} to {ResolvePlayerName(snapshot, ownerRate.Key)}");
        }

        return FormatReadableList(feeParts);
    }

    private static string ResolvePlayerName(GameState snapshot, int playerIndex)
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

    private static string FormatDiceRoll(DiceResultState? diceResult)
    {
        if (diceResult?.WhiteDice is not { Length: >= 2 })
        {
            return "0";
        }

        var whiteDiceText = string.Join("+", diceResult.WhiteDice.Select(value => value.ToString(CultureInfo.InvariantCulture)));

        return diceResult.RedDie.HasValue
            ? string.Concat(whiteDiceText, "+(", diceResult.RedDie.Value.ToString(CultureInfo.InvariantCulture), ")")
            : whiteDiceText;
    }

    public async Task<GameActionResult> EndGameAsync(string playerId, string gameId, CancellationToken cancellationToken)
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
        var filter = TableClient.CreateQueryFilter(
            $"RowKey ge {GamePlayerStateEntity.RowKeyPrefix} and RowKey lt {GamePlayerStateEntity.RowKeyExclusiveUpperBound} and PlayerUserId eq {playerId}");

        await foreach (var playerState in _gamesTable.QueryAsync<GamePlayerStateEntity>(filter: filter, cancellationToken: cancellationToken))
        {
            if (!string.IsNullOrWhiteSpace(playerState.GameId))
            {
                gameIds.Add(playerState.GameId);
            }
            else if (!string.IsNullOrWhiteSpace(playerState.PartitionKey))
            {
                gameIds.Add(playerState.PartitionKey);
            }
        }

        return gameIds;
    }
}

public class DashboardState
{
    public bool HasActiveGame { get; set; }
    public string? ActiveGameId { get; set; }
    public List<GameEntity> JoinableGames { get; set; } = new();
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
    public IReadOnlyList<GamePlayerStateEntity> PlayerStates { get; init; } = [];

    public static GameUpdateResult Success(GameEntity game, IReadOnlyList<GamePlayerStateEntity>? playerStates = null) => new()
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
