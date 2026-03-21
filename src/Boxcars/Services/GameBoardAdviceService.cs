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
        var railroadCityMap = BuildRailroadCityMap(mapDefinition);

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
                .Select(entry => BuildOpponentSummary(entry.playerIndex, entry.player, mapDefinition))
                .ToList(),
            BoardSituationSummary = BuildBoardSituationSummary(gameState),
            SeedContextContent = conversation.SeedContextContent,
            AuthoritativePayloadJson = string.Empty,
            RecentConversation = conversation.Messages.TakeLast(6).ToList(),
            MapContext = BuildMapContext(mapDefinition, railroadCityMap),
            ControlledPlayerContext = BuildControlledPlayerContext(gameState, mapDefinition, safeControlledPlayerIndex, controlledPlayer),
            OpponentContexts = gameState.Players
                .Select((player, idx) => new { player, idx })
                .Where(x => x.idx != safeControlledPlayerIndex)
                .Select(x => BuildOpponentContext(x.idx, x.player, mapDefinition))
                .ToList(),
            AvailableRailroads = BuildAvailableRailroads(gameState, mapDefinition, controlledPlayer, railroadCityMap)
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

    private static string BuildOpponentSummary(int playerIndex, PlayerStateSnapshot player, MapDefinition mapDefinition)
    {
        var ownedRailroadNames = player.OwnedRailroadIndices
            .Select(idx => mapDefinition.Railroads.FirstOrDefault(r => r.Index == idx)?.ShortName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return string.Join(" ",
            $"P{playerIndex + 1} {player.Name}",
            player.IsActive ? "is active in the game." : "has been eliminated.",
            $"Cash: ${player.Cash:N0}.",
            $"Engine: {player.LocomotiveType}.",
            string.IsNullOrWhiteSpace(player.DestinationCityName)
                ? "No known destination is currently assigned."
                : $"Current trip heads toward {player.DestinationCityName} from {ResolveTripStartCityName(player)}.",
            ownedRailroadNames.Length == 0
                ? "No railroads owned."
                : $"Owned railroads: {string.Join(", ", ownedRailroadNames)}.");
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

    private static Dictionary<int, List<string>> BuildRailroadCityMap(MapDefinition mapDefinition)
    {
        var dotToCityName = new Dictionary<(int RegionIndex, int DotIndex), string>();
        foreach (var city in mapDefinition.Cities)
        {
            if (city.MapDotIndex is not int dotIndex) continue;
            var regionIndex = mapDefinition.Regions.FindIndex(r =>
                string.Equals(r.Code, city.RegionCode, StringComparison.OrdinalIgnoreCase));
            if (regionIndex >= 0)
                dotToCityName[(regionIndex, dotIndex)] = city.Name;
        }

        var result = new Dictionary<int, HashSet<string>>();
        foreach (var segment in mapDefinition.RailroadRouteSegments)
        {
            if (!result.TryGetValue(segment.RailroadIndex, out var cities))
            {
                cities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                result[segment.RailroadIndex] = cities;
            }

            if (dotToCityName.TryGetValue((segment.StartRegionIndex, segment.StartDotIndex), out var startCity))
                cities.Add(startCity);
            if (dotToCityName.TryGetValue((segment.EndRegionIndex, segment.EndDotIndex), out var endCity))
                cities.Add(endCity);
        }

        return result.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Order(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static AdvisorMapContext BuildMapContext(MapDefinition mapDefinition, Dictionary<int, List<string>> railroadCityMap)
    {
        var regions = mapDefinition.Regions.Select(r => new AdvisorRegionInfo
        {
            Code = r.Code,
            Name = r.Name,
            Probability = r.Probability ?? 0,
            Cities = mapDefinition.Cities
                .Where(c => string.Equals(c.RegionCode, r.Code, StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Name)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList()
        }).ToList();

        var railroads = mapDefinition.Railroads.Select(r => new AdvisorRailroadInfo
        {
            Index = r.Index,
            Name = r.ShortName ?? r.Name,
            PurchasePrice = r.PurchasePrice ?? 0,
            CityCount = railroadCityMap.TryGetValue(r.Index, out var cities) ? cities.Count : 0,
            ConnectedCities = railroadCityMap.TryGetValue(r.Index, out var connCities) ? connCities : []
        }).ToList();

        return new AdvisorMapContext
        {
            MapName = mapDefinition.Name ?? "Rail Baron",
            RegionCount = mapDefinition.Regions.Count,
            CityCount = mapDefinition.Cities.Count,
            RailroadCount = mapDefinition.Railroads.Count,
            Regions = regions,
            Railroads = railroads
        };
    }

    private AdvisorPlayerContext BuildControlledPlayerContext(
        RailBaronGameState gameState,
        MapDefinition mapDefinition,
        int controlledPlayerIndex,
        PlayerStateSnapshot controlledPlayer)
    {
        var coverage = networkCoverageService.BuildSnapshot(mapDefinition, controlledPlayer.OwnedRailroadIndices);
        var tripPayout = CalculateTripPayoff(mapDefinition, controlledPlayer);
        var pendingFees = controlledPlayerIndex == gameState.ActivePlayerIndex ? gameState.Turn.PendingFeeAmount : 0;
        var ownedRailroadNames = controlledPlayer.OwnedRailroadIndices
            .Select(idx => mapDefinition.Railroads.FirstOrDefault(r => r.Index == idx))
            .Where(r => r is not null)
            .Select(r => r!.ShortName ?? r.Name)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new AdvisorPlayerContext
        {
            Name = controlledPlayer.Name,
            Cash = controlledPlayer.Cash,
            Engine = controlledPlayer.LocomotiveType,
            CurrentCity = controlledPlayer.CurrentCityName,
            HomeCity = controlledPlayer.HomeCityName,
            TripOrigin = string.IsNullOrWhiteSpace(controlledPlayer.DestinationCityName) ? null : ResolveTripStartCityName(controlledPlayer),
            TripDestination = controlledPlayer.DestinationCityName,
            TripPayout = tripPayout,
            RouteProgressIndex = controlledPlayer.RouteProgressIndex,
            PendingFees = pendingFees,
            HasDeclared = controlledPlayer.HasDeclared,
            OwnedRailroads = ownedRailroadNames,
            NetworkAccessPercent = coverage.AccessibleDestinationPercent,
            MonopolyPercent = coverage.MonopolyDestinationPercent,
            RegionCoverage = coverage.RegionAccess.Select(ra => new AdvisorRegionCoverage
            {
                RegionCode = ra.RegionCode,
                AccessPercent = ra.AccessibleDestinationPercent,
                MonopolyPercent = ra.MonopolyDestinationPercent
            }).ToList()
        };
    }

    private AdvisorOpponentContext BuildOpponentContext(
        int playerIndex,
        PlayerStateSnapshot player,
        MapDefinition mapDefinition)
    {
        var coverage = networkCoverageService.BuildSnapshot(mapDefinition, player.OwnedRailroadIndices);
        var tripPayout = CalculateTripPayoff(mapDefinition, player);
        var ownedRailroadNames = player.OwnedRailroadIndices
            .Select(idx => mapDefinition.Railroads.FirstOrDefault(r => r.Index == idx))
            .Where(r => r is not null)
            .Select(r => r!.ShortName ?? r.Name)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new AdvisorOpponentContext
        {
            PlayerIndex = playerIndex,
            Name = player.Name,
            Cash = player.Cash,
            Engine = player.LocomotiveType,
            CurrentCity = player.CurrentCityName,
            HomeCity = player.HomeCityName,
            TripOrigin = string.IsNullOrWhiteSpace(player.DestinationCityName) ? null : ResolveTripStartCityName(player),
            TripDestination = player.DestinationCityName,
            TripPayout = tripPayout,
            RouteProgressIndex = player.RouteProgressIndex,
            HasDeclared = player.HasDeclared,
            IsActive = player.IsActive,
            OwnedRailroads = ownedRailroadNames,
            NetworkAccessPercent = coverage.AccessibleDestinationPercent
        };
    }

    private List<AdvisorPurchasableRailroad> BuildAvailableRailroads(
        RailBaronGameState gameState,
        MapDefinition mapDefinition,
        PlayerStateSnapshot controlledPlayer,
        Dictionary<int, List<string>> railroadCityMap)
    {
        var currentCoverage = networkCoverageService.BuildSnapshot(mapDefinition, controlledPlayer.OwnedRailroadIndices);

        return mapDefinition.Railroads
            .Where(r => r.PurchasePrice.HasValue
                && (!gameState.RailroadOwnership.TryGetValue(r.Index, out var owner) || owner is null))
            .Select(r =>
            {
                var projected = networkCoverageService.BuildProjectedSnapshot(
                    mapDefinition, controlledPlayer.OwnedRailroadIndices, r.Index);
                return new AdvisorPurchasableRailroad
                {
                    Index = r.Index,
                    Name = r.ShortName ?? r.Name,
                    Price = r.PurchasePrice ?? 0,
                    CityCount = railroadCityMap.TryGetValue(r.Index, out var cities) ? cities.Count : 0,
                    ProjectedAccessPercent = projected.AccessibleDestinationPercent,
                    ProjectedMonopolyPercent = projected.MonopolyDestinationPercent,
                    AccessGain = projected.AccessibleDestinationPercent - currentCoverage.AccessibleDestinationPercent,
                    MonopolyGain = projected.MonopolyDestinationPercent - currentCoverage.MonopolyDestinationPercent
                };
            })
            .OrderByDescending(r => r.AccessGain)
            .ToList();
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
            "Each user message contains a JSON context payload with the full authoritative board state.",
            "ControlledPlayer contains the asking player's exact cash, owned railroads, trip details, network coverage by region, and declaration status.",
            "Opponents lists each opponent's cash, owned railroads, trip, coverage, and declaration status.",
            "AvailableRailroads lists unowned railroads with price, connected city count, and projected coverage gain if purchased.",
            "Map contains all regions with destination probabilities and city lists, plus all railroads with connected cities and pricing.",
            "Use these structured fields for precise analysis. Do not restate them verbatim unless the player asks.",
            "Advice is informational only: never claim to execute moves, buy railroads, roll dice, or resolve rules.",
            "If the context is insufficient, say what is missing.",
            "Prefer direct recommendations with concrete reasons tied to cash, fees, destination pressure, network coverage, railroad value, and turn phase.");
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
            ControlledPlayer = snapshot.ControlledPlayerContext,
            Opponents = snapshot.OpponentContexts,
            AvailableRailroads = snapshot.AvailableRailroads,
            Map = snapshot.MapContext,
            PlayerQuestion = userQuestion
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