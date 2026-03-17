using Azure;
using Azure.Data.Tables;
using Boxcars.Data;
using Boxcars.Engine.Persistence;
using Boxcars.GameEngine;
using Boxcars.Hubs;
using Boxcars.Identity;
using Microsoft.AspNetCore.SignalR;
using System.Text.Json;
using System.Globalization;

namespace Boxcars.Services;

public class GameService
{
    public const string DefaultMapFileName = "U21MAP.RB3";

    private readonly TableClient _gamesTable;
    private readonly TableClient _usersTable;
    private readonly IHubContext<DashboardHub> _hubContext;
    private readonly IGameEngine _gameEngine;

    public GameService(TableServiceClient tableServiceClient, IHubContext<DashboardHub> hubContext, IGameEngine gameEngine)
    {
        _gamesTable = tableServiceClient.GetTableClient(TableNames.GamesTable);
        _usersTable = tableServiceClient.GetTableClient(TableNames.UsersTable);
        _hubContext = hubContext;
        _gameEngine = gameEngine;
    }

    public async Task<DashboardState> GetDashboardStateAsync(string playerId, CancellationToken cancellationToken)
    {
        var games = await GetAllGamesAsync(cancellationToken);

        var activeGame = games
            .OrderByDescending(game => game.CreatedAt)
            .FirstOrDefault(game => IsPlayerInGame(game, playerId));

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
        if (request.Players.Count < 2)
        {
            return new GameActionResult { Success = false, Reason = "At least two player slots are required." };
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
            var createdGameId = await _gameEngine.CreateGameAsync(request with { CreatorUserId = request.CreatorUserId },
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

    public async Task<GameUpdateResult> UpdateBotAssignmentsAsync(GameEntity game, string botAssignmentsJson, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);

        try
        {
            var updateEntity = new TableEntity(game.PartitionKey, game.RowKey)
            {
                [nameof(GameEntity.BotAssignmentsJson)] = botAssignmentsJson
            };

            await _gamesTable.UpdateEntityAsync(updateEntity, game.ETag, TableUpdateMode.Merge, cancellationToken);

            var updatedGame = await GetGameAsync(game.GameId, cancellationToken);
            return updatedGame is null
                ? GameUpdateResult.Failed("Game is no longer active.")
                : GameUpdateResult.Success(updatedGame);
        }
        catch (RequestFailedException ex) when (ex.Status == 412)
        {
            return GameUpdateResult.Conflict("The game changed before the bot assignment could be saved. Refresh and try again.");
        }
    }

    public async Task<IReadOnlyList<EventTimelineItem>> GetGameEventsAsync(string gameId, CancellationToken cancellationToken)
    {
        var orderedEvents = new List<GameEventEntity>();

        await foreach (var gameEvent in _gamesTable.QueryAsync<GameEventEntity>(
                           entity => entity.PartitionKey == gameId,
                           cancellationToken: cancellationToken))
        {
            if (!gameEvent.RowKey.StartsWith("Event_", StringComparison.Ordinal))
            {
                continue;
            }

            orderedEvents.Add(gameEvent);
        }

        orderedEvents.Sort(static (left, right) => string.CompareOrdinal(left.RowKey, right.RowKey));

        var events = new List<EventTimelineItem>();
        GameEventEntity? previousGameEvent = null;
        foreach (var gameEvent in orderedEvents)
        {
            events.AddRange(BuildTimelineItems(gameEvent, previousGameEvent));
            previousGameEvent = gameEvent;
        }

        return events;
    }

    private static List<EventTimelineItem> BuildTimelineItems(GameEventEntity gameEvent, GameEventEntity? previousGameEvent)
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
                            : gameEvent.ChangeSummary));
                    break;
                }

                timelineItems.Add(CreateTimelineItem(
                    gameEvent,
                    $"{gameEvent.RowKey}:destination",
                    EventTimelineKind.NewDestination,
                    string.IsNullOrWhiteSpace(actingPlayer?.DestinationCityName)
                        ? $"{actingPlayerName} has a new destination."
                        : $"{actingPlayerName} has a new destination: {actingPlayer.DestinationCityName}"));
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
                        : gameEvent.ChangeSummary));
                break;

            case "RollDice":
                timelineItems.Add(CreateTimelineItem(
                    gameEvent,
                    $"{gameEvent.RowKey}:roll",
                    EventTimelineKind.DiceRoll,
                    $"{actingPlayerName} rolled {FormatDiceRoll(snapshot.Turn.DiceResult)}"));
                break;

            case "Move":
                timelineItems.Add(CreateTimelineItem(
                    gameEvent,
                    $"{gameEvent.RowKey}:move",
                    EventTimelineKind.Move,
                    string.IsNullOrWhiteSpace(gameEvent.ChangeSummary)
                        ? $"{actingPlayerName} moved."
                        : gameEvent.ChangeSummary));

                if (snapshot.Turn.ArrivalResolution is null)
                {
                    break;
                }

                timelineItems.Add(CreateTimelineItem(
                    gameEvent,
                    $"{gameEvent.RowKey}:arrival",
                    EventTimelineKind.Arrival,
                    DescribeArrival(actingPlayerName, snapshot.Turn.ArrivalResolution)));

                if (snapshot.Turn.ArrivalResolution.PurchaseOpportunityAvailable)
                {
                    timelineItems.Add(CreateTimelineItem(
                        gameEvent,
                        $"{gameEvent.RowKey}:purchase",
                        EventTimelineKind.PurchaseOpportunity,
                        $"{actingPlayerName} may buy a railroad or locomotive before ending the turn."));
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
                        payFeesDescription));
                }
                break;

            case "SellRailroad":
                timelineItems.Add(CreateTimelineItem(
                    gameEvent,
                    $"{gameEvent.RowKey}:sale",
                    EventTimelineKind.PayFees,
                    string.IsNullOrWhiteSpace(gameEvent.ChangeSummary)
                        ? $"{actingPlayerName} sold a railroad to raise cash for fees."
                        : gameEvent.ChangeSummary));
                break;

            case "StartAuction":
                timelineItems.Add(CreateTimelineItem(
                    gameEvent,
                    $"{gameEvent.RowKey}:auction-start",
                    EventTimelineKind.PayFees,
                    string.IsNullOrWhiteSpace(gameEvent.ChangeSummary)
                        ? $"{actingPlayerName} started a railroad auction."
                        : gameEvent.ChangeSummary));
                break;

            case "Bid":
                timelineItems.Add(CreateTimelineItem(
                    gameEvent,
                    $"{gameEvent.RowKey}:auction-bid",
                    EventTimelineKind.PayFees,
                    string.IsNullOrWhiteSpace(gameEvent.ChangeSummary)
                        ? $"{actingPlayerName} placed an auction bid."
                        : gameEvent.ChangeSummary));
                break;

            case "AuctionPass":
                timelineItems.Add(CreateTimelineItem(
                    gameEvent,
                    $"{gameEvent.RowKey}:auction-pass",
                    EventTimelineKind.PayFees,
                    string.IsNullOrWhiteSpace(gameEvent.ChangeSummary)
                        ? $"{actingPlayerName} passed in the auction."
                        : gameEvent.ChangeSummary));
                break;

            case "AuctionDropOut":
                timelineItems.Add(CreateTimelineItem(
                    gameEvent,
                    $"{gameEvent.RowKey}:auction-dropout",
                    EventTimelineKind.PayFees,
                    string.IsNullOrWhiteSpace(gameEvent.ChangeSummary)
                        ? $"{actingPlayerName} dropped out of the auction."
                        : gameEvent.ChangeSummary));
                break;

            case "PurchaseRailroad":
                timelineItems.Add(CreateTimelineItem(
                    gameEvent,
                    $"{gameEvent.RowKey}:purchase",
                    EventTimelineKind.Purchase,
                    string.IsNullOrWhiteSpace(gameEvent.ChangeSummary) ? "A railroad was purchased." : gameEvent.ChangeSummary));
                break;

            case "BuyEngine":
            case "BuySuperchief":
                timelineItems.Add(CreateTimelineItem(
                    gameEvent,
                    $"{gameEvent.RowKey}:purchase",
                    EventTimelineKind.Purchase,
                    string.IsNullOrWhiteSpace(gameEvent.ChangeSummary) ? "A locomotive was upgraded." : gameEvent.ChangeSummary));
                break;

            case "DeclinePurchase":
                timelineItems.Add(CreateTimelineItem(
                    gameEvent,
                    $"{gameEvent.RowKey}:decline",
                    EventTimelineKind.DeclinedPurchase,
                    string.IsNullOrWhiteSpace(gameEvent.ChangeSummary)
                        ? $"{actingPlayerName} declined the purchase opportunity"
                        : gameEvent.ChangeSummary));
                break;
        }

        if (timelineItems.Count == 0)
        {
            timelineItems.Add(CreateTimelineItem(
                gameEvent,
                gameEvent.RowKey,
                ResolveTimelineKind(gameEvent.EventKind),
                gameEvent.ChangeSummary));
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
        string? description)
    {
        return new EventTimelineItem
        {
            EventId = eventId,
            EventKind = eventKind,
            Description = string.IsNullOrWhiteSpace(description)
                ? gameEvent.EventKind
                : description,
            OccurredUtc = gameEvent.OccurredUtc,
            ActingPlayerIndex = gameEvent.ActingPlayerIndex
        };
    }

    private static EventTimelineKind ResolveTimelineKind(string? eventKind)
    {
        return NormalizeEventKind(eventKind) switch
        {
            "PickDestination" => EventTimelineKind.NewDestination,
            "ChooseDestinationRegion" => EventTimelineKind.NewDestination,
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

        var grandfatheredRailroads = activePlayerState.GrandfatheredRailroadIndices.ToHashSet();
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

            var requiresFullOwnerRate = !grandfatheredRailroads.Contains(railroadIndex);
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

    private static bool IsPlayerInGame(GameEntity game, string playerId)
    {
        var players = GamePlayerSelectionSerialization.Deserialize(game.PlayersJson);
        return players.Any(player => string.Equals(player.UserId, playerId, StringComparison.OrdinalIgnoreCase));
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

    public static GameUpdateResult Success(GameEntity game) => new()
    {
        Succeeded = true,
        Game = game
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
