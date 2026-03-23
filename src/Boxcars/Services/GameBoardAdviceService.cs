using System.Globalization;
using System.Text.Json;
using Boxcars.Data;
using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Persistence;
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
    PlayerProfileService playerProfileService,
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
        var settings = new GameSettingsResolver().Resolve(game).Settings;

        // Resolve the controlled player's strategy text from their profile
        var controlledSeatIndex = ResolveControlledPlayerIndex(gameState,
            gameBoardStateMapper.BuildTurnViewState(gameId, playerStates, gameState, currentUserId, mapDefinition, gameEntity: game),
            preferredControlledPlayerIndex);
        var safeIndex = controlledSeatIndex >= 0 && controlledSeatIndex < playerStates.Count
            ? controlledSeatIndex
            : 0;
        var controlledUserId = playerStates.FirstOrDefault(ps => ps.SeatIndex == safeIndex)?.PlayerUserId;
        var strategyText = string.Empty;
        if (!string.IsNullOrWhiteSpace(controlledUserId))
        {
            var profile = await playerProfileService.GetProfileAsync(controlledUserId, cancellationToken);
            strategyText = PlayerProfileService.ResolveStrategyTextOrDefault(profile?.StrategyText);
        }

        var snapshot = BuildContextSnapshot(gameId, currentUserId, preferredControlledPlayerIndex, game, conversation, gameState, playerStates, mapDefinition, strategyText, settings);

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
        MapDefinition mapDefinition,
        string strategyText,
        GameSettings settings)
    {
        var turnViewState = gameBoardStateMapper.BuildTurnViewState(gameId, playerStates, gameState, currentUserId, mapDefinition, gameEntity: game);
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
                .Select(entry => BuildOpponentSummary(entry.playerIndex, entry.player, mapDefinition, settings))
                .ToList(),
            BoardSituationSummary = BuildBoardSituationSummary(gameState),
            SeedContextContent = conversation.SeedContextContent,
            AuthoritativePayloadJson = string.Empty,
            RecentConversation = conversation.Messages.TakeLast(6).ToList(),
            MapContext = BuildMapContext(mapDefinition, railroadCityMap, gameState),
            ControlledPlayerContext = BuildControlledPlayerContext(gameState, mapDefinition, safeControlledPlayerIndex, controlledPlayer, settings),
            OpponentContexts = gameState.Players
                .Select((player, idx) => new { player, idx })
                .Where(x => x.idx != safeControlledPlayerIndex)
                .Select(x => BuildOpponentContext(x.idx, x.player, gameState, mapDefinition, settings))
                .ToList(),
            AvailableRailroads = BuildAvailableRailroads(gameState, mapDefinition, controlledPlayer, railroadCityMap),
            HighValuePayouts = BuildHighValuePayouts(mapDefinition),
            StrategyText = strategyText
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

    private static string BuildOpponentSummary(int playerIndex, PlayerStateSnapshot player, MapDefinition mapDefinition, GameSettings settings)
    {
        var ownedRailroadNames = player.OwnedRailroadIndices
            .Select(idx => mapDefinition.Railroads.FirstOrDefault(r => r.Index == idx)?.ShortName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var visibleCash = FormatVisibleCash(player.Cash, settings);

        return string.Join(" ",
            $"P{playerIndex + 1} {player.Name}",
            player.IsActive ? "is active in the game." : "has been eliminated.",
            $"Cash: {visibleCash}.",
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

    private static AdvisorMapContext BuildMapContext(MapDefinition mapDefinition, Dictionary<int, List<string>> railroadCityMap, RailBaronGameState gameState)
    {
        var regions = mapDefinition.Regions.Select(r => new AdvisorRegionInfo
        {
            Code = r.Code,
            Name = r.Name,
            Probability = r.Probability ?? 0,
            Cities = mapDefinition.Cities
                .Where(c => string.Equals(c.RegionCode, r.Code, StringComparison.OrdinalIgnoreCase))
                .Select(c => new AdvisorCityInfo
                {
                    Name = c.Name,
                    Probability = c.Probability ?? 0
                })
                .OrderByDescending(c => c.Probability)
                .ToList()
        }).ToList();

        var railroads = mapDefinition.Railroads.Select(r =>
        {
            string? ownerName = null;
            if (gameState.RailroadOwnership.TryGetValue(r.Index, out var ownerIndex) && ownerIndex is int idx
                && idx >= 0 && idx < gameState.Players.Count)
            {
                ownerName = gameState.Players[idx].Name;
            }

            return new AdvisorRailroadInfo
            {
                Index = r.Index,
                Name = r.ShortName ?? r.Name,
                PurchasePrice = r.PurchasePrice ?? 0,
                CityCount = railroadCityMap.TryGetValue(r.Index, out var cities) ? cities.Count : 0,
                Owner = ownerName,
                ConnectedCities = railroadCityMap.TryGetValue(r.Index, out var connCities) ? connCities : []
            };
        }).ToList();

        return new AdvisorMapContext
        {
            MapName = mapDefinition.Name ?? "Rail Baron",
            RegionCount = mapDefinition.Regions.Count,
            CityCount = mapDefinition.Cities.Count,
            RailroadCount = mapDefinition.Railroads.Count,
            AllRailroadsSold = gameState.AllRailroadsSold,
            Regions = regions,
            Railroads = railroads
        };
    }

    private AdvisorPlayerContext BuildControlledPlayerContext(
        RailBaronGameState gameState,
        MapDefinition mapDefinition,
        int controlledPlayerIndex,
        PlayerStateSnapshot controlledPlayer,
        GameSettings settings)
    {
        var ownedCoverage = networkCoverageService.BuildSnapshot(mapDefinition, controlledPlayer.OwnedRailroadIndices);
        var otherOwnedIndices = gameState.Players
            .Where((_, idx) => idx != controlledPlayerIndex)
            .SelectMany(p => p.OwnedRailroadIndices);
        var effectiveCoverage = networkCoverageService.BuildSnapshotIncludingPublicRailroads(
            mapDefinition, controlledPlayer.OwnedRailroadIndices, otherOwnedIndices);
        var tripPayout = CalculateTripPayoff(mapDefinition, controlledPlayer);
        var pendingFees = controlledPlayerIndex == gameState.ActivePlayerIndex ? gameState.Turn.PendingFeeAmount : 0;
        var ownedRailroadNames = controlledPlayer.OwnedRailroadIndices
            .Select(idx => mapDefinition.Railroads.FirstOrDefault(r => r.Index == idx))
            .Where(r => r is not null)
            .Select(r => r!.ShortName ?? r.Name)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var effectiveRegionLookup = effectiveCoverage.RegionAccess
            .ToDictionary(ra => ra.RegionCode, ra => ra.AccessibleDestinationPercent, StringComparer.OrdinalIgnoreCase);

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
            NetworkAccessPercent = ownedCoverage.AccessibleDestinationPercent,
            EffectiveAccessPercent = effectiveCoverage.AccessibleDestinationPercent,
            MonopolyPercent = ownedCoverage.MonopolyDestinationPercent,
            CashToWin = Math.Max(0, settings.WinningCash - controlledPlayer.Cash),
            RegionCoverage = ownedCoverage.RegionAccess.Select(ra => new AdvisorRegionCoverage
            {
                RegionCode = ra.RegionCode,
                AccessPercent = ra.AccessibleDestinationPercent,
                EffectiveAccessPercent = effectiveRegionLookup.TryGetValue(ra.RegionCode, out var eff) ? eff : ra.AccessibleDestinationPercent,
                MonopolyPercent = ra.MonopolyDestinationPercent
            }).ToList()
        };
    }

    private AdvisorOpponentContext BuildOpponentContext(
        int playerIndex,
        PlayerStateSnapshot player,
        RailBaronGameState gameState,
        MapDefinition mapDefinition,
        GameSettings settings)
    {
        var ownedCoverage = networkCoverageService.BuildSnapshot(mapDefinition, player.OwnedRailroadIndices);
        var otherOwnedIndices = gameState.Players
            .Where((_, idx) => idx != playerIndex)
            .SelectMany(p => p.OwnedRailroadIndices);
        var effectiveCoverage = networkCoverageService.BuildSnapshotIncludingPublicRailroads(
            mapDefinition, player.OwnedRailroadIndices, otherOwnedIndices);
        var tripPayout = CalculateTripPayoff(mapDefinition, player);
        var ownedRailroadNames = player.OwnedRailroadIndices
            .Select(idx => mapDefinition.Railroads.FirstOrDefault(r => r.Index == idx))
            .Where(r => r is not null)
            .Select(r => r!.ShortName ?? r.Name)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var effectiveRegionLookup = effectiveCoverage.RegionAccess
            .ToDictionary(ra => ra.RegionCode, ra => ra.AccessibleDestinationPercent, StringComparer.OrdinalIgnoreCase);

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
            NetworkAccessPercent = ownedCoverage.AccessibleDestinationPercent,
            EffectiveAccessPercent = effectiveCoverage.AccessibleDestinationPercent,
            CashToWin = Math.Max(0, settings.WinningCash - player.Cash),
            RegionCoverage = ownedCoverage.RegionAccess.Select(ra => new AdvisorRegionCoverage
            {
                RegionCode = ra.RegionCode,
                AccessPercent = ra.AccessibleDestinationPercent,
                EffectiveAccessPercent = effectiveRegionLookup.TryGetValue(ra.RegionCode, out var eff) ? eff : ra.AccessibleDestinationPercent,
                MonopolyPercent = ra.MonopolyDestinationPercent
            }).ToList()
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
                    CashAfterPurchase = controlledPlayer.Cash - (r.PurchasePrice ?? 0),
                    ProjectedAccessPercent = projected.AccessibleDestinationPercent,
                    ProjectedMonopolyPercent = projected.MonopolyDestinationPercent,
                    AccessGain = projected.AccessibleDestinationPercent - currentCoverage.AccessibleDestinationPercent,
                    MonopolyGain = projected.MonopolyDestinationPercent - currentCoverage.MonopolyDestinationPercent
                };
            })
            .OrderByDescending(r => r.AccessGain)
            .ToList();
    }

    private static List<AdvisorPayoutEntry> BuildHighValuePayouts(MapDefinition mapDefinition)
    {
        var citiesWithPayout = mapDefinition.Cities
            .Where(c => c.PayoutIndex.HasValue)
            .ToList();

        var entries = new List<AdvisorPayoutEntry>();
        for (int i = 0; i < citiesWithPayout.Count; i++)
        {
            for (int j = i + 1; j < citiesWithPayout.Count; j++)
            {
                var from = citiesWithPayout[i];
                var to = citiesWithPayout[j];
                if (mapDefinition.TryGetPayout(from.PayoutIndex!.Value, to.PayoutIndex!.Value, out var payout) && payout > 0)
                {
                    entries.Add(new AdvisorPayoutEntry { FromCity = from.Name, ToCity = to.Name, Payout = payout });
                }
            }
        }

        return entries
            .OrderByDescending(e => e.Payout)
            .Take(30)
            .ToList();
    }

    private static string FormatVisibleCash(int cash, GameSettings settings)
    {
        if (!settings.KeepCashSecret || cash >= settings.AnnouncingCash)
        {
            return $"${cash:N0}";
        }

        var threshold = Math.Max(1, settings.AnnouncingCash);
        var dollarSigns = Math.Clamp((int)Math.Ceiling((double)(cash * 5) / threshold), 1, 5);
        return new string('$', dollarSigns);
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

        recentMessages.Add(new OpenAiChatMessage("system", BuildContextPayload(snapshot)));
        recentMessages.Add(new OpenAiChatMessage("user", userQuestion));
        return recentMessages;
    }

    private static string BuildSystemPrompt()
    {
        return string.Join(' ',
            "You are a concise in-game Rail Baron strategy advisor for Boxcars.",
            "Each conversation includes a system message with the current board state in labeled markdown sections, each containing JSON data blocks.",

            "CRITICAL RAIL BARON RULES YOU MUST UNDERSTAND:",
            "- Any railroad that is NOT owned by another player is PUBLIC and can be ridden with minimum fee. Only railroads owned by an OPPONENT cost higher use-fees.",
            "- EffectiveAccessPercent shows the player's real travel coverage: owned railroads PLUS all public (unowned) railroads. This is the number that matters for routing.",
            "- NetworkAccessPercent shows only owned-railroad coverage. Early game, EffectiveAccessPercent is nearly 100% because most railroads are still public.",
            "- The win condition requires accumulating the match's configured winning-cash threshold AND reaching your home city. CashToWin shows how far from that threshold.",

            "USE-FEE MECHANICS:",
            "- Each opponent-owned railroad used during a move incurs exactly one fee per owner (not per segment).",
            "- Fee rates are game-specific. Public and user-owned railroads use their configured public/private fees. Opponent-owned railroads use the game's configured unfriendly fee, which can change after all railroads are sold.",
            "- Grandfathered railroads (already being used when purchased) cost only $1,000 that trip.",
            "- If the player cannot pay fees, they must auction/sell railroads. If they still cannot pay, they are eliminated.",
            "- A safe cash buffer depends on the configured fee schedule and the density of opponent-owned railroads on likely routes.",

            "ENGINE TYPES AND UPGRADES:",
            "- Freight (starting engine): Roll 1 white die for movement (1-6 spaces).",
            "- Express uses 2 white dice for movement (2-12 spaces). On doubles, roll 1 bonus die for extra movement.",
            "- Superchief uses 2 white dice + 1 red die for movement (3-18 spaces). If the red die isn't fully used, the remainder carries as a bonus move.",
            "- Engine upgrades happen during the Purchase phase, using the game's configured upgrade prices.",

            "CONTEXT FIELDS:",
            "ControlledPlayer contains the asking player's exact cash, owned railroads, trip details, network coverage by region (owned and effective), declaration status, and CashToWin.",
            "Opponents lists each opponent with cash, owned railroads, trip, CashToWin, EffectiveAccessPercent, and per-region coverage — use these to assess threats and fee exposure.",
            "AvailableRailroads lists unowned railroads with price, connected city count, CashAfterPurchase, and projected OWNED coverage gain if purchased.",
            "Map contains all regions with destination probabilities, cities with individual destination probabilities, and all railroads with connected cities, pricing, and current Owner.",
            "HighValuePayouts lists the top 30 city-to-city payouts so you can evaluate trip value and destination desirability.",
            "StrategyText contains the player's self-described strategic preferences. Tailor your advice to align with this strategy when possible.",

            "STRATEGIC GUIDANCE:",
            "- When recommending purchases, always check CashAfterPurchase. Warn about bankruptcy risk only if CashAfterPurchase is very low AND opponents own many railroads (creating fee exposure).",
            "- Players need to maintain operating cash sufficient to be able to get to their next destination which is unknown. A safe cash buffer depends on the density of opponent-owned railroads on potential routes and the player's current location.",
            "- Factor in opponent railroad ownership density: more opponent-owned railroads = higher fee exposure = need more cash cushion.",
            "- Use city probabilities and payout data to evaluate which regions/cities are most valuable destination targets.",
            "- Factor engine upgrades into purchase timing: Express is cheap and high-impact early; Superchief is expensive but dominant late.",
            "- Consider the player's StrategyText preferences: if they favor monopoly, prioritize monopoly gains; if they favor access, prioritize coverage.",

            "Use these structured fields for precise analysis. Do not restate them verbatim unless the player asks.",
            "Advice is informational only: never claim to execute moves, buy railroads, roll dice, or resolve rules.",
            "If the context is insufficient, say what is missing.",
            "Prefer direct recommendations with concrete reasons tied to cash, fees, destination pressure, network coverage, railroad value, and turn phase.");
    }

    private static string BuildContextPayload(AdvisorContextSnapshot snapshot)
    {
        var sb = new System.Text.StringBuilder();

        sb.Append(CultureInfo.InvariantCulture, $"## Board State — Turn {snapshot.TurnNumber}, Phase: {snapshot.TurnPhase}");
        sb.AppendLine();
        sb.AppendLine(snapshot.BoardSituationSummary);
        sb.AppendLine();

        sb.Append(CultureInfo.InvariantCulture, $"## You: {snapshot.ControlledPlayerName}");
        sb.AppendLine();
        sb.AppendLine(snapshot.ControlledPlayerSummary);
        sb.AppendLine("```json");
        sb.AppendLine(JsonSerializer.Serialize(snapshot.ControlledPlayerContext, JsonOptions));
        sb.AppendLine("```");
        sb.AppendLine();

        if (snapshot.OpponentContexts.Count > 0)
        {
            sb.AppendLine("## Opponents");
            foreach (var (summary, opponent) in snapshot.OtherPlayerSummaries.Zip(snapshot.OpponentContexts))
            {
                sb.Append(CultureInfo.InvariantCulture, $"### {opponent.Name}");
                sb.AppendLine();
                sb.AppendLine(summary);
                sb.AppendLine("```json");
                sb.AppendLine(JsonSerializer.Serialize(opponent, JsonOptions));
                sb.AppendLine("```");
            }
            sb.AppendLine();
        }

        if (snapshot.AvailableRailroads.Count > 0)
        {
            sb.AppendLine("## Available Railroads For Purchase");
            sb.AppendLine("```json");
            sb.AppendLine(JsonSerializer.Serialize(snapshot.AvailableRailroads, JsonOptions));
            sb.AppendLine("```");
            sb.AppendLine();
        }

        sb.AppendLine("## Map Reference");
        sb.AppendLine("```json");
        sb.AppendLine(JsonSerializer.Serialize(snapshot.MapContext, JsonOptions));
        sb.AppendLine("```");
        sb.AppendLine();

        if (snapshot.HighValuePayouts.Count > 0)
        {
            sb.AppendLine("## Top City-to-City Payouts");
            sb.AppendLine("```json");
            sb.AppendLine(JsonSerializer.Serialize(snapshot.HighValuePayouts, JsonOptions));
            sb.AppendLine("```");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(snapshot.StrategyText))
        {
            sb.AppendLine("## Player Strategy");
            sb.AppendLine(snapshot.StrategyText);
        }

        return sb.ToString();
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
