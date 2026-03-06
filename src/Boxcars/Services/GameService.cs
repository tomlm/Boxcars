using Azure;
using Azure.Data.Tables;
using Boxcars.Data;
using Boxcars.GameEngine;
using Boxcars.Hubs;
using Boxcars.Identity;
using Microsoft.AspNetCore.SignalR;

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

    public async Task<IReadOnlyList<EventTimelineItem>> GetGameEventsAsync(string gameId, CancellationToken cancellationToken)
    {
        var events = new List<EventTimelineItem>();

        await foreach (var gameEvent in _gamesTable.QueryAsync<GameEventEntity>(
                           entity => entity.PartitionKey == gameId,
                           cancellationToken: cancellationToken))
        {
            if (!gameEvent.RowKey.StartsWith("Event_", StringComparison.Ordinal))
            {
                continue;
            }

            events.Add(new EventTimelineItem
            {
                EventId = gameEvent.RowKey,
                EventKind = gameEvent.EventKind,
                Description = string.IsNullOrWhiteSpace(gameEvent.ChangeSummary)
                    ? gameEvent.EventKind
                    : gameEvent.ChangeSummary,
                OccurredUtc = gameEvent.OccurredUtc
            });
        }

        return events
            .OrderBy(item => item.EventId, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<GameActionResult> EndGameAsync(string playerId, string gameId, CancellationToken cancellationToken)
    {
        var game = await GetGameAsync(gameId, cancellationToken);
        if (game is null)
        {
            return new GameActionResult { Success = false, Reason = "Game is no longer active." };
        }

        if (!string.Equals(game.CreatorId, playerId, StringComparison.OrdinalIgnoreCase))
        {
            return new GameActionResult { Success = false, Reason = "Only the game creator can end this game." };
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
