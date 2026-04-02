using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Boxcars.Data;
using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Domain;
using Boxcars.GameEngine;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RailBaronGameEngine = global::Boxcars.Engine.Domain.GameEngine;

namespace Boxcars.Services;

public sealed class BotTurnService
{
    private const string AuctionStrategyPhase = "AuctionStrategy";
    private const string AuctionMaxBidOptionType = "AuctionMaxBid";
    private const string AuctionDropOutOptionId = "auction-drop-out";
    private const string AllAiAuctionResolutionSource = "AllAiAuctionResolution";
    private static readonly JsonSerializerOptions IndentedJsonSerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly UserDirectoryService _userDirectoryService;
    private readonly BotDecisionPromptBuilder _promptBuilder;
    private readonly OpenAiBotClient _openAiBotClient;
    private readonly GamePresenceService _gamePresenceService;
    private readonly NetworkCoverageService _networkCoverageService;
    private readonly BotOptions _botOptions;
    private readonly ILogger<BotTurnService> _logger;

    public BotTurnService(
        UserDirectoryService userDirectoryService,
        BotDecisionPromptBuilder promptBuilder,
        OpenAiBotClient openAiBotClient,
        GamePresenceService gamePresenceService,
        NetworkCoverageService networkCoverageService,
        IOptions<BotOptions> botOptions,
        ILogger<BotTurnService> logger)
    {
        _userDirectoryService = userDirectoryService;
        _promptBuilder = promptBuilder;
        _openAiBotClient = openAiBotClient;
        _gamePresenceService = gamePresenceService;
        _networkCoverageService = networkCoverageService;
        _botOptions = botOptions.Value;
        _logger = logger;
    }

