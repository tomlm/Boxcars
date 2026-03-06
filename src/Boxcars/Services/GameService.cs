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
    private static readonly string[] MockPlayerNames = ["Paul", "George", "Ringo", "John"];

    private readonly TableClient _gamesTable;
    private readonly TableClient _gamePlayersTable;
    private readonly TableClient _activeGameIndexTable;
    private readonly IHubContext<DashboardHub> _hubContext;
    private readonly IGameEngine _gameEngine;

    public GameService(TableServiceClient tableServiceClient, IHubContext<DashboardHub> hubContext, IGameEngine gameEngine)
    {
        _gamesTable = tableServiceClient.GetTableClient(TableNames.GamesTable);
        _gamePlayersTable = tableServiceClient.GetTableClient(TableNames.GamePlayersTable);
        _activeGameIndexTable = tableServiceClient.GetTableClient(TableNames.PlayerActiveGameIndexTable);
        _hubContext = hubContext;
        _gameEngine = gameEngine;
    }

    public async Task<DashboardState> GetDashboardStateAsync(string playerId, CancellationToken cancellationToken)
    {
        // Check if player has an active game
        var activeGameIndex = await _activeGameIndexTable.GetEntityIfExistsAsync<IndexEntity>(
            "ACTIVE_GAME",
            playerId,
            cancellationToken: cancellationToken);

        if (activeGameIndex.HasValue
            && activeGameIndex.Value is { } activeGame
            && !string.IsNullOrWhiteSpace(activeGame.GameId))
        {
            return new DashboardState
            {
                HasActiveGame = true,
                ActiveGameId = activeGame.GameId
            };
        }

        var joinableGames = new List<GameEntity>();
        var query = _gamesTable.QueryAsync<GameEntity>(
            e => e.PartitionKey == "ACTIVE",
            cancellationToken: cancellationToken);

        await foreach (var game in query)
        {
            if (game.CurrentPlayerCount < game.MaxPlayers)
            {
                joinableGames.Add(game);
            }
        }

        return new DashboardState
        {
            HasActiveGame = false,
            JoinableGames = joinableGames
        };
    }

    public async Task<GameActionResult> CreateGameAsync(string creatorId, int maxPlayers, CancellationToken cancellationToken)
    {
        // Check if player already has an active game
        var existingActiveGame = await _activeGameIndexTable.GetEntityIfExistsAsync<IndexEntity>(
            "ACTIVE_GAME",
            creatorId,
            cancellationToken: cancellationToken);

        if (existingActiveGame.HasValue)
        {
            return new GameActionResult { Success = false, Reason = "You already have an active game." };
        }

        var seededPlayerNames = new[] { creatorId }
            .Concat(MockPlayerNames)
            .ToList();

        var seededPlayerIds = new[] { creatorId }
            .Concat(MockPlayerNames.Select(name => $"mock-{name.ToLowerInvariant()}"))
            .ToList();

        var gameId = Guid.NewGuid().ToString();

        // Create game entity
        var game = new GameEntity
        {
            PartitionKey = "ACTIVE",
            RowKey = gameId,
            CreatorId = creatorId,
            MapFileName = DefaultMapFileName,
            MaxPlayers = seededPlayerNames.Count,
            CurrentPlayerCount = seededPlayerNames.Count,
            CreatedAt = DateTime.UtcNow
        };

        await _gamesTable.AddEntityAsync(game, cancellationToken);

        foreach (var playerId in seededPlayerIds)
        {
            await _gamePlayersTable.AddEntityAsync(new GamePlayerEntity
            {
                PartitionKey = gameId,
                RowKey = playerId,
                JoinedAt = DateTime.UtcNow
            }, cancellationToken);
        }

        // Add active game index for creator
        await _activeGameIndexTable.AddEntityAsync(new IndexEntity
        {
            PartitionKey = "ACTIVE_GAME",
            RowKey = creatorId,
            GameId = gameId
        }, cancellationToken);

        try
        {
            await _gameEngine.CreateGameAsync(
                seededPlayerNames,
                new GameCreationOptions { PreferredGameId = gameId },
                cancellationToken);
        }
        catch
        {
            await _activeGameIndexTable.DeleteEntityAsync("ACTIVE_GAME", creatorId, cancellationToken: cancellationToken);

            foreach (var playerId in seededPlayerIds)
            {
                await _gamePlayersTable.DeleteEntityAsync(gameId, playerId, cancellationToken: cancellationToken);
            }

            await _gamesTable.DeleteEntityAsync("ACTIVE", gameId, cancellationToken: cancellationToken);
            throw;
        }

        // Broadcast dashboard refresh
        await _hubContext.Clients.All.SendAsync("DashboardStateRefreshed", cancellationToken);

        return new GameActionResult { Success = true, GameId = gameId };
    }

    public async Task<GameActionResult> JoinGameAsync(string playerId, string gameId, CancellationToken cancellationToken)
    {
        // Check if player already has an active game
        var existingActiveGame = await _activeGameIndexTable.GetEntityIfExistsAsync<IndexEntity>(
            "ACTIVE_GAME",
            playerId,
            cancellationToken: cancellationToken);

        if (existingActiveGame.HasValue)
        {
            return new GameActionResult { Success = false, Reason = "You already have an active game." };
        }

        // Read game entity
        GameEntity game;
        try
        {
            var response = await _gamesTable.GetEntityAsync<GameEntity>("ACTIVE", gameId, cancellationToken: cancellationToken);
            game = response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            await _hubContext.Clients.User(playerId).SendAsync("JoinConflict",
                new { gameId, reason = "Game is no longer available." }, cancellationToken);
            return new GameActionResult { Success = false, Reason = "Game is no longer available." };
        }

        if (game.CurrentPlayerCount >= game.MaxPlayers)
        {
            await _hubContext.Clients.User(playerId).SendAsync("JoinConflict",
                new { gameId, reason = "Game is full." }, cancellationToken);
            return new GameActionResult { Success = false, Reason = "Game is full." };
        }

        // Insert player into game
        await _gamePlayersTable.AddEntityAsync(new GamePlayerEntity
        {
            PartitionKey = gameId,
            RowKey = playerId,
            JoinedAt = DateTime.UtcNow
        }, cancellationToken);

        // Insert active game index
        await _activeGameIndexTable.AddEntityAsync(new IndexEntity
        {
            PartitionKey = "ACTIVE_GAME",
            RowKey = playerId,
            GameId = gameId
        }, cancellationToken);

        // Increment player count with ETag concurrency
        game.CurrentPlayerCount++;
        try
        {
            await _gamesTable.UpdateEntityAsync(game, game.ETag, TableUpdateMode.Replace, cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 412)
        {
            // ETag conflict — rollback inserted rows
            try { await _gamePlayersTable.DeleteEntityAsync(gameId, playerId, cancellationToken: cancellationToken); }
            catch (RequestFailedException) { /* best-effort */ }

            try { await _activeGameIndexTable.DeleteEntityAsync("ACTIVE_GAME", playerId, cancellationToken: cancellationToken); }
            catch (RequestFailedException) { /* best-effort */ }

            await _hubContext.Clients.User(playerId).SendAsync("JoinConflict",
                new { gameId, reason = "Game state changed. Please try again." }, cancellationToken);
            return new GameActionResult { Success = false, Reason = "Game state changed. Please try again." };
        }

        // Broadcast dashboard refresh
        await _hubContext.Clients.All.SendAsync("DashboardStateRefreshed", cancellationToken);

        return new GameActionResult { Success = true, GameId = gameId };
    }

    public async Task<GameEntity?> GetGameAsync(string gameId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _gamesTable.GetEntityAsync<GameEntity>("ACTIVE", gameId, cancellationToken: cancellationToken);
            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<GameActionResult> EndGameAsync(string playerId, string gameId, CancellationToken cancellationToken)
    {
        GameEntity game;
        try
        {
            var response = await _gamesTable.GetEntityAsync<GameEntity>("ACTIVE", gameId, cancellationToken: cancellationToken);
            game = response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return new GameActionResult { Success = false, Reason = "Game is no longer active." };
        }

        if (!string.Equals(game.CreatorId, playerId, StringComparison.Ordinal))
        {
            return new GameActionResult { Success = false, Reason = "Only the game creator can end this game." };
        }

        var gamePlayers = new List<GamePlayerEntity>();
        var gamePlayersQuery = _gamePlayersTable.QueryAsync<GamePlayerEntity>(
            player => player.PartitionKey == gameId,
            cancellationToken: cancellationToken);

        await foreach (var gamePlayer in gamePlayersQuery)
        {
            gamePlayers.Add(gamePlayer);
        }

        foreach (var gamePlayer in gamePlayers)
        {
            try
            {
                await _activeGameIndexTable.DeleteEntityAsync("ACTIVE_GAME", gamePlayer.RowKey, cancellationToken: cancellationToken);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Ignore missing index rows.
            }

            try
            {
                await _gamePlayersTable.DeleteEntityAsync(gameId, gamePlayer.RowKey, cancellationToken: cancellationToken);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Ignore missing player rows.
            }
        }

        await _gamesTable.DeleteEntityAsync("ACTIVE", gameId, game.ETag, cancellationToken: cancellationToken);

        await _hubContext.Clients.All.SendAsync("DashboardStateRefreshed", cancellationToken);

        return new GameActionResult { Success = true, GameId = gameId };
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
