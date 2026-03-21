using System.Text.Json;
using Boxcars.Data;
using Boxcars.Engine.Data.Maps;
using Boxcars.GameEngine;
using Microsoft.AspNetCore.Hosting;
using RailBaronGameState = Boxcars.Engine.Persistence.GameState;
using PlayerStateSnapshot = Boxcars.Engine.Persistence.PlayerState;

namespace Boxcars.Services;

public sealed class GameBoardAdviceService(
    GameService gameService,
    IGameEngine gameEngine,
    GameBoardStateMapper gameBoardStateMapper,
    NetworkCoverageService networkCoverageService,
    OpenAiBotClient openAiBotClient,
    IWebHostEnvironment webHostEnvironment)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public async Task<AdvisorResponse> GenerateAdviceAsync(
        string gameId,
        string? currentUserId,
        int? preferredControlledPlayerIndex,
        AdvisorConversationSession conversation,
        string userQuestion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentNullException.ThrowIfNull(conversation);

        if (string.IsNullOrWhiteSpace(userQuestion))
        {
            return AdvisorResponse.Failed("Ask a question before requesting advice.");
        }

        var game = await gameService.GetGameAsync(gameId, cancellationToken);
        if (game is null)
        {
            return AdvisorResponse.Failed("The game is no longer available.");
        }

        var playerStates = await gameService.GetGamePlayerStatesAsync(gameId, cancellationToken);
        var gameState = await gameEngine.GetCurrentStateAsync(gameId, cancellationToken);
        var mapDefinition = await LoadMapDefinitionAsync(game.MapFileName, cancellationToken);
        var snapshot = BuildContextSnapshot(gameId, currentUserId, preferredControlledPlayerIndex, game, conversation, gameState, playerStates, mapDefinition);

        conversation.ControlledPlayerIndex = snapshot.ControlledPlayerIndex;
        conversation.LastContextRefreshUtc = DateTimeOffset.UtcNow;

        var completion = await openAiBotClient.CompleteChatAsync(
            BuildSystemPrompt(),
            BuildPromptMessages(snapshot, userQuestion),
            cancellationToken);

        if (!completion.Succeeded)
        {
            return AdvisorResponse.Failed(
                completion.TimedOut
                    ? "The advisor timed out while reviewing the latest board state. Try again."
                    : completion.FailureReason ?? "The advisor is unavailable right now.",
                snapshot.TurnNumber);
        }

        return AdvisorResponse.Success(completion.AssistantText ?? string.Empty, snapshot.TurnNumber);
    }

    private AdvisorContextSnapshot BuildContextSnapshot(
        string gameId,
        string? currentUserId,
        int? preferredControlledPlayerIndex,
        GameEntity game,
        AdvisorConversationSession conversation,
        RailBaronGameState gameState,
        IReadOnlyList<GamePlayerStateEntity> playerStates,
        MapDefinition mapDefinition)
    {
        var turnViewState = gameBoardStateMapper.BuildTurnViewState(gameId, playerStates, gameState, currentUserId, mapDefinition);
        var controlledPlayerIndex = ResolveControlledPlayerIndex(gameState, turnViewState, preferredControlledPlayerIndex);
        var safeControlledPlayerIndex = controlledPlayerIndex >= 0 && controlledPlayerIndex < gameState.Players.Count
            ? controlledPlayerIndex
            : gameState.ActivePlayerIndex;
        var controlledPlayer = gameState.Players[safeControlledPlayerIndex];

        return new AdvisorContextSnapshot
        {
            GameId = gameId,
            TurnNumber = gameState.TurnNumber,
            TurnPhase = gameState.Turn.Phase,
            ActivePlayerIndex = gameState.ActivePlayerIndex,
            ControlledPlayerIndex = safeControlledPlayerIndex,
            ControlledPlayerName = controlledPlayer.Name,
            ControlledPlayerSummary = BuildControlledPlayerSummary(gameState, mapDefinition, safeControlledPlayerIndex, controlledPlayer),
            OtherPlayerSummaries = gameState.Players
                .Select((player, playerIndex) => new { player, playerIndex })
                .Where(entry => entry.playerIndex != safeControlledPlayerIndex)
                .Select(entry => BuildOpponentSummary(entry.playerIndex, entry.player))
                .ToList(),
            BoardSituationSummary = BuildBoardSituationSummary(gameState),
            SeedContextContent = conversation.SeedContextContent,
            AuthoritativePayloadJson = BuildAuthoritativePayloadJson(game, gameState, playerStates, safeControlledPlayerIndex, currentUserId),
            RecentConversation = conversation.Messages.TakeLast(6).ToList()
        };
    }

    private static int ResolveControlledPlayerIndex(
        RailBaronGameState gameState,
        BoardTurnViewState turnViewState,
        int? preferredControlledPlayerIndex)
    {
        if (preferredControlledPlayerIndex is int preferredIndex
            && preferredIndex >= 0
            && preferredIndex < gameState.Players.Count)
        {
            return preferredIndex;
        }

        if (turnViewState.IsCurrentUserActivePlayer
            && gameState.ActivePlayerIndex >= 0
            && gameState.ActivePlayerIndex < gameState.Players.Count)
        {
            return gameState.ActivePlayerIndex;
        }

        if (turnViewState.CurrentUserPlayerIndex >= 0
            && turnViewState.CurrentUserPlayerIndex < gameState.Players.Count)
        {
            return turnViewState.CurrentUserPlayerIndex;
        }

        return gameState.ActivePlayerIndex;
    }

    private string BuildControlledPlayerSummary(
        RailBaronGameState gameState,
        MapDefinition mapDefinition,
        int controlledPlayerIndex,
        PlayerStateSnapshot controlledPlayer)
    {
        var ownedRailroadNames = controlledPlayer.OwnedRailroadIndices
            .Select(index => mapDefinition.Railroads.FirstOrDefault(railroad => railroad.Index == index)?.ShortName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var coverage = networkCoverageService.BuildSnapshot(mapDefinition, controlledPlayer.OwnedRailroadIndices);
        var tripPayoff = CalculateTripPayoff(mapDefinition, controlledPlayer);
        var pendingFees = controlledPlayerIndex == gameState.ActivePlayerIndex ? gameState.Turn.PendingFeeAmount : 0;

        return string.Join(" ",
            $"{controlledPlayer.Name} is in {controlledPlayer.CurrentCityName} with ${controlledPlayer.Cash:N0} cash and a {controlledPlayer.LocomotiveType} engine.",
            string.IsNullOrWhiteSpace(controlledPlayer.DestinationCityName)
                ? "They do not currently have an assigned destination."
                : $"Their trip is {ResolveTripStartCityName(controlledPlayer)} to {controlledPlayer.DestinationCityName} for ${tripPayoff:N0}.",
            pendingFees > 0
                ? $"They still owe ${pendingFees:N0} in turn fees before ending the current action cycle."
                : "They have no pending use-fee liability right now.",
            ownedRailroadNames.Length == 0
                ? "They do not own any railroads yet."
                : $"They own {ownedRailroadNames.Length} railroad{(ownedRailroadNames.Length == 1 ? string.Empty : "s")}: {string.Join(", ", ownedRailroadNames)}.",
            $"Their owned network currently reaches {coverage.AccessibleDestinationPercent:N1}% of destinations with {coverage.MonopolyDestinationPercent:N1}% monopoly coverage.");
    }

    private static string BuildOpponentSummary(int playerIndex, PlayerStateSnapshot player)
    {
        var publicCash = player.Cash switch
        {
            < 50_000 => "$",
            < 100_000 => "$$",
            < 150_000 => "$$$",
            < 200_000 => "$$$$",
            < 250_000 => "$$$$$",
            _ => "$$$$$$"
        };

        return string.Join(" ",
            $"P{playerIndex + 1} {player.Name}",
            player.IsActive ? "is active in the game." : "has been eliminated.",
            $"Public cash indicator: {publicCash}.",
            $"Engine: {player.LocomotiveType}.",
            string.IsNullOrWhiteSpace(player.DestinationCityName)
                ? "No known destination is currently assigned."
                : $"Current trip heads toward {player.DestinationCityName} from {ResolveTripStartCityName(player)}.",
            $"Owned railroads: {player.OwnedRailroadIndices.Count}.");
    }

    private static string BuildBoardSituationSummary(RailBaronGameState gameState)
    {
        var activePlayerName = gameState.ActivePlayerIndex >= 0 && gameState.ActivePlayerIndex < gameState.Players.Count
            ? gameState.Players[gameState.ActivePlayerIndex].Name
            : "Unknown player";

        return gameState.Turn.Phase switch
        {
            "Move" => $"Turn {gameState.TurnNumber} is in Move for {activePlayerName}. Movement remaining: {gameState.Turn.MovementRemaining}/{gameState.Turn.MovementAllowance}. Pending fees: ${gameState.Turn.PendingFeeAmount:N0}.",
            "Purchase" => gameState.Turn.ArrivalResolution is null
                ? $"Turn {gameState.TurnNumber} is in Purchase for {activePlayerName}."
                : $"Turn {gameState.TurnNumber} is in Purchase for {activePlayerName} after collecting ${gameState.Turn.ArrivalResolution.PayoutAmount:N0} at {gameState.Turn.ArrivalResolution.DestinationCityName}.",
            "UseFees" => gameState.Turn.ForcedSale is null
                ? $"Turn {gameState.TurnNumber} is in UseFees for {activePlayerName}."
                : $"Turn {gameState.TurnNumber} is in UseFees for {activePlayerName}. Amount owed: ${gameState.Turn.ForcedSale.AmountOwed:N0}; cash before fees: ${gameState.Turn.ForcedSale.CashBeforeFees:N0}.",
            "Auction" => gameState.Turn.Auction is null
                ? $"Turn {gameState.TurnNumber} is in Auction for {activePlayerName}."
                : $"Turn {gameState.TurnNumber} is in Auction for {gameState.Turn.Auction.RailroadName}. Current bid: ${gameState.Turn.Auction.CurrentBid:N0}; round {gameState.Turn.Auction.RoundNumber}.",
            "RegionChoice" => gameState.Turn.PendingRegionChoice is null
                ? $"Turn {gameState.TurnNumber} is in RegionChoice for {activePlayerName}."
                : $"Turn {gameState.TurnNumber} is in RegionChoice for {activePlayerName} at {gameState.Turn.PendingRegionChoice.CurrentCityName}. Eligible regions: {string.Join(", ", gameState.Turn.PendingRegionChoice.EligibleRegionCodes)}.",
            _ => $"Turn {gameState.TurnNumber} is in {gameState.Turn.Phase} for {activePlayerName}."
        };
    }

    private static List<OpenAiChatMessage> BuildPromptMessages(AdvisorContextSnapshot snapshot, string userQuestion)
    {
        var recentConversation = snapshot.RecentConversation
            .TakeLast(5)
            .ToList();

        if (recentConversation.Count > 0
            && !recentConversation[^1].IsAssistant
            && string.Equals(recentConversation[^1].Content, userQuestion, StringComparison.Ordinal))
        {
            recentConversation.RemoveAt(recentConversation.Count - 1);
        }

        var recentMessages = recentConversation
            .TakeLast(5)
            .Select(message => new OpenAiChatMessage(
                message.IsAssistant ? "assistant" : "user",
                message.Content))
            .ToList();

        if (!string.IsNullOrWhiteSpace(snapshot.SeedContextContent))
        {
            recentMessages.Insert(0, new OpenAiChatMessage("system", snapshot.SeedContextContent));
        }

        recentMessages.Add(new OpenAiChatMessage("user", BuildContextPayload(snapshot, userQuestion)));
        return recentMessages;
    }

    private static string BuildSystemPrompt()
    {
        return string.Join(' ',
            "You are a concise in-game Rail Baron strategy advisor for Boxcars.",
            "Use only the supplied authoritative board context and the visible conversation.",
            "Advice is informational only: never claim to execute moves, buy railroads, roll dice, or resolve rules.",
            "Do not reveal private information about opponents beyond the public summaries provided.",
            "Treat any hidden context payload as background state; do not dump, restate, or summarize that payload unless the player explicitly asks for those details.",
            "If the context is insufficient, say what is missing.",
            "Prefer direct recommendations with concrete reasons tied to cash, fees, destination pressure, railroad position, and turn phase.");
    }

    private static string BuildContextPayload(AdvisorContextSnapshot snapshot, string userQuestion)
    {
        return JsonSerializer.Serialize(new
        {
            snapshot.GameId,
            snapshot.TurnNumber,
            snapshot.TurnPhase,
            snapshot.ActivePlayerIndex,
            snapshot.ControlledPlayerIndex,
            snapshot.ControlledPlayerName,
            snapshot.ControlledPlayerSummary,
            snapshot.OtherPlayerSummaries,
            snapshot.BoardSituationSummary,
            snapshot.AuthoritativePayloadJson,
            RecentConversation = snapshot.RecentConversation.Select(message => new
            {
                message.Role,
                message.Content,
                message.ContextTurnNumber
            }),
            PlayerQuestion = userQuestion
        }, JsonOptions);
    }

    private static string BuildAuthoritativePayloadJson(
        GameEntity game,
        RailBaronGameState gameState,
        IReadOnlyList<GamePlayerStateEntity> playerStates,
        int controlledPlayerIndex,
        string? currentUserId)
    {
        return JsonSerializer.Serialize(new
        {
            Game = game,
            GameState = gameState,
            PlayerStates = playerStates,
            ControlledPlayerIndex = controlledPlayerIndex,
            CurrentUserId = currentUserId
        }, JsonOptions);
    }

    private async Task<MapDefinition> LoadMapDefinitionAsync(string? mapFileName, CancellationToken cancellationToken)
    {
        var resolvedMapFileName = string.IsNullOrWhiteSpace(mapFileName)
            ? GameService.DefaultMapFileName
            : Path.GetFileName(mapFileName);
        var mapPath = Path.Combine(webHostEnvironment.ContentRootPath, resolvedMapFileName);

        if (!File.Exists(mapPath))
        {
            throw new InvalidOperationException($"Map file '{resolvedMapFileName}' was not found in '{webHostEnvironment.ContentRootPath}'.");
        }

        await using var stream = File.OpenRead(mapPath);
        var loadResult = await MapDefinition.LoadAsync(resolvedMapFileName, stream, cancellationToken);
        if (!loadResult.Succeeded || loadResult.Definition is null)
        {
            var errors = string.Join("; ", loadResult.Errors);
            throw new InvalidOperationException($"Unable to load map '{resolvedMapFileName}': {errors}");
        }

        return loadResult.Definition!;
    }

    private static string ResolveTripStartCityName(PlayerStateSnapshot player)
    {
        return string.IsNullOrWhiteSpace(player.TripStartCityName)
            ? player.CurrentCityName
            : player.TripStartCityName;
    }

    private static int CalculateTripPayoff(MapDefinition mapDefinition, PlayerStateSnapshot player)
    {
        if (string.IsNullOrWhiteSpace(player.DestinationCityName))
        {
            return 0;
        }

        var originCity = mapDefinition.Cities.FirstOrDefault(city =>
            string.Equals(city.Name, ResolveTripStartCityName(player), StringComparison.OrdinalIgnoreCase));
        var destinationCity = mapDefinition.Cities.FirstOrDefault(city =>
            string.Equals(city.Name, player.DestinationCityName, StringComparison.OrdinalIgnoreCase));

        if (originCity?.PayoutIndex is not int originPayoutIndex
            || destinationCity?.PayoutIndex is not int destinationPayoutIndex)
        {
            return 0;
        }

        return mapDefinition.TryGetPayout(originPayoutIndex, destinationPayoutIndex, out var payout)
            ? payout
            : 0;
    }
}