    public GameSeatState? FindActiveSeatState(IReadOnlyList<GameSeatState> playerStates, string playerUserId)
    {
        ArgumentNullException.ThrowIfNull(playerStates);

        return playerStates.FirstOrDefault(playerState =>
            string.Equals(playerState.PlayerUserId, playerUserId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(playerState.BotControlStatus, BotControlStatuses.Active, StringComparison.OrdinalIgnoreCase)
            && playerState.BotControlClearedUtc is null);
    }

    public bool ClearActiveBotControl(IReadOnlyList<GameSeatState> playerStates, string playerUserId, string clearReason, string clearedStatus = BotControlStatuses.Cleared)
    {
        ArgumentNullException.ThrowIfNull(playerStates);

        var changed = false;
        var now = DateTimeOffset.UtcNow;

        foreach (var playerState in playerStates.Where(playerState =>
                     string.Equals(playerState.PlayerUserId, playerUserId, StringComparison.OrdinalIgnoreCase)
                     && string.Equals(playerState.BotControlStatus, BotControlStatuses.Active, StringComparison.OrdinalIgnoreCase)
                     && playerState.BotControlClearedUtc is null))
        {
            playerState.BotControlStatus = clearedStatus;
            playerState.BotControlClearReason = clearReason;
            playerState.BotControlClearedUtc = now;
            changed = true;
        }

        return changed;
    }

    public async Task EnsureBotSeatControlStatesAsync(
        string gameId,
        List<GameSeatState> playerStates,
        string controllerUserId,
        CancellationToken cancellationToken)
    {
        foreach (var playerState in playerStates)
        {
            if (string.IsNullOrWhiteSpace(playerState.PlayerUserId))
            {
                continue;
            }

            var strategyProfile = await _userDirectoryService.GetBotDefinitionAsync(playerState.PlayerUserId, cancellationToken);
            if (strategyProfile is null || !strategyProfile.IsBotUser)
            {
                if (SeatControllerModes.IsAiControlled(playerState.ControllerMode)
                    && !_gamePresenceService.IsUserConnected(gameId, playerState.PlayerUserId)
                    && (!PlayerControlRules.HasActiveBotControl(playerState)
                        || !string.Equals(PlayerControlRules.ResolveBotControllerMode(playerState), SeatControllerModes.AI, StringComparison.OrdinalIgnoreCase)))
                {
                    playerState.ControllerMode = SeatControllerModes.AI;
                    playerState.ControllerUserId = string.Empty;
                    playerState.BotControlActivatedUtc = DateTimeOffset.UtcNow;
                    playerState.BotControlClearedUtc = null;
                    playerState.BotControlStatus = BotControlStatuses.Active;
                    playerState.BotControlClearReason = string.Empty;
                }

                continue;
            }

            if (PlayerControlRules.HasActiveBotControl(playerState)
                && string.Equals(PlayerControlRules.ResolveBotControllerMode(playerState), SeatControllerModes.AI, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            playerState.ControllerMode = SeatControllerModes.AI;
            playerState.ControllerUserId = string.Empty;
            playerState.BotControlActivatedUtc = DateTimeOffset.UtcNow;
            playerState.BotControlClearedUtc = null;
            playerState.BotControlStatus = BotControlStatuses.Active;
            playerState.BotControlClearReason = string.Empty;
        }
    }

    public async Task<BotDecisionResolution?> ResolveDecisionAsync(
        string gameId,
        IReadOnlyList<GameSeatState> playerStates,
        string playerUserId,
        string targetPlayerName,
        string phase,
        int turnNumber,
        string authoritativeStatePayload,
        IReadOnlyList<BotLegalOption> legalOptions,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);

        var botControlContext = await ResolveBotControlContextAsync(gameId, playerStates, playerUserId, cancellationToken);
        if (botControlContext is null)
        {
            return null;
        }

        var botDefinition = botControlContext.Value.Definition;

        var context = new BotDecisionContext
        {
            GameId = gameId,
            PlayerUserId = playerUserId,
            TargetPlayerName = targetPlayerName,
            Phase = phase,
            TurnNumber = turnNumber,
            BotName = botDefinition.Name,
            StrategyText = botDefinition.StrategyText,
            GameStatePayload = authoritativeStatePayload,
            LegalOptions = legalOptions,
            TimeoutUtc = DateTimeOffset.UtcNow.AddSeconds(_botOptions.DecisionTimeoutSeconds)
        };

        if (legalOptions.Count == 0)
        {
            return null;
        }

        if (legalOptions.Count == 1)
        {
            return _promptBuilder.ResolveWithoutOpenAi(context, "Only one legal option was available.");
        }

        var systemPrompt = _promptBuilder.BuildSystemPrompt(context);
        var userPrompt = _promptBuilder.BuildUserPrompt(context);
        WritePurchaseReserveDebugPayload(context.Phase, context.TargetPlayerName, authoritativeStatePayload);
        var openAiResult = await _openAiBotClient.SelectOptionAsync(systemPrompt, userPrompt, cancellationToken);

        if (!openAiResult.Succeeded)
        {
#pragma warning disable CA1848 // Use the LoggerMessage delegates
            _logger.LogWarning(
                "OpenAI decision failed for phase {Phase} in game {GameId} for player {PlayerUserId}. Reason: {FailureReason}. Raw response: {RawResponse}",
                context.Phase,
                context.GameId,
                context.PlayerUserId,
                openAiResult.FailureReason,
                openAiResult.RawResponse);
#pragma warning restore CA1848 // Use the LoggerMessage delegates
            return _promptBuilder.ResolveWithoutOpenAi(
                context,
                openAiResult.TimedOut ? "OpenAI request timed out." : openAiResult.FailureReason ?? "OpenAI request failed.");
        }

        var selectedOption = _promptBuilder.FindOption(context, openAiResult.SelectedOptionId);
        if (selectedOption is null)
        {
#pragma warning disable CA1848 // Use the LoggerMessage delegates
            _logger.LogWarning(
                "OpenAI returned an invalid or stale option for phase {Phase} in game {GameId} for player {PlayerUserId}. Selected option: {SelectedOptionId}. Legal options: {LegalOptions}. Raw response: {RawResponse}",
                context.Phase,
                context.GameId,
                context.PlayerUserId,
                openAiResult.SelectedOptionId,
                string.Join(", ", context.LegalOptions.Select(option => option.OptionId)),
                openAiResult.RawResponse);
#pragma warning restore CA1848 // Use the LoggerMessage delegates
            return _promptBuilder.ResolveWithoutOpenAi(context, "OpenAI returned an invalid or stale option.");
        }

        return new BotDecisionResolution
        {
            GameId = context.GameId,
            PlayerUserId = context.PlayerUserId,
            Phase = context.Phase,
            SelectedOptionId = selectedOption.OptionId,
            Source = "OpenAI"
        };
    }

    private static void WritePurchaseReserveDebugPayload(string phase, string targetPlayerName, string authoritativeStatePayload)
    {
        if (!string.Equals(phase, "Purchase", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(authoritativeStatePayload))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(authoritativeStatePayload);
            if (!document.RootElement.TryGetProperty("PhaseContext", out var phaseContext)
                || !phaseContext.TryGetProperty("PurchaseRisk", out var purchaseRisk))
            {
                return;
            }

            var formattedPayload = JsonSerializer.Serialize(purchaseRisk, IndentedJsonSerializerOptions);
            Debug.WriteLine($"BotTurnService Purchase reserve model for '{targetPlayerName}': {formattedPayload}");
        }
        catch (JsonException exception)
        {
            Debug.WriteLine($"BotTurnService failed to parse purchase reserve model for '{targetPlayerName}': {exception.Message}");
        }
    }

    public async Task<PlayerAction?> CreateBotActionAsync(
        string gameId,
        List<GameSeatState> playerStates,
        RailBaronGameEngine gameEngine,
        MapDefinition mapDefinition,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentNullException.ThrowIfNull(gameEngine);
        ArgumentNullException.ThrowIfNull(mapDefinition);

        var snapshot = gameEngine.ToSnapshot();
        var playerSelections = GameSeatStateProjection.BuildSeatSelections(playerStates);

        if (gameEngine.CurrentTurn.AuctionState is not null)
        {
            return await CreateAuctionActionAsync(gameId, playerStates, gameEngine, mapDefinition, snapshot, playerSelections, cancellationToken);
        }

        return gameEngine.CurrentTurn.Phase switch
        {
            TurnPhase.HomeCityChoice => await CreateHomeCityChoiceActionAsync(gameId, playerStates, gameEngine, snapshot, playerSelections, cancellationToken),
            TurnPhase.HomeSwap => await CreateHomeSwapActionAsync(gameId, playerStates, gameEngine, snapshot, playerSelections, cancellationToken),
            TurnPhase.RegionChoice => await CreateRegionChoiceActionAsync(gameId, playerStates, gameEngine, mapDefinition, snapshot, playerSelections, cancellationToken),
            TurnPhase.Move => await CreateMoveActionAsync(gameId, playerStates, gameEngine, snapshot, playerSelections, cancellationToken),
            TurnPhase.Purchase => await CreatePurchaseActionAsync(gameId, playerStates, gameEngine, mapDefinition, snapshot, playerSelections, cancellationToken),
            TurnPhase.UseFees => await CreateForcedSaleActionAsync(gameId, playerStates, gameEngine, mapDefinition, playerSelections, cancellationToken),
            _ => null
        };
    }

    private Task<PlayerAction?> CreateHomeCityChoiceActionAsync(
        string gameId,
        IReadOnlyList<GameSeatState> playerStates,
        RailBaronGameEngine gameEngine,
        global::Boxcars.Engine.Persistence.GameState snapshot,
        IReadOnlyList<GamePlayerSelection> playerSelections,
        CancellationToken cancellationToken)
    {
        _ = gameId;
        _ = playerStates;
        _ = cancellationToken;

        var pendingHomeCityChoice = snapshot.Turn.PendingHomeCityChoice;
        if (pendingHomeCityChoice is null || pendingHomeCityChoice.EligibleCityNames.Count == 0)
        {
            return Task.FromResult<PlayerAction?>(null);
        }

        var selectedCityName = pendingHomeCityChoice.EligibleCityNames
            .OrderBy(cityName => cityName, StringComparer.OrdinalIgnoreCase)
            .First();
        var playerIndex = gameEngine.CurrentTurn.ActivePlayer.Index;

        return Task.FromResult<PlayerAction?>(new ChooseHomeCityAction
        {
            PlayerId = gameEngine.CurrentTurn.ActivePlayer.Name,
            PlayerIndex = playerIndex,
            ActorUserId = ResolveBotActorUserId(),
            SelectedCityName = selectedCityName,
            BotMetadata = CreateCollectiveAuctionMetadata()
        });
    }

    private Task<PlayerAction?> CreateHomeSwapActionAsync(
        string gameId,
        IReadOnlyList<GameSeatState> playerStates,
        RailBaronGameEngine gameEngine,
        global::Boxcars.Engine.Persistence.GameState snapshot,
        IReadOnlyList<GamePlayerSelection> playerSelections,
        CancellationToken cancellationToken)
    {
        _ = gameId;
        _ = playerStates;
        _ = snapshot;
        _ = playerSelections;
        _ = cancellationToken;

        var playerIndex = gameEngine.CurrentTurn.ActivePlayer.Index;
        return Task.FromResult<PlayerAction?>(new ResolveHomeSwapAction
        {
            PlayerId = gameEngine.CurrentTurn.ActivePlayer.Name,
            PlayerIndex = playerIndex,
            ActorUserId = ResolveBotActorUserId(),
            SwapHomeAndDestination = false,
            BotMetadata = CreateCollectiveAuctionMetadata()
        });
    }

    public async Task<PlayerAction?> TryResolveAllAiAuctionAsync(
        string gameId,
        List<GameSeatState> playerStates,
        RailBaronGameEngine gameEngine,
        MapDefinition mapDefinition,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentNullException.ThrowIfNull(gameEngine);
        ArgumentNullException.ThrowIfNull(mapDefinition);

        var auctionState = gameEngine.CurrentTurn.AuctionState;
        if (auctionState is null)
        {
            return null;
        }

        var railroad = gameEngine.Railroads.FirstOrDefault(candidate => candidate.Index == auctionState.RailroadIndex)
            ?? throw new InvalidOperationException($"Railroad '{auctionState.RailroadIndex}' was not found.");
        var playerSelections = GameSeatStateProjection.BuildSeatSelections(playerStates);
        var activeParticipantIndices = auctionState.Participants
            .Where(participant => participant.IsEligible && !participant.HasDroppedOut)
            .Select(participant => participant.PlayerIndex)
            .Distinct()
            .ToList();

        if (activeParticipantIndices.Count == 0)
        {
            return null;
        }

        var botControlContexts = new Dictionary<int, (GameSeatState PlayerState, BotStrategyDefinitionEntity Definition)>();
        foreach (var participantIndex in activeParticipantIndices)
        {
            var slotUserId = ResolveSlotUserId(playerSelections, participantIndex);
            var explicitSeatState = string.IsNullOrWhiteSpace(slotUserId)
                ? null
                : playerStates.FirstOrDefault(playerState =>
                    string.Equals(playerState.PlayerUserId, slotUserId, StringComparison.OrdinalIgnoreCase));

            if (explicitSeatState is null || !PlayerControlRules.HasActiveBotControl(explicitSeatState))
            {
                return null;
            }

            var botControlContext = await ResolveAuctionBidderContextAsync(gameId, playerStates, playerSelections, participantIndex, cancellationToken);
            if (botControlContext is null)
            {
                return null;
            }

            botControlContexts[participantIndex] = botControlContext.Value;
        }

        if (gameEngine.CurrentTurn.AuctionState is not { CurrentBidderPlayerIndex: int bidderPlayerIndex } liveAuctionState)
        {
            return null;
        }

        var snapshot = gameEngine.ToSnapshot();
        var bidderPlans = new Dictionary<int, (GameSeatState PlayerState, BotStrategyDefinitionEntity Definition, int MaximumBid)>();
        foreach (var participantIndex in activeParticipantIndices)
        {
            if (!botControlContexts.TryGetValue(participantIndex, out var botControlContext))
            {
                return null;
            }

            var plan = await ResolveAuctionBidPlanAsync(
                gameId,
                playerStates,
                gameEngine,
                mapDefinition,
                snapshot,
                playerSelections,
                liveAuctionState,
                participantIndex,
                botControlContext,
                cancellationToken);
            if (plan is null)
            {
                return null;
            }

            bidderPlans[participantIndex] = (plan.Value.PlayerState, botControlContext.Definition, plan.Value.MaximumBid);
        }

        if (!bidderPlans.TryGetValue(bidderPlayerIndex, out var currentBidderPlan))
        {
            return null;
        }

        var bidder = gameEngine.Players[bidderPlayerIndex];
        var maximumBidByPlayer = bidderPlans.ToDictionary(entry => entry.Key, entry => entry.Value.MaximumBid);
        var cashByPlayer = gameEngine.Players.ToDictionary(player => player.Index, player => player.Cash);
        var compressedBidAmount = SimulateCompressedAuctionBidAmount(liveAuctionState, maximumBidByPlayer, cashByPlayer);

        if (compressedBidAmount.HasValue)
        {
            gameEngine.SubmitAuctionBid(railroad, bidder, compressedBidAmount.Value);

            return new BidAction
            {
                PlayerId = bidder.Name,
                PlayerIndex = bidderPlayerIndex,
                ActorUserId = ResolveBotActorUserId(),
                RailroadIndex = auctionState.RailroadIndex,
                AmountBid = compressedBidAmount.Value,
                BotMetadata = CreateBotMetadata(
                    currentBidderPlan.PlayerState,
                    currentBidderPlan.Definition.Name,
                    AllAiAuctionResolutionSource,
                    isBotPlayer: currentBidderPlan.Definition.IsBotUser)
            };
        }

        gameEngine.DropOutOfAuction(railroad, bidder);
        return new AuctionDropOutAction
        {
            PlayerId = bidder.Name,
            PlayerIndex = bidderPlayerIndex,
            ActorUserId = ResolveBotActorUserId(),
            RailroadIndex = auctionState.RailroadIndex,
            BotMetadata = CreateBotMetadata(
                currentBidderPlan.PlayerState,
                currentBidderPlan.Definition.Name,
                AllAiAuctionResolutionSource,
                isBotPlayer: currentBidderPlan.Definition.IsBotUser)
        };
    }

    private static int? SimulateCompressedAuctionBidAmount(
        AuctionState auctionState,
        IReadOnlyDictionary<int, int> maximumBidByPlayer,
        IReadOnlyDictionary<int, int> cashByPlayer)
    {
        if (auctionState.CurrentBidderPlayerIndex is not int originalBidderPlayerIndex)
        {
            return null;
        }

        var droppedParticipants = auctionState.Participants
            .Where(participant => participant.HasDroppedOut || !participant.IsEligible)
            .Select(participant => participant.PlayerIndex)
            .ToHashSet();
        var currentBid = auctionState.CurrentBid;
        int? currentBidderPlayerIndex = originalBidderPlayerIndex;
        var leaderPlayerIndex = auctionState.LastBidderPlayerIndex;
        var lastBidByPlayer = new Dictionary<int, int>();

        while (currentBidderPlayerIndex is int bidderPlayerIndex)
        {
            var minimumBid = currentBid > 0
                ? currentBid + RailBaronGameEngine.AuctionBidIncrement
                : auctionState.StartingPrice;
            var maximumBid = maximumBidByPlayer.GetValueOrDefault(bidderPlayerIndex, 0);
            var availableCash = cashByPlayer.GetValueOrDefault(bidderPlayerIndex, 0);

            if (maximumBid >= minimumBid && availableCash >= minimumBid)
            {
                currentBid = minimumBid;
                leaderPlayerIndex = bidderPlayerIndex;
                lastBidByPlayer[bidderPlayerIndex] = minimumBid;
            }
            else
            {
                droppedParticipants.Add(bidderPlayerIndex);
            }

            var activeParticipants = auctionState.Participants
                .Where(participant => !droppedParticipants.Contains(participant.PlayerIndex))
                .Select(participant => participant.PlayerIndex)
                .ToList();
            if (leaderPlayerIndex is int resolvedLeaderPlayerIndex)
            {
                if (activeParticipants.All(participantIndex => participantIndex == resolvedLeaderPlayerIndex))
                {
                    break;
                }
            }
            else if (activeParticipants.Count == 0)
            {
                break;
            }

            var nextBidderPlayerIndex = GetNextSimulatedAuctionParticipantIndex(
                auctionState.Participants,
                droppedParticipants,
                bidderPlayerIndex);
            currentBidderPlayerIndex = nextBidderPlayerIndex;
        }

        return lastBidByPlayer.GetValueOrDefault(originalBidderPlayerIndex) > 0
            ? lastBidByPlayer[originalBidderPlayerIndex]
            : null;
    }

    private static int? GetNextSimulatedAuctionParticipantIndex(
        IReadOnlyList<AuctionParticipant> participants,
        HashSet<int> droppedParticipants,
        int currentPlayerIndex)
    {
        if (participants.Count == 0)
        {
            return null;
        }

        var currentPosition = participants
            .Select((participant, index) => new { participant, index })
            .Where(entry => entry.participant.PlayerIndex == currentPlayerIndex)
            .Select(entry => entry.index)
            .DefaultIfEmpty(-1)
            .First();

        if (currentPosition < 0)
        {
            return participants
                .Select(participant => participant.PlayerIndex)
                .Where(playerIndex => !droppedParticipants.Contains(playerIndex))
                .Cast<int?>()
                .FirstOrDefault();
        }

        for (var offset = 1; offset <= participants.Count; offset++)
        {
            var candidate = participants[(currentPosition + offset) % participants.Count];
            if (!droppedParticipants.Contains(candidate.PlayerIndex))
            {
                return candidate.PlayerIndex;
            }
        }

        return null;
    }

    private async Task<PlayerAction?> CreateRegionChoiceActionAsync(
        string gameId,
        IReadOnlyList<GameSeatState> playerStates,
        RailBaronGameEngine gameEngine,
        MapDefinition mapDefinition,
        global::Boxcars.Engine.Persistence.GameState snapshot,
        IReadOnlyList<GamePlayerSelection> playerSelections,
        CancellationToken cancellationToken)
    {
        var playerIndex = gameEngine.CurrentTurn.ActivePlayer.Index;
        var slotUserId = ResolveSlotUserId(playerSelections, playerIndex);
        if (string.IsNullOrWhiteSpace(slotUserId) || snapshot.Turn.PendingRegionChoice is null)
        {
            return null;
        }

        var botControlContext = await ResolveBotControlContextAsync(gameId, playerStates, slotUserId, cancellationToken);
        if (botControlContext is null)
        {
            return null;
        }

        var options = snapshot.Turn.PendingRegionChoice.EligibleRegionCodes
            .OrderBy(regionCode => regionCode, StringComparer.OrdinalIgnoreCase)
            .Select(regionCode => new BotLegalOption
            {
                OptionId = $"region:{regionCode}",
                OptionType = "Region",
                DisplayText = regionCode,
                Payload = regionCode
            })
            .ToList();

        var resolution = await ResolveDecisionAsync(
            gameId,
            playerStates,
            slotUserId,
            gameEngine.CurrentTurn.ActivePlayer.Name,
            "PickRegion",
            snapshot.TurnNumber,
            BuildOpenAiStatePayload(
                gameId,
                gameEngine,
                snapshot,
                mapDefinition,
                playerSelections,
                playerIndex,
                slotUserId,
                BuildRegionChoicePhaseContext(mapDefinition, snapshot.Turn.PendingRegionChoice)),
            options,
            cancellationToken);

        if (resolution is null)
        {
            return null;
        }

        return new ChooseDestinationRegionAction
        {
            PlayerId = gameEngine.CurrentTurn.ActivePlayer.Name,
            PlayerIndex = playerIndex,
            ActorUserId = ResolveBotActorUserId(),
            SelectedRegionCode = resolution.SelectedOptionId["region:".Length..],
            BotMetadata = CreateBotMetadata(botControlContext.Value.PlayerState, botControlContext.Value.Definition, resolution.Source, resolution.FallbackReason)
        };
    }

    private async Task<PlayerAction?> CreateMoveActionAsync(
        string gameId,
        IReadOnlyList<GameSeatState> playerStates,
        RailBaronGameEngine gameEngine,
        global::Boxcars.Engine.Persistence.GameState snapshot,
        IReadOnlyList<GamePlayerSelection> playerSelections,
        CancellationToken cancellationToken)
    {
        var player = gameEngine.CurrentTurn.ActivePlayer;
        var slotUserId = ResolveSlotUserId(playerSelections, player.Index);
        if (string.IsNullOrWhiteSpace(slotUserId))
        {
            return null;
        }

        var botControlContext = await ResolveBotControlContextAsync(gameId, playerStates, slotUserId, cancellationToken);
        if (botControlContext is null)
        {
            return null;
        }

        var snapshotPlayer = snapshot.Players[player.Index];
        if (player.Destination is null || string.IsNullOrWhiteSpace(snapshotPlayer.DestinationCityName))
        {
            return null;
        }

        var route = player.ActiveRoute;
        var routeProgressIndex = route is null
            ? 0
            : Math.Clamp(snapshotPlayer.RouteProgressIndex, 0, route.Segments.Count);

        if (route is null || routeProgressIndex >= route.Segments.Count)
        {
            route = gameEngine.SuggestRoute();
            routeProgressIndex = 0;
        }

        var remainingSegments = Math.Max(0, route.Segments.Count - routeProgressIndex);
        var steps = Math.Min(gameEngine.CurrentTurn.MovementRemaining, remainingSegments);
        if (steps <= 0)
        {
            return null;
        }

        var pointsTaken = route.NodeIds
            .Skip(routeProgressIndex)
            .Take(steps + 1)
            .ToList();
        var selectedSegmentKeys = route.Segments
            .Skip(routeProgressIndex)
            .Take(steps)
            .Select(segment => BuildSegmentKey(segment.FromNodeId, segment.ToNodeId, segment.RailroadIndex))
            .ToList();

        return new MoveAction
        {
            PlayerId = player.Name,
            PlayerIndex = player.Index,
            ActorUserId = ResolveBotActorUserId(),
            PointsTaken = pointsTaken,
            SelectedSegmentKeys = selectedSegmentKeys,
            BotMetadata = CreateBotMetadata(botControlContext.Value.PlayerState, botControlContext.Value.Definition, "SuggestedRoute")
        };
    }

    private async Task<PlayerAction?> CreatePurchaseActionAsync(
        string gameId,
        IReadOnlyList<GameSeatState> playerStates,
        RailBaronGameEngine gameEngine,
        MapDefinition mapDefinition,
        global::Boxcars.Engine.Persistence.GameState snapshot,
        IReadOnlyList<GamePlayerSelection> playerSelections,
        CancellationToken cancellationToken)
    {
        var player = gameEngine.CurrentTurn.ActivePlayer;
        var slotUserId = ResolveSlotUserId(playerSelections, player.Index);
        if (string.IsNullOrWhiteSpace(slotUserId))
        {
            return null;
        }

        var botControlContext = await ResolveBotControlContextAsync(gameId, playerStates, slotUserId, cancellationToken);
        if (botControlContext is null)
        {
            return null;
        }

        var actionsByOptionId = new Dictionary<string, PlayerAction>(StringComparer.Ordinal);
        var legalOptions = new List<BotLegalOption>();
        var snapshotPlayer = snapshot.Players[player.Index];
        var pendingFeeAmount = CalculatePendingFeeAmount(gameEngine, player.Index, snapshotPlayer, snapshot.Turn.RailroadsRiddenThisTurn);
        var currentCoverage = _networkCoverageService.BuildSnapshot(mapDefinition, player.OwnedRailroads.Select(railroad => railroad.Index));
        var purchaseReserve = BuildPurchaseReserveProfile(mapDefinition, player, currentCoverage, pendingFeeAmount);

        foreach (var railroad in gameEngine.Railroads
                     .Where(railroad => railroad.Owner is null && !railroad.IsPublic && railroad.PurchasePrice <= player.Cash)
                     .OrderByDescending(railroad => railroad.PurchasePrice)
                     .ThenBy(railroad => railroad.Name, StringComparer.OrdinalIgnoreCase))
        {
            var projectedCashAfterPurchase = player.Cash - railroad.PurchasePrice;
            var projectedFeeShortfall = Math.Max(0, pendingFeeAmount - projectedCashAfterPurchase);
            var reserveShortfall = Math.Max(0, purchaseReserve.RecommendedOperatingReserveCash - projectedCashAfterPurchase);
            var optionId = $"buy-railroad:{railroad.Index}";
            legalOptions.Add(new BotLegalOption
            {
                OptionId = optionId,
                OptionType = "PurchaseRailroad",
                DisplayText = BuildPurchaseDisplayText(
                    $"Buy {railroad.Name}",
                    railroad.PurchasePrice,
                    projectedCashAfterPurchase,
                    pendingFeeAmount,
                    projectedFeeShortfall,
                    purchaseReserve.RecommendedOperatingReserveCash,
                    reserveShortfall),
                Payload = railroad.Index.ToString(CultureInfo.InvariantCulture)
            });

            actionsByOptionId[optionId] = new PurchaseRailroadAction
            {
                PlayerId = player.Name,
                PlayerIndex = player.Index,
                ActorUserId = ResolveBotActorUserId(),
                RailroadIndex = railroad.Index,
                AmountPaid = railroad.PurchasePrice
            };
        }

        var currentEngineType = player.LocomotiveType;
        foreach (var engineType in Enum.GetValues<LocomotiveType>().Where(candidate => candidate > currentEngineType))
        {
            var amountPaid = RailBaronGameEngine.GetUpgradeCost(currentEngineType, engineType, gameEngine.Settings);
            if (amountPaid <= 0 || amountPaid > player.Cash)
            {
                continue;
            }

            var projectedCashAfterPurchase = player.Cash - amountPaid;
            var projectedFeeShortfall = Math.Max(0, pendingFeeAmount - projectedCashAfterPurchase);
            var reserveShortfall = Math.Max(0, purchaseReserve.RecommendedOperatingReserveCash - projectedCashAfterPurchase);
            var optionId = $"buy-engine:{engineType}";
            legalOptions.Add(new BotLegalOption
            {
                OptionId = optionId,
                OptionType = "BuyEngine",
                DisplayText = BuildPurchaseDisplayText(
                    $"Buy {engineType}",
                    amountPaid,
                    projectedCashAfterPurchase,
                    pendingFeeAmount,
                    projectedFeeShortfall,
                    purchaseReserve.RecommendedOperatingReserveCash,
                    reserveShortfall),
                Payload = engineType.ToString()
            });

            actionsByOptionId[optionId] = new BuyEngineAction
            {
                PlayerId = player.Name,
                PlayerIndex = player.Index,
                ActorUserId = ResolveBotActorUserId(),
                EngineType = engineType,
                AmountPaid = amountPaid
            };
        }

        const string declineOptionId = "decline-purchase";
        legalOptions.Add(new BotLegalOption
        {
            OptionId = declineOptionId,
            OptionType = "NoPurchase",
            DisplayText = "Buy nothing",
            Payload = string.Empty
        });
        actionsByOptionId[declineOptionId] = new DeclinePurchaseAction
        {
            PlayerId = player.Name,
            PlayerIndex = player.Index,
            ActorUserId = ResolveBotActorUserId()
        };

        var resolution = await ResolveDecisionAsync(
            gameId,
            playerStates,
            slotUserId,
            player.Name,
            "Purchase",
            snapshot.TurnNumber,
            BuildOpenAiStatePayload(
                gameId,
                gameEngine,
                snapshot,
                mapDefinition,
                playerSelections,
                player.Index,
                slotUserId,
                BuildPurchasePhaseContext(gameEngine, snapshot, mapDefinition, player.Index, player)),
            legalOptions,
            cancellationToken);

        if (resolution is null || !actionsByOptionId.TryGetValue(resolution.SelectedOptionId, out var action))
        {
            return null;
        }

        return action with
        {
            BotMetadata = CreateBotMetadata(botControlContext.Value.PlayerState, botControlContext.Value.Definition, resolution.Source, resolution.FallbackReason)
        };
    }

    private async Task<PlayerAction?> CreateAuctionActionAsync(
        string gameId,
        List<GameSeatState> playerStates,
        RailBaronGameEngine gameEngine,
        MapDefinition mapDefinition,
        global::Boxcars.Engine.Persistence.GameState snapshot,
        IReadOnlyList<GamePlayerSelection> playerSelections,
        CancellationToken cancellationToken)
    {
        var auctionState = gameEngine.CurrentTurn.AuctionState;
        if (auctionState?.CurrentBidderPlayerIndex is not int bidderPlayerIndex)
        {
            return null;
        }

        var slotUserId = ResolveSlotUserId(playerSelections, bidderPlayerIndex);
        if (string.IsNullOrWhiteSpace(slotUserId))
        {
            return null;
        }

        var botControlContext = await ResolveBotControlContextAsync(gameId, playerStates, slotUserId, cancellationToken);
        if (botControlContext is null)
        {
            return null;
        }

        var plan = await ResolveAuctionBidPlanAsync(
            gameId,
            playerStates,
            gameEngine,
            mapDefinition,
            snapshot,
            playerSelections,
            auctionState,
            bidderPlayerIndex,
            botControlContext.Value,
            cancellationToken);
        if (plan is null)
        {
            return null;
        }

        var bidder = gameEngine.Players[bidderPlayerIndex];
        var minimumBid = auctionState.CurrentBid > 0
            ? auctionState.CurrentBid + RailBaronGameEngine.AuctionBidIncrement
            : auctionState.StartingPrice;

        return BuildAuctionThresholdAction(
            plan.Value.PlayerState,
            botControlContext.Value.Definition,
            ResolveBotActorUserId(),
            bidder,
            bidderPlayerIndex,
            auctionState,
            minimumBid,
            plan.Value.MaximumBid,
            plan.Value.Source,
            plan.Value.FallbackReason);
    }

    private async Task<(GameSeatState PlayerState, BotStrategyDefinitionEntity Definition)?> ResolveAuctionBidderContextAsync(
        string gameId,
        IReadOnlyList<GameSeatState> playerStates,
        IReadOnlyList<GamePlayerSelection> playerSelections,
        int bidderPlayerIndex,
        CancellationToken cancellationToken)
    {
        var slotUserId = ResolveSlotUserId(playerSelections, bidderPlayerIndex);
        if (string.IsNullOrWhiteSpace(slotUserId))
        {
            return null;
        }

        var explicitSeatState = playerStates.FirstOrDefault(playerState =>
            string.Equals(playerState.PlayerUserId, slotUserId, StringComparison.OrdinalIgnoreCase));
        if (explicitSeatState is null || !PlayerControlRules.HasActiveBotControl(explicitSeatState))
        {
            return null;
        }

        return await ResolveBotControlContextAsync(gameId, playerStates, slotUserId, cancellationToken, requireExplicitAiControl: true);
    }

    private async Task<(GameSeatState PlayerState, int MaximumBid, string Source, string? FallbackReason)?> ResolveAuctionBidPlanAsync(
        string gameId,
        List<GameSeatState> playerStates,
        RailBaronGameEngine gameEngine,
        MapDefinition mapDefinition,
        global::Boxcars.Engine.Persistence.GameState snapshot,
        IReadOnlyList<GamePlayerSelection> playerSelections,
        AuctionState auctionState,
        int bidderPlayerIndex,
        (GameSeatState PlayerState, BotStrategyDefinitionEntity Definition) botControlContext,
        CancellationToken cancellationToken)
    {
        var slotUserId = ResolveSlotUserId(playerSelections, bidderPlayerIndex);
        if (string.IsNullOrWhiteSpace(slotUserId))
        {
            return null;
        }

        var bidder = gameEngine.Players[bidderPlayerIndex];
        var minimumBid = auctionState.CurrentBid > 0
            ? auctionState.CurrentBid + RailBaronGameEngine.AuctionBidIncrement
            : auctionState.StartingPrice;
        var playerState = botControlContext.PlayerState;

        if (bidder.Cash < minimumBid)
        {
            return (playerState, 0, "OnlyLegalChoice", null);
        }

        if (TryGetCachedAuctionMaximumBid(playerState, auctionState, snapshot.TurnNumber, out var cachedMaximumBid))
        {
            return (playerState, cachedMaximumBid, "AuctionPlan", null);
        }

        var legalOptions = BuildAuctionStrategyOptions(minimumBid, bidder.Cash);
        var resolution = await ResolveDecisionAsync(
            gameId,
            playerStates,
            slotUserId,
            bidder.Name,
            AuctionStrategyPhase,
            snapshot.TurnNumber,
            BuildOpenAiStatePayload(
                gameId,
                gameEngine,
                snapshot,
                mapDefinition,
                playerSelections,
                bidderPlayerIndex,
                slotUserId,
                BuildAuctionPhaseContext(gameEngine, snapshot.Turn.Auction!, minimumBid)),
            legalOptions,
            cancellationToken);
        if (resolution is null)
        {
            return null;
        }

        var maximumBid = TryParseAuctionMaximumBid(resolution.SelectedOptionId, out var selectedMaximumBid)
            ? selectedMaximumBid
            : 0;
        playerState = CacheAuctionPlan(playerStates, playerState, auctionState, snapshot.TurnNumber, maximumBid);
        return (playerState, maximumBid, resolution.Source, resolution.FallbackReason);
    }

    private async Task<PlayerAction?> CreateForcedSaleActionAsync(
        string gameId,
        IReadOnlyList<GameSeatState> playerStates,
        RailBaronGameEngine gameEngine,
        MapDefinition mapDefinition,
        IReadOnlyList<GamePlayerSelection> playerSelections,
        CancellationToken cancellationToken)
    {
        var player = gameEngine.CurrentTurn.ActivePlayer;
        var slotUserId = ResolveSlotUserId(playerSelections, player.Index);
        if (string.IsNullOrWhiteSpace(slotUserId) || gameEngine.CurrentTurn.ForcedSaleState is null)
        {
            return null;
        }

        var botControlContext = await ResolveBotControlContextAsync(gameId, playerStates, slotUserId, cancellationToken);
        if (botControlContext is null)
        {
            return null;
        }

        var currentCoverage = _networkCoverageService.BuildSnapshot(mapDefinition, player.OwnedRailroads.Select(railroad => railroad.Index));
        var bestCandidate = player.OwnedRailroads
            .Select(railroad =>
            {
                var projectedCoverage = _networkCoverageService.BuildProjectedSnapshotAfterSale(
                    mapDefinition,
                    player.OwnedRailroads.Select(ownedRailroad => ownedRailroad.Index),
                    railroad.Index);

                return new
                {
                    Railroad = railroad,
                    Evaluation = new SellImpactEvaluation
                    {
                        RailroadIndex = railroad.Index,
                        RailroadId = railroad.Name,
                        AccessDeltaScore = (int)Math.Round((projectedCoverage.AccessibleDestinationPercent - currentCoverage.AccessibleDestinationPercent) * 10m, MidpointRounding.AwayFromZero),
                        MonopolyDeltaScore = (int)Math.Round((projectedCoverage.MonopolyDestinationPercent - currentCoverage.MonopolyDestinationPercent) * 10m, MidpointRounding.AwayFromZero),
                        TieBreakerKey = railroad.Name,
                        CompositeRank = string.Concat(
                            projectedCoverage.AccessibleDestinationPercent.ToString("00000.0", CultureInfo.InvariantCulture),
                            ":",
                            projectedCoverage.MonopolyDestinationPercent.ToString("00000.0", CultureInfo.InvariantCulture),
                            ":",
                            railroad.Name)
                    }
                };
            })
            .OrderByDescending(candidate => candidate.Evaluation.AccessDeltaScore)
            .ThenByDescending(candidate => candidate.Evaluation.MonopolyDeltaScore)
            .ThenBy(candidate => candidate.Evaluation.TieBreakerKey, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (bestCandidate is null)
        {
            return null;
        }

        if (HasEligibleForcedSaleBidder(gameEngine, player, bestCandidate.Railroad))
        {
            return new StartAuctionAction
            {
                PlayerId = player.Name,
                PlayerIndex = player.Index,
                ActorUserId = ResolveBotActorUserId(),
                RailroadIndex = bestCandidate.Railroad.Index,
                BotMetadata = CreateBotMetadata(botControlContext.Value.PlayerState, botControlContext.Value.Definition, "DeterministicAuction")
            };
        }

        return new SellRailroadAction
        {
            PlayerId = player.Name,
            PlayerIndex = player.Index,
            ActorUserId = ResolveBotActorUserId(),
            RailroadIndex = bestCandidate.Railroad.Index,
            AmountReceived = bestCandidate.Railroad.PurchasePrice / 2,
            BotMetadata = CreateBotMetadata(botControlContext.Value.PlayerState, botControlContext.Value.Definition, "DeterministicSell")
        };
    }

    private static bool HasEligibleForcedSaleBidder(RailBaronGameEngine gameEngine, Player seller, Railroad railroad)
    {
        var startingPrice = railroad.PurchasePrice / 2;
        return gameEngine.Players.Any(player =>
            player != seller
            && player.IsActive
            && !player.IsBankrupt
            && player.Cash >= startingPrice);
    }

    private async Task<(GameSeatState PlayerState, BotStrategyDefinitionEntity Definition)?> ResolveBotControlContextAsync(
        string gameId,
        IReadOnlyList<GameSeatState> playerStates,
        string playerUserId,
        CancellationToken cancellationToken,
        bool requireExplicitAiControl = false)
    {
        var playerState = FindActiveSeatState(playerStates, playerUserId);
        var isConnected = _gamePresenceService.IsUserConnected(gameId, playerUserId);

        if (playerState is null)
        {
            return null;
        }

        var resolvedControllerMode = PlayerControlRules.ResolveBotControllerMode(playerState);
        if (requireExplicitAiControl && !SeatControllerModes.IsAiControlled(resolvedControllerMode))
        {
            return null;
        }

        resolvedControllerMode ??= SeatControllerModes.AI;
        var dedicatedBotSeatDefinition = await _userDirectoryService.GetBotDefinitionAsync(playerUserId, cancellationToken);
        var isDedicatedBotPlayer = dedicatedBotSeatDefinition?.IsBotUser == true;
        var botDefinition = isDedicatedBotPlayer
            ? await _userDirectoryService.GetBotDefinitionAsync(playerUserId, cancellationToken)
            : await _userDirectoryService.GetAutomationProfileAsync(playerUserId, cancellationToken);
        if (botDefinition is null)
        {
            ClearActiveBotControl(playerStates, playerUserId, "The assigned bot definition no longer exists.", BotControlStatuses.MissingDefinition);
            return null;
        }

        if (SeatControllerModes.IsAiControlled(resolvedControllerMode)
            && isConnected)
        {
            ClearActiveBotControl(playerStates, playerUserId, "Player reconnected.", BotControlStatuses.Cleared);
            return null;
        }

        playerState.ControllerMode = resolvedControllerMode;
        playerState.ControllerUserId = string.Empty;
        return (playerState, botDefinition);
    }

    private string ResolveBotActorUserId()
    {
        return _botOptions.ServerActorUserId;
    }

    private static string? ResolveSlotUserId(IReadOnlyList<GamePlayerSelection> selections, int playerIndex)
    {
        return playerIndex >= 0 && playerIndex < selections.Count
            ? selections[playerIndex].UserId
            : null;
    }

    private string BuildOpenAiStatePayload(
        string gameId,
        RailBaronGameEngine gameEngine,
        global::Boxcars.Engine.Persistence.GameState snapshot,
        MapDefinition mapDefinition,
        IReadOnlyList<GamePlayerSelection> playerSelections,
        int targetPlayerIndex,
        string? targetPlayerUserId,
        object phaseContext)
    {
        var targetPlayerState = snapshot.Players[targetPlayerIndex];
        var regionProbabilityLookup = BuildRegionProbabilityLookup(mapDefinition);
        var otherOwnedRailroadIndices = snapshot.Players
            .Where((_, playerIndex) => playerIndex != targetPlayerIndex)
            .SelectMany(player => player.OwnedRailroadIndices)
            .Distinct()
            .ToArray();
        var targetOwnedRailroads = BuildRailroadSnapshots(gameEngine, targetPlayerState.OwnedRailroadIndices);
        var targetCoverage = _networkCoverageService.BuildSnapshot(mapDefinition, targetPlayerState.OwnedRailroadIndices);
        var targetReachableCoverage = _networkCoverageService.BuildSnapshotIncludingPublicRailroads(
            mapDefinition,
            targetPlayerState.OwnedRailroadIndices,
            otherOwnedRailroadIndices);

        return JsonSerializer.Serialize(new
        {
            GameId = gameId,
            snapshot.GameStatus,
            snapshot.TurnNumber,
            CurrentPhase = snapshot.Turn.Phase,
            ActivePlayer = new
            {
                snapshot.ActivePlayerIndex,
                PlayerName = snapshot.ActivePlayerIndex >= 0 && snapshot.ActivePlayerIndex < snapshot.Players.Count
                    ? snapshot.Players[snapshot.ActivePlayerIndex].Name
                    : string.Empty
            },
            TargetPlayer = new
            {
                PlayerIndex = targetPlayerIndex,
                PlayerName = targetPlayerState.Name,
                UserId = targetPlayerUserId,
                ExistingOwnedRailroads = targetOwnedRailroads,
                OwnedNetworkCoverage = targetCoverage,
                ReachableNetworkCoverageIncludingPublicRailroads = targetReachableCoverage
            },
            Players = snapshot.Players
                .Select((playerState, playerIndex) => BuildPlayerMetadata(gameEngine, mapDefinition, playerSelections, playerState, playerIndex, targetPlayerIndex))
                .ToList(),
            RailroadMarket = new
            {
                SoldRailroads = gameEngine.Railroads
                    .Where(railroad => !railroad.IsPublic && railroad.Owner is not null)
                    .OrderBy(railroad => railroad.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(railroad => new
                    {
                        railroad.Index,
                        railroad.Name,
                        railroad.ShortName,
                        railroad.PurchasePrice,
                        OwnerPlayerIndex = railroad.Owner!.Index,
                        OwnerPlayerName = railroad.Owner.Name
                    })
                    .ToList(),
                AvailableRailroads = gameEngine.Railroads
                    .Where(railroad => !railroad.IsPublic && railroad.Owner is null)
                    .OrderByDescending(railroad => railroad.PurchasePrice)
                    .ThenBy(railroad => railroad.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(railroad => new
                    {
                        railroad.Index,
                        railroad.Name,
                        railroad.ShortName,
                        railroad.PurchasePrice
                    })
                    .ToList(),
                PublicRailroads = gameEngine.Railroads
                    .Where(railroad => railroad.IsPublic)
                    .OrderBy(railroad => railroad.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(railroad => new
                    {
                        railroad.Index,
                        railroad.Name,
                        railroad.ShortName,
                        railroad.PurchasePrice
                    })
                    .ToList(),
                TargetPlayerExistingOwnedRailroads = targetOwnedRailroads,
                snapshot.AllRailroadsSold
            },
            MapPercentages = new
            {
                Regions = mapDefinition.Regions
                    .OrderBy(region => region.Code, StringComparer.OrdinalIgnoreCase)
                    .Select(region => new
                    {
                        region.Index,
                        region.Code,
                        region.Name,
                        ProbabilityPercent = regionProbabilityLookup.TryGetValue(region.Code, out var probabilityPercent)
                            ? probabilityPercent
                            : 0m
                    })
                    .ToList(),
                Cities = BuildCityProbabilityRows(mapDefinition, regionProbabilityLookup)
            },
            TurnState = new
            {
                snapshot.Turn.Phase,
                snapshot.Turn.MovementAllowance,
                snapshot.Turn.MovementRemaining,
                snapshot.Turn.BonusRollAvailable,
                snapshot.Turn.PendingFeeAmount,
                snapshot.Turn.SelectedRailroadForSaleIndex,
                snapshot.Turn.RailroadsRiddenThisTurn,
                snapshot.Turn.DiceResult,
                snapshot.Turn.ArrivalResolution,
                snapshot.Turn.ForcedSale,
                snapshot.Turn.Auction,
                snapshot.Turn.PendingRegionChoice
            },
            PhaseContext = phaseContext
        });
    }

    private static object BuildRegionChoicePhaseContext(
        MapDefinition mapDefinition,
        global::Boxcars.Engine.Persistence.PendingRegionChoiceTurnState pendingRegionChoice)
    {
        var regionProbabilityLookup = BuildRegionProbabilityLookup(mapDefinition);

        return new
        {
            CurrentCity = BuildCityReference(mapDefinition, pendingRegionChoice.CurrentCityName),
            pendingRegionChoice.CurrentRegionCode,
            pendingRegionChoice.TriggeredByInitialRegionCode,
            EligibleRegions = pendingRegionChoice.EligibleRegionCodes
                .OrderBy(regionCode => regionCode, StringComparer.OrdinalIgnoreCase)
                .Select(regionCode => new
                {
                    RegionCode = regionCode,
                    EligibleCityCount = pendingRegionChoice.EligibleCityCountsByRegion.TryGetValue(regionCode, out var eligibleCityCount)
                        ? eligibleCityCount
                        : 0,
                    RegionProbabilityPercent = regionProbabilityLookup.TryGetValue(regionCode, out var probabilityPercent)
                        ? probabilityPercent
                        : 0m
                })
                .ToList()
        };
    }

    private object BuildPurchasePhaseContext(
        RailBaronGameEngine gameEngine,
        global::Boxcars.Engine.Persistence.GameState snapshot,
        MapDefinition mapDefinition,
        int playerIndex,
        Player player)
    {
        var ownedRailroadIndices = player.OwnedRailroads.Select(railroad => railroad.Index).ToArray();
        var currentCoverage = _networkCoverageService.BuildSnapshot(mapDefinition, ownedRailroadIndices);
        var snapshotPlayer = snapshot.Players[playerIndex];
        var pendingFeeAmount = CalculatePendingFeeAmount(gameEngine, playerIndex, snapshotPlayer, snapshot.Turn.RailroadsRiddenThisTurn);
        var purchaseReserve = BuildPurchaseReserveProfile(mapDefinition, player, currentCoverage, pendingFeeAmount);

        return new
        {
            CurrentCity = BuildCityReference(mapDefinition, player.CurrentCity.Name),
            DestinationCity = BuildCityReference(mapDefinition, player.Destination?.Name),
            player.Cash,
            Engine = player.LocomotiveType.ToString(),
            PendingFeeAmount = pendingFeeAmount,
            CashAfterFeesWithoutPurchase = player.Cash - pendingFeeAmount,
            ImmediateForcedSaleWithoutPurchase = player.Cash < pendingFeeAmount,
            PurchaseRisk = new
            {
                purchaseReserve.CurrentRegionCode,
                purchaseReserve.RecommendedOperatingReserveCash,
                purchaseReserve.EngineRiskMultiplier,
                purchaseReserve.WeightedReservePressure,
                TopRiskRegions = purchaseReserve.RegionPressures
                    .Take(4)
                    .Select(pressure => new
                    {
                        pressure.RegionCode,
                        pressure.RegionName,
                        pressure.ProbabilityPercent,
                        pressure.OwnedAccessPercent,
                        pressure.AccessGapPercent,
                        pressure.RegionHopCount,
                        pressure.HopMultiplier,
                        pressure.WeightedReservePressure
                    })
                    .ToList()
            },
            AffordableRailroadOptions = gameEngine.Railroads
                .Where(railroad => railroad.Owner is null && !railroad.IsPublic && railroad.PurchasePrice <= player.Cash)
                .OrderByDescending(railroad => railroad.PurchasePrice)
                .ThenBy(railroad => railroad.Name, StringComparer.OrdinalIgnoreCase)
                .Select(railroad =>
                {
                    var projectedCoverage = _networkCoverageService.BuildProjectedSnapshot(mapDefinition, ownedRailroadIndices, railroad.Index);
                    var cashAfterPurchase = player.Cash - railroad.PurchasePrice;
                    var feeShortfall = Math.Max(0, pendingFeeAmount - cashAfterPurchase);
                    var reserveShortfall = Math.Max(0, purchaseReserve.RecommendedOperatingReserveCash - cashAfterPurchase);
                    return new
                    {
                        railroad.Index,
                        railroad.Name,
                        railroad.ShortName,
                        railroad.PurchasePrice,
                        CashAfterPurchase = cashAfterPurchase,
                        CashAfterFees = cashAfterPurchase - pendingFeeAmount,
                        WouldTriggerForcedSale = feeShortfall > 0,
                        FeeShortfall = feeShortfall,
                        RecommendedOperatingReserveCash = purchaseReserve.RecommendedOperatingReserveCash,
                        PreservesRecommendedOperatingReserve = reserveShortfall == 0,
                        OperatingReserveShortfall = reserveShortfall,
                        CashVsRecommendedOperatingReserve = cashAfterPurchase - purchaseReserve.RecommendedOperatingReserveCash,
                        CurrentAccessibleDestinationPercent = currentCoverage.AccessibleDestinationPercent,
                        ProjectedAccessibleDestinationPercent = projectedCoverage.AccessibleDestinationPercent,
                        AccessDeltaPercent = Math.Round(projectedCoverage.AccessibleDestinationPercent - currentCoverage.AccessibleDestinationPercent, 1, MidpointRounding.AwayFromZero),
                        CurrentMonopolyDestinationPercent = currentCoverage.MonopolyDestinationPercent,
                        ProjectedMonopolyDestinationPercent = projectedCoverage.MonopolyDestinationPercent,
                        MonopolyDeltaPercent = Math.Round(projectedCoverage.MonopolyDestinationPercent - currentCoverage.MonopolyDestinationPercent, 1, MidpointRounding.AwayFromZero),
                        ProjectedRegionCoverage = projectedCoverage.RegionAccess
                    };
                })
                .ToList(),
            EngineUpgradeOptions = Enum.GetValues<LocomotiveType>()
                .Where(candidate => candidate > player.LocomotiveType)
                .Select(engineType =>
                {
                    var upgradeCost = RailBaronGameEngine.GetUpgradeCost(player.LocomotiveType, engineType, gameEngine.Settings);
                    var cashAfterPurchase = player.Cash - upgradeCost;
                    var feeShortfall = Math.Max(0, pendingFeeAmount - cashAfterPurchase);
                    var reserveShortfall = Math.Max(0, purchaseReserve.RecommendedOperatingReserveCash - cashAfterPurchase);
                    return new
                    {
                        EngineType = engineType.ToString(),
                        UpgradeCost = upgradeCost,
                        IsAffordable = upgradeCost > 0 && upgradeCost <= player.Cash,
                        CashAfterPurchase = cashAfterPurchase,
                        CashAfterFees = cashAfterPurchase - pendingFeeAmount,
                        WouldTriggerForcedSale = feeShortfall > 0,
                        FeeShortfall = feeShortfall,
                        RecommendedOperatingReserveCash = purchaseReserve.RecommendedOperatingReserveCash,
                        PreservesRecommendedOperatingReserve = reserveShortfall == 0,
                        OperatingReserveShortfall = reserveShortfall,
                        CashVsRecommendedOperatingReserve = cashAfterPurchase - purchaseReserve.RecommendedOperatingReserveCash
                    };
                })
                .Where(option => option.UpgradeCost > 0)
                .ToList(),
            OwnedRailroads = player.OwnedRailroads
                .OrderBy(railroad => railroad.Name, StringComparer.OrdinalIgnoreCase)
                .Select(railroad =>
                {
                    var projectedCoverage = _networkCoverageService.BuildProjectedSnapshotAfterSale(mapDefinition, ownedRailroadIndices, railroad.Index);
                    return new
                    {
                        railroad.Index,
                        railroad.Name,
                        SaleValue = railroad.PurchasePrice / 2,
                        AccessDeltaPercentAfterSale = Math.Round(projectedCoverage.AccessibleDestinationPercent - currentCoverage.AccessibleDestinationPercent, 1, MidpointRounding.AwayFromZero),
                        MonopolyDeltaPercentAfterSale = Math.Round(projectedCoverage.MonopolyDestinationPercent - currentCoverage.MonopolyDestinationPercent, 1, MidpointRounding.AwayFromZero)
                    };
                })
                .ToList()
        };
    }

    private static string BuildPurchaseDisplayText(
        string label,
        int purchasePrice,
        int cashAfterPurchase,
        int pendingFeeAmount,
        int projectedFeeShortfall,
        int recommendedOperatingReserveCash,
        int reserveShortfall)
    {
        var currencyCulture = CultureInfo.InvariantCulture;
        var reserveText = reserveShortfall > 0
            ? $", reserve target {recommendedOperatingReserveCash.ToString("C0", currencyCulture)}, operating cushion short by {reserveShortfall.ToString("C0", currencyCulture)}"
            : $", reserve target {recommendedOperatingReserveCash.ToString("C0", currencyCulture)}, operating cushion preserved";

        if (pendingFeeAmount <= 0)
        {
            return $"{label} for {purchasePrice.ToString("C0", currencyCulture)} (cash after purchase {cashAfterPurchase.ToString("C0", currencyCulture)}{reserveText})";
        }

        if (projectedFeeShortfall > 0)
        {
            return $"{label} for {purchasePrice.ToString("C0", currencyCulture)} (cash after purchase {cashAfterPurchase.ToString("C0", currencyCulture)}, fees due {pendingFeeAmount.ToString("C0", currencyCulture)}, forced-sale shortfall {projectedFeeShortfall.ToString("C0", currencyCulture)}{reserveText})";
        }

        return $"{label} for {purchasePrice.ToString("C0", currencyCulture)} (cash after purchase {cashAfterPurchase.ToString("C0", currencyCulture)}, fees due {pendingFeeAmount.ToString("C0", currencyCulture)}, fees still covered{reserveText})";
    }

    private static PurchaseReserveProfile BuildPurchaseReserveProfile(
        MapDefinition mapDefinition,
        Player player,
        NetworkCoverageSnapshot currentCoverage,
        int pendingFeeAmount)
    {
        var currentRegionCode = player.CurrentCity.RegionCode;
        var regionProbabilityLookup = BuildRegionProbabilityLookup(mapDefinition);
        var ownedRegionAccessByCode = currentCoverage.RegionAccess
            .ToDictionary(region => region.RegionCode, region => region.AccessibleDestinationPercent, StringComparer.OrdinalIgnoreCase);
        var regionHopCounts = BuildRegionHopCounts(mapDefinition, currentRegionCode);
        var engineRiskMultiplier = GetPurchaseReserveEngineMultiplier(player.LocomotiveType);

        var regionPressures = mapDefinition.Regions
            .Where(region => !string.Equals(region.Code, currentRegionCode, StringComparison.OrdinalIgnoreCase))
            .Select(region =>
            {
                var probabilityPercent = regionProbabilityLookup.TryGetValue(region.Code, out var probabilityValue)
                    ? probabilityValue
                    : 0m;
                var ownedAccessPercent = ownedRegionAccessByCode.TryGetValue(region.Code, out var accessValue)
                    ? accessValue
                    : 0m;
                var accessGapPercent = Math.Max(0m, 100m - ownedAccessPercent);
                var regionHopCount = regionHopCounts.TryGetValue(region.Code, out var hopCount)
                    ? hopCount
                    : Math.Max(1, mapDefinition.Regions.Count);
                var hopMultiplier = 1m + Math.Max(0, regionHopCount - 1) * 0.35m;
                var weightedReservePressure = Math.Round(
                    probabilityPercent * accessGapPercent / 100m * hopMultiplier,
                    2,
                    MidpointRounding.AwayFromZero);

                return new PurchaseReservePressure(
                    region.Code,
                    region.Name,
                    probabilityPercent,
                    ownedAccessPercent,
                    accessGapPercent,
                    regionHopCount,
                    hopMultiplier,
                    weightedReservePressure);
            })
            .OrderByDescending(pressure => pressure.WeightedReservePressure)
            .ThenByDescending(pressure => pressure.ProbabilityPercent)
            .ThenBy(pressure => pressure.RegionCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var weightedReservePressure = regionPressures.Sum(pressure => pressure.WeightedReservePressure);
        var engineReserveFloor = GetPurchaseReserveFloor(player.LocomotiveType);
        var variableReserve = weightedReservePressure * 55m * engineRiskMultiplier;
        var recommendedOperatingReserveCash = RoundUpToNearest(
            decimal.ToInt32(Math.Ceiling(engineReserveFloor + pendingFeeAmount + variableReserve)),
            500);

        return new PurchaseReserveProfile(
            currentRegionCode,
            recommendedOperatingReserveCash,
            engineRiskMultiplier,
            weightedReservePressure,
            regionPressures);
    }

    private static decimal GetPurchaseReserveEngineMultiplier(LocomotiveType locomotiveType)
    {
        return locomotiveType switch
        {
            LocomotiveType.Freight => 1.3m,
            LocomotiveType.Express => 1.1m,
            LocomotiveType.Superchief => 0.9m,
            _ => 1m
        };
    }

    private static int GetPurchaseReserveFloor(LocomotiveType locomotiveType)
    {
        return locomotiveType switch
        {
            LocomotiveType.Freight => 10_000,
            LocomotiveType.Express => 8_000,
            LocomotiveType.Superchief => 6_000,
            _ => 8_000
        };
    }

    private static Dictionary<string, int> BuildRegionHopCounts(MapDefinition mapDefinition, string originRegionCode)
    {
        var regionCodeByIndex = mapDefinition.Regions
            .ToDictionary(region => region.Index, region => region.Code);
        var adjacencyByRegion = mapDefinition.Regions
            .ToDictionary(
                region => region.Code,
                _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

        foreach (var segment in mapDefinition.RailroadRouteSegments)
        {
            if (!regionCodeByIndex.TryGetValue(segment.StartRegionIndex, out var startRegionCode)
                || !regionCodeByIndex.TryGetValue(segment.EndRegionIndex, out var endRegionCode)
                || string.Equals(startRegionCode, endRegionCode, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            adjacencyByRegion[startRegionCode].Add(endRegionCode);
            adjacencyByRegion[endRegionCode].Add(startRegionCode);
        }

        var hopCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [originRegionCode] = 0
        };
        var queue = new Queue<string>();
        queue.Enqueue(originRegionCode);

        while (queue.Count > 0)
        {
            var regionCode = queue.Dequeue();
            var currentHopCount = hopCounts[regionCode];

            if (!adjacencyByRegion.TryGetValue(regionCode, out var neighbors))
            {
                continue;
            }

            foreach (var neighbor in neighbors)
            {
                if (hopCounts.ContainsKey(neighbor))
                {
                    continue;
                }

                hopCounts[neighbor] = currentHopCount + 1;
                queue.Enqueue(neighbor);
            }
        }

        return hopCounts;
    }

    private static int RoundUpToNearest(int amount, int increment)
    {
        if (amount <= 0)
        {
            return 0;
        }

        return ((amount + increment - 1) / increment) * increment;
    }

    private static int CalculatePendingFeeAmount(
        RailBaronGameEngine gameEngine,
        int playerIndex,
        global::Boxcars.Engine.Persistence.PlayerState snapshotPlayer,
        IEnumerable<int> railroadsRiddenThisTurn)
    {
        var feeBuckets = new Dictionary<int, PendingFeeBucket>();

        foreach (var railroadIndex in railroadsRiddenThisTurn)
        {
            var railroad = gameEngine.Railroads.FirstOrDefault(candidate => candidate.Index == railroadIndex);
            if (railroad is null)
            {
                continue;
            }

            var usesBaseRate = railroad.Owner is null || railroad.Owner.Index == playerIndex;
            var ownerKey = usesBaseRate ? -1 : railroad.Owner!.Index;

            if (!feeBuckets.TryGetValue(ownerKey, out var bucket))
            {
                bucket = new PendingFeeBucket(usesBaseRate ? null : railroad.Owner);
                feeBuckets[ownerKey] = bucket;
            }

            if (!usesBaseRate && !snapshotPlayer.GrandfatheredRailroadIndices.Contains(railroad.Index))
            {
                bucket.RequiresFullOwnerRate = true;
            }
        }

        if (feeBuckets.Count == 0)
        {
            return 0;
        }

        var opponentRate = gameEngine.AllRailroadsSold ? 10000 : 5000;
        return feeBuckets.Values.Sum(bucket => bucket.Owner is null
            ? 1000
            : bucket.RequiresFullOwnerRate ? opponentRate : 1000);
    }

    private sealed class PendingFeeBucket(Player? owner)
    {
        public Player? Owner { get; } = owner;

        public bool RequiresFullOwnerRate { get; set; }
    }

    private static List<BotLegalOption> BuildAuctionStrategyOptions(int minimumBid, int availableCash)
    {
        var legalOptions = new List<BotLegalOption>();

        for (var maximumBid = minimumBid; maximumBid <= availableCash; maximumBid += RailBaronGameEngine.AuctionBidIncrement)
        {
            legalOptions.Add(new BotLegalOption
            {
                OptionId = BuildAuctionMaximumBidOptionId(maximumBid),
                OptionType = AuctionMaxBidOptionType,
                DisplayText = $"Stay in until the bid exceeds {maximumBid.ToString("C0", CultureInfo.InvariantCulture)}. Bid {minimumBid.ToString("C0", CultureInfo.InvariantCulture)} now.",
                Payload = maximumBid.ToString(CultureInfo.InvariantCulture)
            });
        }

        legalOptions.Add(new BotLegalOption
        {
            OptionId = AuctionDropOutOptionId,
            OptionType = "AuctionDropOut",
            DisplayText = $"Drop out now instead of bidding {minimumBid.ToString("C0", CultureInfo.InvariantCulture)}.",
            Payload = string.Empty
        });

        return legalOptions;
    }

    private static string BuildAuctionMaximumBidOptionId(int maximumBid)
    {
        return $"auction-cap:{maximumBid.ToString(CultureInfo.InvariantCulture)}";
    }

    private static bool TryParseAuctionMaximumBid(string? optionId, out int maximumBid)
    {
        maximumBid = 0;
        if (string.IsNullOrWhiteSpace(optionId)
            || !optionId.StartsWith("auction-cap:", StringComparison.Ordinal))
        {
            return false;
        }

        return int.TryParse(optionId["auction-cap:".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out maximumBid);
    }

    private static bool TryGetCachedAuctionMaximumBid(GameSeatState playerState, AuctionState auctionState, int turnNumber, out int maximumBid)
    {
        maximumBid = 0;

        if (playerState.AuctionPlanMaximumBid is null
            || playerState.AuctionPlanTurnNumber != turnNumber
            || playerState.AuctionPlanRailroadIndex != auctionState.RailroadIndex
            || playerState.AuctionPlanStartingPrice != auctionState.StartingPrice)
        {
            return false;
        }

        maximumBid = playerState.AuctionPlanMaximumBid.Value;
        return true;
    }

    private static PlayerAction BuildAuctionThresholdAction(
        GameSeatState playerState,
        BotStrategyDefinitionEntity definition,
        string actorUserId,
        Player bidder,
        int bidderPlayerIndex,
        AuctionState auctionState,
        int minimumBid,
        int maximumBid,
        string source,
        string? fallbackReason = null)
    {
        if (maximumBid >= minimumBid && bidder.Cash >= minimumBid)
        {
            return new BidAction
            {
                PlayerId = bidder.Name,
                PlayerIndex = bidderPlayerIndex,
                ActorUserId = actorUserId,
                RailroadIndex = auctionState.RailroadIndex,
                AmountBid = minimumBid,
                BotMetadata = CreateBotMetadata(playerState, definition, source, fallbackReason)
            };
        }

        return new AuctionDropOutAction
        {
            PlayerId = bidder.Name,
            PlayerIndex = bidderPlayerIndex,
            ActorUserId = actorUserId,
            RailroadIndex = auctionState.RailroadIndex,
            BotMetadata = CreateBotMetadata(playerState, definition, source, fallbackReason)
        };
    }

    private static GameSeatState CacheAuctionPlan(
        List<GameSeatState> playerStates,
        GameSeatState playerState,
        AuctionState auctionState,
        int turnNumber,
        int maximumBid)
    {
        playerState.AuctionPlanTurnNumber = turnNumber;
        playerState.AuctionPlanRailroadIndex = auctionState.RailroadIndex;
        playerState.AuctionPlanStartingPrice = auctionState.StartingPrice;
        playerState.AuctionPlanMaximumBid = maximumBid;

        for (var index = 0; index < playerStates.Count; index++)
        {
            if (!string.Equals(playerStates[index].PlayerUserId, playerState.PlayerUserId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(playerStates[index].BotControlStatus, BotControlStatuses.Active, StringComparison.OrdinalIgnoreCase)
                || playerStates[index].BotControlClearedUtc is not null)
            {
                continue;
            }

            playerStates[index].AuctionPlanTurnNumber = playerState.AuctionPlanTurnNumber;
            playerStates[index].AuctionPlanRailroadIndex = playerState.AuctionPlanRailroadIndex;
            playerStates[index].AuctionPlanStartingPrice = playerState.AuctionPlanStartingPrice;
            playerStates[index].AuctionPlanMaximumBid = playerState.AuctionPlanMaximumBid;
            return playerStates[index];
        }

        return playerState;
    }

    private static object BuildAuctionPhaseContext(
        RailBaronGameEngine gameEngine,
        global::Boxcars.Engine.Persistence.AuctionTurnState auctionState,
        int minimumBid)
    {
        return new
        {
            AuctionRailroad = BuildRailroadSnapshot(gameEngine, auctionState.RailroadIndex),
            auctionState.CurrentBid,
            auctionState.StartingPrice,
            MinimumBid = minimumBid,
            Seller = new
            {
                auctionState.SellerPlayerIndex,
                auctionState.SellerPlayerName
            },
            Participants = auctionState.Participants
                .OrderBy(participant => participant.PlayerIndex)
                .Select(participant => new
                {
                    participant.PlayerIndex,
                    participant.PlayerName,
                    participant.CashOnHand,
                    participant.LastBidAmount,
                    participant.IsEligible,
                    participant.HasDroppedOut,
                    participant.HasPassedThisRound,
                    participant.LastAction
                })
                .ToList()
        };
    }

    private static object BuildPlayerMetadata(
        RailBaronGameEngine gameEngine,
        MapDefinition mapDefinition,
        IReadOnlyList<GamePlayerSelection> playerSelections,
        global::Boxcars.Engine.Persistence.PlayerState playerState,
        int playerIndex,
        int targetPlayerIndex)
    {
        return new
        {
            PlayerIndex = playerIndex,
            playerState.Name,
            UserId = ResolveSlotUserId(playerSelections, playerIndex),
            IsTargetPlayer = playerIndex == targetPlayerIndex,
            playerState.Cash,
            Engine = playerState.LocomotiveType,
            playerState.IsActive,
            playerState.IsBankrupt,
            playerState.HasDeclared,
            HomeCity = BuildCityReference(mapDefinition, playerState.HomeCityName),
            CurrentCity = BuildCityReference(mapDefinition, playerState.CurrentCityName),
            TripStartCity = BuildCityReference(mapDefinition, playerState.TripStartCityName),
            DestinationCity = BuildCityReference(mapDefinition, playerState.DestinationCityName),
            playerState.CurrentNodeId,
            playerState.RouteProgressIndex,
            ActiveRouteSegmentCount = playerState.ActiveRoute?.Segments.Count ?? 0,
            playerState.SelectedRouteNodeIds,
            playerState.SelectedRouteSegmentKeys,
            playerState.UsedSegments,
            OwnedRailroads = BuildRailroadSnapshots(gameEngine, playerState.OwnedRailroadIndices),
            GrandfatheredRailroads = BuildRailroadSnapshots(gameEngine, playerState.GrandfatheredRailroadIndices)
        };
    }

    private static List<object> BuildRailroadSnapshots(RailBaronGameEngine gameEngine, IEnumerable<int> railroadIndices)
    {
        var railroadLookup = gameEngine.Railroads.ToDictionary(railroad => railroad.Index);

        return railroadIndices
            .Distinct()
            .OrderBy(index => railroadLookup.TryGetValue(index, out var railroad) ? railroad.Name : index.ToString(CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase)
            .Select(index => BuildRailroadSnapshot(railroadLookup, index))
            .Where(snapshot => snapshot is not null)
            .Cast<object>()
            .ToList();
    }

    private static object? BuildRailroadSnapshot(RailBaronGameEngine gameEngine, int railroadIndex)
    {
        return BuildRailroadSnapshot(gameEngine.Railroads.ToDictionary(railroad => railroad.Index), railroadIndex);
    }

    private static object? BuildRailroadSnapshot(Dictionary<int, Railroad> railroadLookup, int railroadIndex)
    {
        return railroadLookup.TryGetValue(railroadIndex, out var railroad)
            ? new
            {
                railroad.Index,
                railroad.Name,
                railroad.ShortName,
                railroad.PurchasePrice,
                railroad.IsPublic,
                OwnerPlayerIndex = railroad.Owner?.Index,
                OwnerPlayerName = railroad.Owner?.Name
            }
            : null;
    }

    private static object? BuildCityReference(MapDefinition mapDefinition, string? cityName)
    {
        if (string.IsNullOrWhiteSpace(cityName))
        {
            return null;
        }

        var city = mapDefinition.Cities.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, cityName, StringComparison.OrdinalIgnoreCase));

        return city is null
            ? new { Name = cityName }
            : new
            {
                city.Name,
                city.RegionCode,
                city.MapDotIndex,
                city.Probability,
                city.PayoutIndex
            };
    }

    private static Dictionary<string, decimal> BuildRegionProbabilityLookup(MapDefinition mapDefinition)
    {
        var regionIndexByCode = mapDefinition.Regions
            .ToDictionary(region => region.Code, region => region.Index, StringComparer.OrdinalIgnoreCase);
        var weightedRegions = mapDefinition.Regions
            .Where(region => region.Probability.HasValue && region.Probability.Value > 0)
            .ToList();

        if (weightedRegions.Count > 0)
        {
            return weightedRegions.ToDictionary(
                region => region.Code,
                region => Math.Round((decimal)region.Probability!.Value, 3, MidpointRounding.AwayFromZero),
                StringComparer.OrdinalIgnoreCase);
        }

        var cityCountByRegion = mapDefinition.Cities
            .Where(city => city.MapDotIndex.HasValue && regionIndexByCode.ContainsKey(city.RegionCode))
            .GroupBy(city => city.RegionCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var totalCityCount = cityCountByRegion.Values.Sum();

        return cityCountByRegion.ToDictionary(
            entry => entry.Key,
            entry => totalCityCount == 0
                ? 0m
                : Math.Round(entry.Value * 100m / totalCityCount, 3, MidpointRounding.AwayFromZero),
            StringComparer.OrdinalIgnoreCase);
    }

    private static List<object> BuildCityProbabilityRows(
        MapDefinition mapDefinition,
        Dictionary<string, decimal> regionProbabilityLookup)
    {
        var regionIndexByCode = mapDefinition.Regions
            .ToDictionary(region => region.Code, region => region.Index, StringComparer.OrdinalIgnoreCase);
        var cityRows = new List<object>();

        foreach (var group in mapDefinition.Cities
                     .Where(city => city.MapDotIndex.HasValue && regionIndexByCode.ContainsKey(city.RegionCode))
                     .GroupBy(city => city.RegionCode, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var cities = group.OrderBy(city => city.Name, StringComparer.OrdinalIgnoreCase).ToList();
            var weightedCities = cities.Where(city => city.Probability.HasValue && city.Probability.Value > 0).ToList();
            var uniformWeight = cities.Count == 0
                ? 0m
                : Math.Round(100m / cities.Count, 2, MidpointRounding.AwayFromZero);

            foreach (var city in cities)
            {
                var withinRegionPercentage = weightedCities.Count > 0
                    ? city.Probability.HasValue
                        ? Math.Round((decimal)city.Probability.Value, 2, MidpointRounding.AwayFromZero)
                        : 0m
                    : uniformWeight;
                var globalAccessPercentage = regionProbabilityLookup.TryGetValue(city.RegionCode, out var regionProbability)
                    ? Math.Round(regionProbability * withinRegionPercentage / 100m, 2, MidpointRounding.AwayFromZero)
                    : 0m;

                cityRows.Add(new
                {
                    city.RegionCode,
                    CityName = city.Name,
                    WithinRegionPercentage = withinRegionPercentage,
                    GlobalAccessPercentage = globalAccessPercentage
                });
            }
        }

        return cityRows;
    }

    private static string BuildSegmentKey(string fromNodeId, string toNodeId, int railroadIndex)
    {
        return string.Concat(fromNodeId, "|", toNodeId, "|", railroadIndex.ToString(CultureInfo.InvariantCulture));
    }

    private static BotRecordedActionMetadata CreateBotMetadata(GameSeatState playerState, BotStrategyDefinitionEntity definition, string source, string? fallbackReason = null)
    {
        return CreateBotMetadata(playerState, definition.Name, source, fallbackReason, definition.IsBotUser);
    }

    private sealed record PurchaseReserveProfile(
        string CurrentRegionCode,
        int RecommendedOperatingReserveCash,
        decimal EngineRiskMultiplier,
        decimal WeightedReservePressure,
        IReadOnlyList<PurchaseReservePressure> RegionPressures);

    private sealed record PurchaseReservePressure(
        string RegionCode,
        string RegionName,
        decimal ProbabilityPercent,
        decimal OwnedAccessPercent,
        decimal AccessGapPercent,
        int RegionHopCount,
        decimal HopMultiplier,
        decimal WeightedReservePressure);

    private static BotRecordedActionMetadata CreateBotMetadata(GameSeatState playerState, string botName, string source, string? fallbackReason = null, bool isBotPlayer = false)
    {
        return new BotRecordedActionMetadata
        {
            BotDefinitionId = playerState.PlayerUserId,
            BotName = botName,
            ControllerMode = playerState.ControllerMode,
            IsBotPlayer = isBotPlayer,
            DecisionSource = source,
            FallbackReason = fallbackReason
        };
    }

    private static BotRecordedActionMetadata CreateCollectiveAuctionMetadata()
    {
        return new BotRecordedActionMetadata
        {
            BotDefinitionId = string.Empty,
            BotName = "AI Auction",
            ControllerMode = SeatControllerModes.AI,
            IsBotPlayer = true,
            DecisionSource = AllAiAuctionResolutionSource
        };
    }

}
