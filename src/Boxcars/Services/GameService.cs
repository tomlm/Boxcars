using Azure;
using Azure.Data.Tables;
using Boxcars.Data;
using Boxcars.GameEngine;
using Boxcars.Hubs;
using Boxcars.Identity;
using Microsoft.AspNetCore.SignalR;
using System.Text.Json;

namespace Boxcars.Services;

public class GameService
{
    public const string DefaultMapFileName = "U21MAP.RB3";
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly TableClient _gamesTable;
    private readonly TableClient _activeGameIndexTable;
    private readonly TableClient _usersTable;
    private readonly IHubContext<DashboardHub> _hubContext;
    private readonly IGameEngine _gameEngine;

    public GameService(TableServiceClient tableServiceClient, IHubContext<DashboardHub> hubContext, IGameEngine gameEngine)
    {
        _gamesTable = tableServiceClient.GetTableClient(TableNames.GamesTable);
        _activeGameIndexTable = tableServiceClient.GetTableClient(TableNames.PlayerActiveGameIndexTable);
        _usersTable = tableServiceClient.GetTableClient(TableNames.UsersTable);
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

    public async Task<GameActionResult> CreateGameAsync(string creatorId, IReadOnlyList<GameCreationPlayer> selectedPlayers, CancellationToken cancellationToken)
    {
        if (selectedPlayers.Count is < 2 or > 6)
        {
            return new GameActionResult { Success = false, Reason = "Games must include between 2 and 6 players." };
        }

        var distinctUserIds = selectedPlayers
            .Select(player => player.UserId)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (distinctUserIds.Count != selectedPlayers.Count)
        {
            return new GameActionResult { Success = false, Reason = "Each player must be selected only once." };
        }

        if (!distinctUserIds.Contains(creatorId, StringComparer.Ordinal))
        {
            return new GameActionResult { Success = false, Reason = "The game creator must be included in the player list." };
        }

        var resolvedPlayers = new List<ResolvedGamePlayer>(selectedPlayers.Count);
        var fallbackColorIndex = 0;

        foreach (var selectedPlayer in selectedPlayers)
        {
            var existingActiveGame = await _activeGameIndexTable.GetEntityIfExistsAsync<IndexEntity>(
                "ACTIVE_GAME",
                selectedPlayer.UserId,
                cancellationToken: cancellationToken);

            if (existingActiveGame.HasValue)
            {
                var userName = selectedPlayer.UserId;
                try
                {
                    var activeUserResponse = await _usersTable.GetEntityAsync<ApplicationUser>("USER", selectedPlayer.UserId, cancellationToken: cancellationToken);
                    userName = string.IsNullOrWhiteSpace(activeUserResponse.Value.Nickname)
                        ? activeUserResponse.Value.Email
                        : activeUserResponse.Value.Nickname;
                }
                catch (RequestFailedException)
                {
                }

                return new GameActionResult { Success = false, Reason = $"Player '{userName}' already has an active game." };
            }

            ApplicationUser user;
            try
            {
                var response = await _usersTable.GetEntityAsync<ApplicationUser>("USER", selectedPlayer.UserId, cancellationToken: cancellationToken);
                user = response.Value;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return new GameActionResult { Success = false, Reason = "One or more selected players no longer exist." };
            }

            if (string.IsNullOrWhiteSpace(user.Nickname))
            {
                return new GameActionResult { Success = false, Reason = $"Player '{user.Email}' must set a nickname before being added to a game." };
            }

            var fallbackColor = PlayerColorOptions.Colors[fallbackColorIndex % PlayerColorOptions.Colors.Length];
            fallbackColorIndex++;

            resolvedPlayers.Add(new ResolvedGamePlayer
            {
                UserId = user.RowKey,
                DisplayName = user.Nickname,
                ThumbnailUrl = user.ThumbnailUrl,
                AssignedColor = PlayerColorOptions.NormalizeOrDefault(selectedPlayer.AssignedColor, fallbackColor)
            });
        }

        var distinctColors = resolvedPlayers
            .Select(player => player.AssignedColor)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        if (distinctColors != resolvedPlayers.Count)
        {
            return new GameActionResult { Success = false, Reason = "Each player must have a unique color." };
        }

        var gameId = Guid.NewGuid().ToString();

        // Create game entity
        var game = new GameEntity
        {
            PartitionKey = "ACTIVE",
            RowKey = gameId,
            CreatorId = creatorId,
            MapFileName = DefaultMapFileName,
            MaxPlayers = resolvedPlayers.Count,
            CurrentPlayerCount = resolvedPlayers.Count,
            Players = SerializePlayers(resolvedPlayers.Select((player, index) => new GameSeat
            {
                UserId = player.UserId,
                DisplayName = player.DisplayName,
                ThumbnailUrl = player.ThumbnailUrl,
                AssignedColor = player.AssignedColor,
                TurnOrder = index
            })),
            CreatedAt = DateTime.UtcNow
        };

        await _gamesTable.AddEntityAsync(game, cancellationToken);

        foreach (var player in resolvedPlayers)
        {
            await _activeGameIndexTable.AddEntityAsync(new IndexEntity
            {
                PartitionKey = "ACTIVE_GAME",
                RowKey = player.UserId,
                GameId = gameId
            }, cancellationToken);
        }

        try
        {
            await _gameEngine.CreateGameAsync(
                resolvedPlayers.Select(player => player.DisplayName).ToList(),
                new GameCreationOptions { PreferredGameId = gameId },
                cancellationToken);
        }
        catch
        {
            foreach (var player in resolvedPlayers)
            {
                try { await _activeGameIndexTable.DeleteEntityAsync("ACTIVE_GAME", player.UserId, cancellationToken: cancellationToken); }
                catch (RequestFailedException) { }
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

        ApplicationUser user;
        try
        {
            var userResponse = await _usersTable.GetEntityAsync<ApplicationUser>("USER", playerId, cancellationToken: cancellationToken);
            user = userResponse.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return new GameActionResult { Success = false, Reason = "Player profile could not be loaded." };
        }

        var existingPlayers = GetConfiguredPlayers(game);
        var fallbackColor = PlayerColorOptions.Colors
            .FirstOrDefault(color => existingPlayers.All(player => !string.Equals(player.AssignedColor, color, StringComparison.OrdinalIgnoreCase)))
            ?? PlayerColorOptions.Colors[game.CurrentPlayerCount % PlayerColorOptions.Colors.Length];
        var preferredColor = PlayerColorOptions.IsSupported(user.PreferredColor)
            && existingPlayers.All(player => !string.Equals(player.AssignedColor, user.PreferredColor, StringComparison.OrdinalIgnoreCase))
                ? user.PreferredColor
                : fallbackColor;

        existingPlayers.Add(new GameSeat
        {
            UserId = playerId,
            DisplayName = string.IsNullOrWhiteSpace(user.Nickname) ? user.Email : user.Nickname,
            ThumbnailUrl = user.ThumbnailUrl,
            AssignedColor = PlayerColorOptions.NormalizeOrDefault(preferredColor, fallbackColor),
            TurnOrder = game.CurrentPlayerCount
        });

        // Insert active game index
        await _activeGameIndexTable.AddEntityAsync(new IndexEntity
        {
            PartitionKey = "ACTIVE_GAME",
            RowKey = playerId,
            GameId = gameId
        }, cancellationToken);

        // Increment player count with ETag concurrency
        game.CurrentPlayerCount++;
        game.Players = SerializePlayers(existingPlayers);
        try
        {
            await _gamesTable.UpdateEntityAsync(game, game.ETag, TableUpdateMode.Replace, cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 412)
        {
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

    public async Task<List<GameSeat>> GetGamePlayersAsync(string gameId, CancellationToken cancellationToken)
    {
        var game = await GetGameAsync(gameId, cancellationToken);
        return game is null ? [] : GetConfiguredPlayers(game);
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

        var gamePlayers = GetConfiguredPlayers(game);

        foreach (var gamePlayer in gamePlayers)
        {
            try
            {
                await _activeGameIndexTable.DeleteEntityAsync("ACTIVE_GAME", gamePlayer.UserId, cancellationToken: cancellationToken);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Ignore missing index rows.
            }
        }

        await _gamesTable.DeleteEntityAsync("ACTIVE", gameId, game.ETag, cancellationToken: cancellationToken);

        await _hubContext.Clients.All.SendAsync("DashboardStateRefreshed", cancellationToken);

        return new GameActionResult { Success = true, GameId = gameId };
    }

    private static List<GameSeat> GetConfiguredPlayers(GameEntity game)
    {
        if (string.IsNullOrWhiteSpace(game.Players))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<GameSeat>>(game.Players, JsonSerializerOptions)?
                .OrderBy(player => player.TurnOrder)
                .ToList()
                ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string SerializePlayers(IEnumerable<GameSeat> players)
    {
        return JsonSerializer.Serialize(players.OrderBy(player => player.TurnOrder), JsonSerializerOptions);
    }
}

public sealed class GameSeat
{
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }
    public string ThumbnailUrl { get; init; } = string.Empty;
    public required string AssignedColor { get; init; }
    public int TurnOrder { get; init; }
}

file sealed class ResolvedGamePlayer
{
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }
    public required string ThumbnailUrl { get; init; }
    public required string AssignedColor { get; init; }
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
