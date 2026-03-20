using Boxcars.Data;
using Boxcars.Data.Maps;
using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Domain;
using Boxcars.Services.Maps;
using Microsoft.Extensions.Options;
using RailBaronGameState = Boxcars.Engine.Persistence.GameState;
using PlayerStateSnapshot = Boxcars.Engine.Persistence.PlayerState;

namespace Boxcars.Services;

public sealed class GameBoardStateMapper(
    NetworkCoverageService networkCoverageService,
    MapAnalysisService mapAnalysisService,
    PurchaseRecommendationService purchaseRecommendationService,
    IOptions<PurchaseRulesOptions> purchaseRulesOptions,
    GamePresenceService? gamePresenceService = null)
{
    public IReadOnlyList<GamePlayerSelection> GetPlayerSelections(IReadOnlyList<GamePlayerStateEntity>? playerStates)
    {
        return playerStates is null || playerStates.Count == 0
            ? []
            : GamePlayerStateProjection.BuildPlayerSelections(playerStates);
    }

    public IReadOnlyList<GamePlayerStateEntity> GetBotControlledPlayerStates(IReadOnlyList<GamePlayerStateEntity>? playerStates)
    {
        return playerStates is null || playerStates.Count == 0
            ? []
            : playerStates
                .Where(playerState => !string.IsNullOrWhiteSpace(playerState.BotDefinitionId))
                .ToList();
    }

    public IReadOnlyDictionary<string, GamePlayerStateEntity> BuildLatestBotControlStates(IReadOnlyList<GamePlayerStateEntity>? playerStates)
    {
        return BuildPlayerStateLookup(playerStates);
    }

    public static string GetBotControlStatusLabel(GamePlayerStateEntity? playerState, string? botName = null)
    {
        if (playerState is null)
        {
            return string.Empty;
        }

        return playerState.BotControlStatus switch
        {
            BotControlStatuses.Active => string.IsNullOrWhiteSpace(botName) ? "Bot assigned" : botName,
            BotControlStatuses.MissingDefinition => "Bot removed from library",
            _ => string.Empty
        };
    }

    public IReadOnlyList<PlayerControlBinding> BuildPlayerControlBindings(string gameId, IReadOnlyList<GamePlayerStateEntity>? playerStates, string? currentUserId)
    {
        var selections = GetPlayerSelections(playerStates);
        var playerStatesByUserId = BuildPlayerStateLookup(playerStates);

        return selections
            .Select((selection, index) =>
            {
                var controllerState = ResolveSeatControllerState(gameId, selection.UserId, playerStatesByUserId);
                return new PlayerControlBinding
                {
                    ControllerMode = controllerState.ControllerMode,
                    DelegatedControllerUserId = controllerState.DelegatedControllerUserId ?? string.Empty,
                    IsConnected = controllerState.IsConnected,
                    BotDefinitionId = controllerState.BotDefinitionId ?? string.Empty,
                    HasActiveBotControl = PlayerControlRules.IsAiControlledMode(controllerState.ControllerMode),
                    UserId = selection.UserId,
                    PlayerIndex = index,
                    DisplayName = string.IsNullOrWhiteSpace(selection.DisplayName) ? selection.UserId : selection.DisplayName,
                    Color = selection.Color,
                    IsCurrentUser = PlayerControlRules.IsDirectlyBoundToUser(selection.UserId, currentUserId)
                };
            })
            .ToList();
    }

    public BoardTurnViewState BuildTurnViewState(
        string gameId,
        IReadOnlyList<GamePlayerStateEntity>? playerStates,
        RailBaronGameState? state,
        string? currentUserId,
        MapDefinition? mapDefinition = null,
        TurnMovementPreview? preview = null,
        ArrivalResolutionModel? arrivalResolution = null)
    {
        if (state is null || state.Players.Count == 0)
        {
            return new BoardTurnViewState();
        }

        var bindings = BuildPlayerControlBindings(gameId, playerStates, currentUserId);
        var currentUserPlayerIndex = bindings
            .Where(binding => binding.IsCurrentUser)
            .Select(binding => binding.PlayerIndex)
            .DefaultIfEmpty(-1)
            .First();

        var activePlayerIndex = state.ActivePlayerIndex;
        var activePlayer = activePlayerIndex >= 0 && activePlayerIndex < state.Players.Count
            ? state.Players[activePlayerIndex]
            : state.Players[0];
        var activeBinding = activePlayerIndex >= 0 && activePlayerIndex < bindings.Count
            ? bindings[activePlayerIndex]
            : null;

        var selectedRoutePreview = NormalizePreview(activePlayerIndex, state, preview ?? BuildSelectedRoutePreview(activePlayer, state));
        var movementAllowance = state.Turn.MovementAllowance > 0
            ? state.Turn.MovementAllowance
            : CalculateRollTotal(state);
        var movementRemaining = Math.Max(0, state.Turn.MovementRemaining - selectedRoutePreview.MoveCount);

        var resolvedArrival = arrivalResolution ?? BuildArrivalResolution(state, mapDefinition, activePlayerIndex, activePlayer);
        var forcedSalePhase = BuildForcedSalePhaseModel(state, mapDefinition, activePlayerIndex, activePlayer, bindings, currentUserId);
        var regionChoicePhase = BuildRegionChoicePhaseModel(state, mapDefinition, activePlayerIndex, activePlayer);

        return new BoardTurnViewState
        {
            ActivePlayerIndex = activePlayerIndex,
            CurrentUserPlayerIndex = currentUserPlayerIndex,
            ActivePlayerName = activePlayer.Name,
            TurnPhase = state.Turn.Phase,
            WhiteDieOne = GetWhiteDie(state, 0),
            WhiteDieTwo = GetWhiteDie(state, 1),
            RedDie = state.Turn.DiceResult?.RedDie,
            BonusRollAvailable = state.Turn.BonusRollAvailable,
            MovementAllowance = movementAllowance,
            MovementRemaining = movementRemaining,
            PreviewFee = selectedRoutePreview.FeeEstimate,
            PreviewHasUnfriendlyFee = selectedRoutePreview.HasUnfriendlyFee,
            CurrentRollTotal = CalculateRollTotal(state),
            IsActivePlayerAtDestination = IsPlayerAtDestination(activePlayer),
            ActivePlayerDestinationCity = activePlayer.DestinationCityName ?? string.Empty,
            ActivePlayerControllerMode = activeBinding?.ControllerMode ?? SeatControllerModes.Self,
            SelectedRoutePreview = selectedRoutePreview,
            TraveledSegmentKeys = activePlayer.UsedSegments,
            IsCurrentUserActivePlayer = activeBinding is not null
                && PlayerControlRules.CanUserControlSlot(
                    activeBinding.UserId,
                    currentUserId,
                    activeBinding.DelegatedControllerUserId,
                    activePlayer.IsActive),
            CanEndTurn = CanEndTurn(state, selectedRoutePreview),
            ArrivalResolution = resolvedArrival,
            PurchasePhase = resolvedArrival?.PurchasePhase,
            ForcedSalePhase = forcedSalePhase,
            RegionChoicePhase = regionChoicePhase
        };
    }

    private SeatControllerState ResolveSeatControllerState(
        string gameId,
        string? slotUserId,
        Dictionary<string, GamePlayerStateEntity>? playerStatesByUserId = null)
    {
        playerStatesByUserId ??= BuildPlayerStateLookup(null);
        var activePlayerState = !string.IsNullOrWhiteSpace(slotUserId)
            && playerStatesByUserId.TryGetValue(slotUserId, out var playerState)
                ? playerState
                : null;

        if (gamePresenceService is null)
        {
            return PlayerControlRules.ResolveSeatControllerState(
                gameId,
                slotUserId,
                isConnected: true,
                delegatedControllerUserId: null,
                activePlayerState);
        }

        return gamePresenceService.ResolveSeatControllerState(gameId, slotUserId, activePlayerState);
    }

    private static Dictionary<string, GamePlayerStateEntity> BuildPlayerStateLookup(IReadOnlyList<GamePlayerStateEntity>? playerStates)
    {
        if (playerStates is null || playerStates.Count == 0)
        {
            return new Dictionary<string, GamePlayerStateEntity>(StringComparer.OrdinalIgnoreCase);
        }

        return playerStates
            .Where(playerState => !string.IsNullOrWhiteSpace(playerState.PlayerUserId))
            .GroupBy(playerState => playerState.PlayerUserId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(playerState => playerState.BotControlClearedUtc ?? playerState.BotControlActivatedUtc ?? DateTimeOffset.MinValue)
                    .ThenByDescending(playerState => playerState.BotControlActivatedUtc ?? DateTimeOffset.MinValue)
                    .First(),
                StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<PlayerMapState> BuildPlayerMapStates(IReadOnlyList<GamePlayerStateEntity>? playerStates, RailBaronGameState? state, string? currentUserId)
    {
        if (state is null)
        {
            return [];
        }

        var selections = GetPlayerSelections(playerStates);

        return state.Players
            .Select((player, index) =>
            {
                var selection = index < selections.Count ? selections[index] : null;
                return new PlayerMapState
                {
                    PlayerId = selection?.UserId ?? player.Name,
                    Color = selection?.Color ?? PlayerColorOptions.Colors[index % PlayerColorOptions.Colors.Length],
                    HomeCityName = player.HomeCityName,
                    TripStartCityName = player.TripStartCityName,
                    CurrentCityName = player.CurrentCityName,
                    StartNodeId = player.ActiveRoute?.NodeIds.FirstOrDefault(),
                    DestinationCityName = player.DestinationCityName,
                    CurrentNodeId = player.CurrentNodeId,
                    TraveledSegmentKeys = player.UsedSegments,
                    IsCurrentUser = PlayerControlRules.IsDirectlyBoundToUser(selection?.UserId, currentUserId),
                    SelectedRouteSegmentKeys = BuildSelectedRouteSegmentKeys(player),
                    IsActiveTurn = index == state.ActivePlayerIndex
                };
            })
            .ToList();
    }

    public TurnMovementPreview BuildSelectedRoutePreview(PlayerStateSnapshot? playerState, RailBaronGameState? state)
    {
        if (playerState is null)
        {
            return TurnMovementPreview.Empty;
        }

        var nodeIds = playerState.SelectedRouteNodeIds.Count > 0
            ? playerState.SelectedRouteNodeIds
            : playerState.ActiveRoute?.NodeIds ?? [];

        var segmentKeys = playerState.SelectedRouteSegmentKeys.Count > 0
            ? playerState.SelectedRouteSegmentKeys
            : BuildSelectedRouteSegmentKeys(playerState);

        var moveCount = Math.Max(0, segmentKeys.Count);
        var movementRemaining = Math.Max(0, state?.Turn.MovementRemaining ?? 0);
        var feeSummary = state is null
            ? new PreviewFeeSummary(0, false)
            : CalculatePreviewFeeSummary(state, state.ActivePlayerIndex, segmentKeys);

        return new TurnMovementPreview
        {
            NodeIds = nodeIds,
            SegmentKeys = segmentKeys,
            MoveCount = moveCount,
            FeeEstimate = feeSummary.FeeEstimate,
            HasUnfriendlyFee = feeSummary.HasUnfriendlyFee,
            ExhaustsMovement = movementRemaining > 0 && moveCount >= movementRemaining
        };
    }

    private static TurnMovementPreview NormalizePreview(int activePlayerIndex, RailBaronGameState state, TurnMovementPreview preview)
    {
        var moveCount = Math.Max(0, preview.SegmentKeys.Count);
        var movementRemaining = Math.Max(0, state.Turn.MovementRemaining);
        var feeSummary = CalculatePreviewFeeSummary(state, activePlayerIndex, preview.SegmentKeys);

        return new TurnMovementPreview
        {
            NodeIds = preview.NodeIds,
            SegmentKeys = preview.SegmentKeys,
            MoveCount = moveCount,
            FeeEstimate = feeSummary.FeeEstimate,
            HasUnfriendlyFee = feeSummary.HasUnfriendlyFee,
            ExhaustsMovement = movementRemaining > 0 && moveCount >= movementRemaining
        };
    }

    private static PreviewFeeSummary CalculatePreviewFeeSummary(RailBaronGameState state, int activePlayerIndex, IReadOnlyList<string> segmentKeys)
    {
        var railroadIndices = new HashSet<int>(state.Turn.RailroadsRiddenThisTurn);

        foreach (var segmentKey in segmentKeys)
        {
            if (TryParseRailroadIndex(segmentKey, out var railroadIndex))
            {
                railroadIndices.Add(railroadIndex);
            }
        }

        var activePlayer = state.Players[activePlayerIndex];
        var fullRateRailroadIndices = SimulateFullRateRailroads(
            activePlayerIndex,
            activePlayer.GrandfatheredRailroadIndices,
            state.Turn.RailroadsRequiringFullOwnerRateThisTurn,
            state.RailroadOwnership,
            segmentKeys);
        var usesBaseRateRailroad = false;
        var ownerBuckets = new Dictionary<int, bool>();

        foreach (var railroadIndex in railroadIndices)
        {
            if (!state.RailroadOwnership.TryGetValue(railroadIndex, out var ownerIndex) || ownerIndex is null)
            {
                usesBaseRateRailroad = true;
                continue;
            }

            if (ownerIndex.Value == activePlayerIndex)
            {
                usesBaseRateRailroad = true;
                continue;
            }

            if (ownerIndex.Value != activePlayerIndex)
            {
                var requiresFullOwnerRate = fullRateRailroadIndices.Contains(railroadIndex);
                if (!ownerBuckets.TryGetValue(ownerIndex.Value, out var existingRequiresFullOwnerRate))
                {
                    ownerBuckets[ownerIndex.Value] = requiresFullOwnerRate;
                }
                else
                {
                    ownerBuckets[ownerIndex.Value] = existingRequiresFullOwnerRate || requiresFullOwnerRate;
                }
            }
        }

        var bankFee = usesBaseRateRailroad ? 1000 : 0;
        var opponentRate = state.AllRailroadsSold ? 10000 : 5000;
        return new PreviewFeeSummary(
            bankFee + ownerBuckets.Values.Sum(requiresFullOwnerRate => requiresFullOwnerRate ? opponentRate : 1000),
            ownerBuckets.Count > 0);
    }

    private readonly record struct PreviewFeeSummary(int FeeEstimate, bool HasUnfriendlyFee);

    private static HashSet<int> SimulateFullRateRailroads(
        int activePlayerIndex,
        IReadOnlyList<int> currentGrandfatheredRailroads,
        IReadOnlyList<int> currentFullRateRailroads,
        Dictionary<int, int?> railroadOwnership,
        IReadOnlyList<string> segmentKeys)
    {
        var grandfatheredRailroads = currentGrandfatheredRailroads.ToHashSet();
        var fullRateRailroads = currentFullRateRailroads.ToHashSet();

        foreach (var segmentKey in segmentKeys)
        {
            if (!TryParseRailroadIndex(segmentKey, out var railroadIndex))
            {
                continue;
            }

            if (railroadOwnership.TryGetValue(railroadIndex, out var ownerIndex)
                && ownerIndex is not null
                && ownerIndex.Value != activePlayerIndex
                && !grandfatheredRailroads.Contains(railroadIndex))
            {
                fullRateRailroads.Add(railroadIndex);
            }

            if (grandfatheredRailroads.Count == 0)
            {
                continue;
            }

            if (grandfatheredRailroads.Contains(railroadIndex))
            {
                grandfatheredRailroads.IntersectWith([railroadIndex]);
            }
            else
            {
                grandfatheredRailroads.Clear();
            }
        }

        return fullRateRailroads;
    }

    private static bool TryParseRailroadIndex(string segmentKey, out int railroadIndex)
    {
        railroadIndex = -1;

        if (string.IsNullOrWhiteSpace(segmentKey))
        {
            return false;
        }

        var parts = segmentKey.Split('|', StringSplitOptions.TrimEntries);
        return parts.Length == 3
            && int.TryParse(parts[2], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out railroadIndex);
    }

    private static List<string> BuildSelectedRouteSegmentKeys(PlayerStateSnapshot playerState)
    {
        if (playerState.ActiveRoute is null)
        {
            return playerState.SelectedRouteSegmentKeys.ToList();
        }

        return playerState.ActiveRoute.Segments
            .Select(segment => string.Concat(segment.FromNodeId, "|", segment.ToNodeId, "|", segment.RailroadIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();
    }

    private static int CalculateRollTotal(RailBaronGameState state)
    {
        if (state.Turn.DiceResult is null)
        {
            return 0;
        }

        return (state.Turn.DiceResult.WhiteDice?.Sum() ?? 0) + (state.Turn.DiceResult.RedDie ?? 0);
    }

    private static int GetWhiteDie(RailBaronGameState state, int index)
    {
        var whiteDice = state.Turn.DiceResult?.WhiteDice;
        return whiteDice is { Length: > 0 } && index < whiteDice.Length
            ? whiteDice[index]
            : 0;
    }

    private static bool IsPlayerAtDestination(PlayerStateSnapshot player)
    {
        return !string.IsNullOrWhiteSpace(player.DestinationCityName)
            && string.Equals(player.CurrentCityName, player.DestinationCityName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanEndTurn(RailBaronGameState state, TurnMovementPreview preview)
    {
        if (string.Equals(state.Turn.Phase, "EndTurn", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.Equals(state.Turn.Phase, "Move", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return Math.Max(0, state.Turn.MovementRemaining - preview.MoveCount) <= 0 || preview.ExhaustsMovement;
    }

    private ArrivalResolutionModel? BuildArrivalResolution(
        RailBaronGameState state,
        MapDefinition? mapDefinition,
        int activePlayerIndex,
        PlayerStateSnapshot activePlayer)
    {
        if (state.Turn.ArrivalResolution is null)
        {
            return null;
        }

        var purchasePhase = BuildPurchasePhaseModel(state, mapDefinition, activePlayerIndex, activePlayer);

        return new ArrivalResolutionModel
        {
            PlayerIndex = state.Turn.ArrivalResolution.PlayerIndex,
            DestinationCityName = state.Turn.ArrivalResolution.DestinationCityName,
            PayoutAmount = state.Turn.ArrivalResolution.PayoutAmount,
            CashAfterPayout = state.Turn.ArrivalResolution.CashAfterPayout,
            PurchaseOpportunityAvailable = state.Turn.ArrivalResolution.PurchaseOpportunityAvailable,
            Message = state.Turn.ArrivalResolution.Message,
            IsVisible = true,
            PurchasePhase = purchasePhase,
            HasActivePurchaseControls = purchasePhase?.HasActivePurchaseControls == true,
            NoPurchaseNotification = purchasePhase?.NoPurchaseNotification
        };
    }

    private PurchasePhaseModel? BuildPurchasePhaseModel(
        RailBaronGameState state,
        MapDefinition? mapDefinition,
        int activePlayerIndex,
        PlayerStateSnapshot activePlayer)
    {
        if (!string.Equals(state.Turn.Phase, TurnPhase.Purchase.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var currentCoverage = mapDefinition is null
            ? null
            : networkCoverageService.BuildSnapshot(mapDefinition, activePlayer.OwnedRailroadIndices);
        var engineOptions = BuildEligibleEngineOptions(activePlayer);
        var affordableRailroadOptions = mapDefinition is null
            ? []
            : BuildAffordableRailroadOptions(state, mapDefinition, activePlayer.Cash);
        var railroadOptions = mapDefinition is null
            ? []
            : BuildAvailableRailroadOptions(state, mapDefinition, activePlayer.Cash);
        var taskbarOptions = railroadOptions
            .Select(railroadOption => new PurchaseOptionModel
            {
                OptionKey = BuildRailroadOptionKey(railroadOption.RailroadIndex),
                OptionKind = PurchaseOptionKind.Railroad,
                DisplayName = railroadOption.RailroadName,
                PurchasePrice = railroadOption.PurchasePrice,
                SortPriceDescendingKey = railroadOption.PurchasePrice,
                IsAffordable = railroadOption.IsAffordable
            })
            .Concat(engineOptions.Select(engineOption => new PurchaseOptionModel
            {
                OptionKey = BuildEngineOptionKey(engineOption.EngineType),
                OptionKind = PurchaseOptionKind.EngineUpgrade,
                DisplayName = engineOption.DisplayName,
                PurchasePrice = engineOption.PurchasePrice,
                SortPriceDescendingKey = engineOption.PurchasePrice,
                IsAffordable = engineOption.IsEligible
            }))
            .OrderByDescending(option => option.SortPriceDescendingKey)
            .ThenBy(option => option.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        string? selectedOptionKey = null;
        var selectedRailroad = selectedOptionKey is not null
            ? railroadOptions.FirstOrDefault(option => string.Equals(BuildRailroadOptionKey(option.RailroadIndex), selectedOptionKey, StringComparison.Ordinal))
            : null;
        var projectedCoverage = mapDefinition is not null && selectedRailroad is not null && currentCoverage is not null
            ? networkCoverageService.BuildProjectedSnapshot(mapDefinition, activePlayer.OwnedRailroadIndices, selectedRailroad.RailroadIndex)
            : null;
        var mapAnalysisReport = mapDefinition is null ? null : mapAnalysisService.BuildReport(mapDefinition);
        var unownedRailroads = mapDefinition is null
            ? []
            : mapDefinition.Railroads
                .Where(railroad => !state.RailroadOwnership.TryGetValue(railroad.Index, out var ownerIndex) || ownerIndex is null)
                .ToList();
        var projectedCoverageByRailroad = mapDefinition is null
            ? new Dictionary<int, NetworkCoverageSnapshot>()
            : unownedRailroads.ToDictionary(
                railroad => railroad.Index,
                railroad => networkCoverageService.BuildProjectedSnapshot(mapDefinition, activePlayer.OwnedRailroadIndices, railroad.Index));

        var allEligibleEngineTypes = GetEligibleUpgradeTargets(ParseLocomotiveType(activePlayer.LocomotiveType)).ToList();
        var hasUpgradeableEngine = allEligibleEngineTypes.Count > 0;
        var hasUnownedRailroads = mapDefinition is not null && mapDefinition.Railroads.Any(railroad => !state.RailroadOwnership.TryGetValue(railroad.Index, out var ownerIndex) || ownerIndex is null);
        var hasPurchaseOptions = taskbarOptions.Count > 0;
        var noPurchaseNotification = !hasPurchaseOptions && (hasUnownedRailroads || hasUpgradeableEngine)
            ? $"{activePlayer.Name} does not have enough money to purchase anything."
            : null;
        var hasActivePurchaseControls = hasPurchaseOptions || !string.IsNullOrWhiteSpace(noPurchaseNotification);

        return new PurchasePhaseModel
        {
            PlayerIndex = activePlayerIndex,
            PlayerName = activePlayer.Name,
            CashAvailable = activePlayer.Cash,
            DestinationCityName = state.Turn.ArrivalResolution?.DestinationCityName ?? string.Empty,
            PayoutAmount = state.Turn.ArrivalResolution?.PayoutAmount ?? 0,
            CashAfterPayout = state.Turn.ArrivalResolution?.CashAfterPayout ?? activePlayer.Cash,
            RailroadOptions = railroadOptions,
            EngineOptions = engineOptions,
            TaskbarOptions = taskbarOptions,
            TaskbarState = new PurchaseTaskbarState
            {
                Options = taskbarOptions,
                SelectedOptionKey = selectedOptionKey,
                CanBuy = false,
                CanDecline = hasActivePurchaseControls
            },
            CanDecline = hasActivePurchaseControls,
            CurrentCoverage = currentCoverage,
            ProjectedCoverage = projectedCoverage,
            SelectedTab = PurchaseExperienceTab.Map,
            MapAnalysisReport = mapAnalysisReport,
            SelectedOptionKey = selectedOptionKey,
            SelectedRailroadOverlay = mapDefinition is not null && selectedRailroad is not null
                ? networkCoverageService.BuildRailroadOverlayInfo(mapDefinition, activePlayer.OwnedRailroadIndices, selectedRailroad)
                : null,
            HasActivePurchaseControls = hasActivePurchaseControls,
            NoPurchaseNotification = noPurchaseNotification,
            RecommendationInputs = mapAnalysisReport is null
                ? null
                : purchaseRecommendationService.BuildInputSet(
                    mapAnalysisReport,
                    affordableRailroadOptions.Select(option => option.RailroadIndex),
                    unownedRailroads.Select(railroad => railroad.Index),
                    allEligibleEngineTypes,
                    currentCoverage,
                    projectedCoverageByRailroad)
        };
    }

    private static List<RailroadPurchaseOption> BuildAvailableRailroadOptions(RailBaronGameState state, MapDefinition mapDefinition, int cashAvailable)
    {
        return mapDefinition.Railroads
            .Where(railroad => !state.RailroadOwnership.TryGetValue(railroad.Index, out var ownerIndex) || ownerIndex is null)
            .Select(railroad => new RailroadPurchaseOption
            {
                RailroadIndex = railroad.Index,
                RailroadName = railroad.Name,
                ShortName = railroad.ShortName ?? string.Empty,
                PurchasePrice = railroad.PurchasePrice ?? Boxcars.Engine.Domain.GameEngine.GetRailroadPurchasePrice(railroad.Index),
                IsAffordable = cashAvailable >= (railroad.PurchasePrice ?? Boxcars.Engine.Domain.GameEngine.GetRailroadPurchasePrice(railroad.Index))
            })
            .OrderByDescending(option => option.PurchasePrice)
            .ThenBy(option => option.RailroadName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<RailroadPurchaseOption> BuildAffordableRailroadOptions(RailBaronGameState state, MapDefinition mapDefinition, int cashAvailable)
    {
        return BuildAvailableRailroadOptions(state, mapDefinition, cashAvailable)
            .Where(option => option.IsAffordable)
            .ToList();
    }

    private List<EngineUpgradeOption> BuildEligibleEngineOptions(PlayerStateSnapshot activePlayer)
    {
        var currentEngineType = ParseLocomotiveType(activePlayer.LocomotiveType);

        return GetEligibleUpgradeTargets(currentEngineType)
            .Select(targetEngineType =>
            {
                var price = Boxcars.Engine.Domain.GameEngine.GetUpgradeCost(currentEngineType, targetEngineType, purchaseRulesOptions.Value.SuperchiefPrice);
                return new EngineUpgradeOption
                {
                    EngineType = targetEngineType,
                    DisplayName = targetEngineType.ToString(),
                    PurchasePrice = price,
                    CurrentEngineType = currentEngineType,
                    IsEligible = price > 0 && activePlayer.Cash >= price
                };
            })
            .Where(option => option.PurchasePrice > 0)
            .OrderByDescending(option => option.PurchasePrice)
            .ThenBy(option => option.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<LocomotiveType> GetEligibleUpgradeTargets(LocomotiveType currentEngineType)
    {
        return Enum.GetValues<LocomotiveType>()
            .Where(targetEngineType => targetEngineType > currentEngineType);
    }

    private static LocomotiveType ParseLocomotiveType(string locomotiveType)
    {
        return Enum.TryParse<LocomotiveType>(locomotiveType, out var parsedLocomotiveType)
            ? parsedLocomotiveType
            : LocomotiveType.Freight;
    }

    private static string BuildRailroadOptionKey(int railroadIndex)
    {
        return $"railroad:{railroadIndex}";
    }

    private RegionChoicePhaseModel? BuildRegionChoicePhaseModel(
        RailBaronGameState state,
        MapDefinition? mapDefinition,
        int activePlayerIndex,
        PlayerStateSnapshot activePlayer)
    {
        if (!string.Equals(state.Turn.Phase, TurnPhase.RegionChoice.ToString(), StringComparison.OrdinalIgnoreCase)
            || state.Turn.PendingRegionChoice is null)
        {
            return null;
        }

        var otherOwnedRailroadIndices = state.RailroadOwnership
            .Where(entry => entry.Value.HasValue && entry.Value.Value != activePlayerIndex)
            .Select(entry => entry.Key)
            .ToArray();
        var pendingRegionChoice = state.Turn.PendingRegionChoice;
        var coverageByRegionCode = mapDefinition is null
            ? new Dictionary<string, RegionCoverageSnapshot>(StringComparer.OrdinalIgnoreCase)
            : networkCoverageService.BuildSnapshotIncludingPublicRailroads(
                    mapDefinition,
                    activePlayer.OwnedRailroadIndices,
                    otherOwnedRailroadIndices)
                .RegionAccess
                .ToDictionary(region => region.RegionCode, region => region, StringComparer.OrdinalIgnoreCase);
        var regionChoiceAverages = BuildRegionChoiceAverages(mapDefinition, pendingRegionChoice.CurrentCityName);
        var regionByCode = mapDefinition?.Regions.ToDictionary(region => region.Code, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, RegionDefinition>(StringComparer.OrdinalIgnoreCase);

        var options = pendingRegionChoice.EligibleRegionCodes
            .Select(regionCode =>
            {
                coverageByRegionCode.TryGetValue(regionCode, out var coverage);
                var regionName = regionByCode.TryGetValue(regionCode, out var region)
                    ? region.Name
                    : regionCode;
                var regionProbability = regionByCode.TryGetValue(regionCode, out region)
                    ? Convert.ToDecimal(region.Probability ?? 0d, System.Globalization.CultureInfo.InvariantCulture)
                    : 0m;
                var eligibleCityCount = pendingRegionChoice.EligibleCityCountsByRegion.TryGetValue(regionCode, out var count)
                    ? count
                    : 0;
                regionChoiceAverages.TryGetValue(regionCode, out var regionChoiceAverage);

                return new DestinationRegionOption
                {
                    RegionCode = regionCode,
                    RegionName = regionName,
                    RegionProbabilityPercent = regionProbability,
                    AccessibleDestinationPercent = coverage?.AccessibleDestinationPercent ?? 0m,
                    MonopolyDestinationPercent = coverage?.MonopolyDestinationPercent ?? 0m,
                    EligibleCityCount = eligibleCityCount,
                    AverageDistance = regionChoiceAverage?.AverageDistance,
                    AveragePayout = regionChoiceAverage?.AveragePayout
                };
            })
            .OrderByDescending(option => option.AccessibleDestinationPercent)
            .ThenBy(option => option.RegionName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var currentRegionName = regionByCode.TryGetValue(pendingRegionChoice.CurrentRegionCode, out var currentRegion)
            ? currentRegion.Name
            : pendingRegionChoice.CurrentRegionCode;

        return new RegionChoicePhaseModel
        {
            PlayerIndex = pendingRegionChoice.PlayerIndex,
            PlayerName = activePlayer.Name,
            CurrentCityName = pendingRegionChoice.CurrentCityName,
            CurrentRegionCode = pendingRegionChoice.CurrentRegionCode,
            CurrentRegionName = currentRegionName,
            Options = options,
            CanConfirm = options.Count > 0
        };
    }

    private static Dictionary<string, RegionChoiceAverageMetrics> BuildRegionChoiceAverages(
        MapDefinition? mapDefinition,
        string currentCityName)
    {
        if (mapDefinition is null || string.IsNullOrWhiteSpace(currentCityName))
        {
            return new Dictionary<string, RegionChoiceAverageMetrics>(StringComparer.OrdinalIgnoreCase);
        }

        var currentCity = mapDefinition.Cities.FirstOrDefault(city =>
            string.Equals(city.Name, currentCityName, StringComparison.OrdinalIgnoreCase));
        if (currentCity is null)
        {
            return new Dictionary<string, RegionChoiceAverageMetrics>(StringComparer.OrdinalIgnoreCase);
        }

        var regionIndexByCode = mapDefinition.Regions.ToDictionary(region => region.Code, region => region.Index, StringComparer.OrdinalIgnoreCase);
        var routeService = new MapRouteService();
        var routeContext = routeService.BuildContext(mapDefinition);
        var currentNodeId = TryBuildCityNodeId(currentCity, regionIndexByCode);

        return mapDefinition.Regions
            .Select(region => new
            {
                region.Code,
                Metrics = BuildRegionChoiceAverageMetrics(
                    mapDefinition,
                    routeService,
                    routeContext,
                    regionIndexByCode,
                    currentCity,
                    currentNodeId,
                    region.Code)
            })
            .Where(entry => entry.Metrics is not null)
            .ToDictionary(
                entry => entry.Code,
                entry => entry.Metrics!,
                StringComparer.OrdinalIgnoreCase);
    }

    private static RegionChoiceAverageMetrics? BuildRegionChoiceAverageMetrics(
        MapDefinition mapDefinition,
        MapRouteService routeService,
        MapRouteContext routeContext,
        IReadOnlyDictionary<string, int> regionIndexByCode,
        CityDefinition currentCity,
        string? currentNodeId,
        string regionCode)
    {
        var weightedCities = mapDefinition.Cities
            .Where(city => string.Equals(city.RegionCode, regionCode, StringComparison.OrdinalIgnoreCase)
                && city.Probability.HasValue
                && city.Probability.Value > 0)
            .ToList();
        if (weightedCities.Count == 0)
        {
            return null;
        }

        var totalWeight = weightedCities.Sum(city => Convert.ToDecimal(city.Probability!.Value, System.Globalization.CultureInfo.InvariantCulture));
        if (totalWeight <= 0m)
        {
            return null;
        }

        decimal? averageDistance = null;
        if (!string.IsNullOrWhiteSpace(currentNodeId) && routeContext.Adjacency.ContainsKey(currentNodeId))
        {
            decimal weightedDistanceTotal = 0m;
            decimal distanceWeightTotal = 0m;

            foreach (var city in weightedCities)
            {
                var destinationNodeId = TryBuildCityNodeId(city, regionIndexByCode);
                if (string.IsNullOrWhiteSpace(destinationNodeId))
                {
                    continue;
                }

                int? distance = string.Equals(currentNodeId, destinationNodeId, StringComparison.OrdinalIgnoreCase)
                    ? 0
                    : routeContext.Adjacency.ContainsKey(destinationNodeId)
                        ? routeService.FindShortestSelection(routeContext, currentNodeId, destinationNodeId)?.Segments.Count
                        : null;
                if (!distance.HasValue)
                {
                    continue;
                }

                var weight = Convert.ToDecimal(city.Probability!.Value, System.Globalization.CultureInfo.InvariantCulture) / totalWeight;
                weightedDistanceTotal += weight * distance.Value;
                distanceWeightTotal += weight;
            }

            if (distanceWeightTotal > 0m)
            {
                averageDistance = decimal.Round(weightedDistanceTotal / distanceWeightTotal, 2, MidpointRounding.AwayFromZero);
            }
        }

        decimal? averagePayout = null;
        if (currentCity.PayoutIndex.HasValue)
        {
            decimal weightedPayoutTotal = 0m;
            decimal payoutWeightTotal = 0m;

            foreach (var city in weightedCities)
            {
                if (!city.PayoutIndex.HasValue || !mapDefinition.TryGetPayout(currentCity.PayoutIndex.Value, city.PayoutIndex.Value, out var payout))
                {
                    continue;
                }

                var weight = Convert.ToDecimal(city.Probability!.Value, System.Globalization.CultureInfo.InvariantCulture) / totalWeight;
                weightedPayoutTotal += weight * payout;
                payoutWeightTotal += weight;
            }

            if (payoutWeightTotal > 0m)
            {
                averagePayout = decimal.Round(weightedPayoutTotal / payoutWeightTotal, 0, MidpointRounding.AwayFromZero);
            }
        }

        return new RegionChoiceAverageMetrics(averageDistance, averagePayout);
    }

    private static string? TryBuildCityNodeId(CityDefinition city, IReadOnlyDictionary<string, int> regionIndexByCode)
    {
        return city.MapDotIndex.HasValue && regionIndexByCode.TryGetValue(city.RegionCode, out var regionIndex)
            ? MapRouteService.NodeKey(regionIndex, city.MapDotIndex.Value)
            : null;
    }

    private sealed record RegionChoiceAverageMetrics(decimal? AverageDistance, decimal? AveragePayout);

    private ForcedSalePhaseModel? BuildForcedSalePhaseModel(
        RailBaronGameState state,
        MapDefinition? mapDefinition,
        int activePlayerIndex,
        PlayerStateSnapshot activePlayer,
        IReadOnlyList<PlayerControlBinding> bindings,
        string? currentUserId)
    {
        if (!string.Equals(state.Turn.Phase, TurnPhase.UseFees.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        bool hasForcedSaleState = state.Turn.ForcedSale is not null || state.Turn.PendingFeeAmount > activePlayer.Cash;
        if (!hasForcedSaleState)
        {
            return null;
        }

        int? effectiveSelectedRailroadIndex = state.Turn.SelectedRailroadForSaleIndex
            ?? activePlayer.OwnedRailroadIndices.OrderBy(index => index).Select(index => (int?)index).FirstOrDefault();
        var saleCandidates = BuildSaleCandidates(activePlayer, mapDefinition, effectiveSelectedRailroadIndex);
        var currentNetwork = mapDefinition is null
            ? null
            : networkCoverageService.BuildSnapshot(mapDefinition, activePlayer.OwnedRailroadIndices);
        var projectedNetworkAfterSale = mapDefinition is not null && effectiveSelectedRailroadIndex is int selectedRailroadIndex
            ? networkCoverageService.BuildProjectedSnapshotAfterSale(mapDefinition, activePlayer.OwnedRailroadIndices, selectedRailroadIndex)
            : null;

        return new ForcedSalePhaseModel
        {
            PlayerIndex = activePlayerIndex,
            PlayerName = activePlayer.Name,
            AmountOwed = Math.Max(state.Turn.PendingFeeAmount, state.Turn.ForcedSale?.AmountOwed ?? 0),
            CashOnHand = activePlayer.Cash,
            FeeShortfall = Math.Max(0, Math.Max(state.Turn.PendingFeeAmount, state.Turn.ForcedSale?.AmountOwed ?? 0) - activePlayer.Cash),
            SaleCandidates = saleCandidates,
            SelectedRailroadIndex = effectiveSelectedRailroadIndex,
            CurrentNetwork = currentNetwork,
            ProjectedNetworkAfterSale = projectedNetworkAfterSale,
            NetworkTab = new NetworkTabModel
            {
                PlayerName = activePlayer.Name,
                RailroadSummaries = saleCandidates
                    .Select(candidate => new NetworkRailroadSummaryModel
                    {
                        RailroadIndex = candidate.RailroadIndex,
                        RailroadName = candidate.RailroadName,
                        OriginalPurchasePrice = candidate.OriginalPurchasePrice,
                        AccessPercentWithCurrentOwnership = currentNetwork?.AccessibleCityPercent ?? 0m,
                        MonopolyPercentWithCurrentOwnership = currentNetwork?.MonopolyCityPercent ?? 0m,
                        AccessPercentIfSold = mapDefinition is null
                            ? null
                            : networkCoverageService.BuildProjectedSnapshotAfterSale(mapDefinition, activePlayer.OwnedRailroadIndices, candidate.RailroadIndex).AccessibleCityPercent,
                        MonopolyPercentIfSold = mapDefinition is null
                            ? null
                            : networkCoverageService.BuildProjectedSnapshotAfterSale(mapDefinition, activePlayer.OwnedRailroadIndices, candidate.RailroadIndex).MonopolyCityPercent
                    })
                    .ToList(),
                SelectedRailroadImpact = effectiveSelectedRailroadIndex is int selectedCandidateRailroadIndex && currentNetwork is not null && projectedNetworkAfterSale is not null
                    ? new RailroadOverlayInfo
                    {
                        RailroadIndex = selectedCandidateRailroadIndex,
                        RailroadName = saleCandidates.FirstOrDefault(candidate => candidate.RailroadIndex == selectedCandidateRailroadIndex)?.RailroadName ?? string.Empty,
                        PurchasePrice = saleCandidates.FirstOrDefault(candidate => candidate.RailroadIndex == selectedCandidateRailroadIndex)?.OriginalPurchasePrice ?? 0,
                        IsAffordable = true,
                        MetricRows = BuildOverlayMetricRows(currentNetwork, projectedNetworkAfterSale, mapDefinition!)
                    }
                    : null
            },
            AuctionState = state.Turn.Auction is null
                ? null
                : new ForcedSaleAuctionStateModel
                {
                    RailroadIndex = state.Turn.Auction.RailroadIndex,
                    RailroadName = state.Turn.Auction.RailroadName,
                    SellerPlayerIndex = state.Turn.Auction.SellerPlayerIndex,
                    SellerPlayerName = state.Turn.Auction.SellerPlayerName,
                    StartingPrice = state.Turn.Auction.StartingPrice,
                    CurrentBid = state.Turn.Auction.CurrentBid,
                    LastBidderPlayerIndex = state.Turn.Auction.LastBidderPlayerIndex,
                    CurrentBidderPlayerIndex = state.Turn.Auction.CurrentBidderPlayerIndex,
                    CurrentBidderPlayerName = state.Turn.Auction.CurrentBidderPlayerIndex is int currentBidderPlayerIndex
                        && currentBidderPlayerIndex >= 0
                        && currentBidderPlayerIndex < state.Players.Count
                            ? state.Players[currentBidderPlayerIndex].Name
                            : string.Empty,
                    MinimumBid = state.Turn.Auction.CurrentBid > 0
                        ? state.Turn.Auction.CurrentBid + Boxcars.Engine.Domain.GameEngine.AuctionBidIncrement
                        : state.Turn.Auction.StartingPrice,
                    RoundNumber = state.Turn.Auction.RoundNumber,
                    ConsecutiveNoBidTurnCount = state.Turn.Auction.ConsecutiveNoBidTurnCount,
                    Status = ParseAuctionStatus(state.Turn.Auction.Status),
                    CanCurrentUserAct = state.Turn.Auction.CurrentBidderPlayerIndex is int currentBidderIndex
                        && currentBidderIndex >= 0
                        && currentBidderIndex < bindings.Count
                        && state.Players[currentBidderIndex].IsActive
                        && PlayerControlRules.IsDirectlyBoundToUser(bindings[currentBidderIndex].UserId, currentUserId),
                    Participants = state.Turn.Auction.Participants
                        .Select(participant => new AuctionParticipantModel
                        {
                            PlayerIndex = participant.PlayerIndex,
                            PlayerName = participant.PlayerName,
                            CashOnHand = participant.CashOnHand,
                            LastBidAmount = participant.LastBidAmount,
                            IsEligible = participant.IsEligible,
                            HasDroppedOut = participant.HasDroppedOut,
                            HasPassedThisRound = participant.HasPassedThisRound,
                            LastAction = ParseAuctionParticipantAction(participant.LastAction)
                        })
                        .ToList()
                },
            CanSellToBank = effectiveSelectedRailroadIndex.HasValue && state.Turn.Auction is null,
            CanStartAuction = effectiveSelectedRailroadIndex.HasValue && state.Turn.Auction is null,
            CanResolveFees = (state.Turn.ForcedSale?.CanPayNow ?? false) && state.Turn.Auction is null
        };
    }

    private static List<SaleCandidateModel> BuildSaleCandidates(PlayerStateSnapshot activePlayer, MapDefinition? mapDefinition, int? selectedRailroadIndex)
    {
        if (mapDefinition is null)
        {
            return [];
        }

        var ownedIndices = activePlayer.OwnedRailroadIndices.ToHashSet();
        return mapDefinition.Railroads
            .Where(railroad => ownedIndices.Contains(railroad.Index))
            .Select(railroad =>
            {
                var price = railroad.PurchasePrice ?? Boxcars.Engine.Domain.GameEngine.GetRailroadPurchasePrice(railroad.Index);
                return new SaleCandidateModel
                {
                    RailroadIndex = railroad.Index,
                    RailroadName = railroad.Name,
                    ShortName = railroad.ShortName ?? string.Empty,
                    OriginalPurchasePrice = price,
                    BankSalePrice = price / 2,
                    IsSelected = selectedRailroadIndex == railroad.Index
                };
            })
            .OrderBy(candidate => candidate.RailroadName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<RailroadOverlayMetricRow> BuildOverlayMetricRows(
        NetworkCoverageSnapshot currentCoverage,
        NetworkCoverageSnapshot projectedCoverage,
        MapDefinition mapDefinition)
    {
        var currentAccessByCode = currentCoverage.RegionAccess.ToDictionary(region => region.RegionCode, region => region.AccessibleDestinationPercent, StringComparer.OrdinalIgnoreCase);
        var projectedAccessByCode = projectedCoverage.RegionAccess.ToDictionary(region => region.RegionCode, region => region.AccessibleDestinationPercent, StringComparer.OrdinalIgnoreCase);
        var currentMonopolyByCode = currentCoverage.RegionAccess.ToDictionary(region => region.RegionCode, region => region.MonopolyDestinationPercent, StringComparer.OrdinalIgnoreCase);
        var projectedMonopolyByCode = projectedCoverage.RegionAccess.ToDictionary(region => region.RegionCode, region => region.MonopolyDestinationPercent, StringComparer.OrdinalIgnoreCase);
        var rows = new List<RailroadOverlayMetricRow>
        {
            new()
            {
                Label = "Total",
                AccessPercent = currentCoverage.AccessibleDestinationPercent,
                ProjectedAccessPercent = projectedCoverage.AccessibleDestinationPercent,
                MonopolyPercent = currentCoverage.MonopolyDestinationPercent,
                ProjectedMonopolyPercent = projectedCoverage.MonopolyDestinationPercent,
                AccessDeltaPercent = Math.Round(projectedCoverage.AccessibleDestinationPercent - currentCoverage.AccessibleDestinationPercent, 1, MidpointRounding.AwayFromZero),
                MonopolyDeltaPercent = Math.Round(projectedCoverage.MonopolyDestinationPercent - currentCoverage.MonopolyDestinationPercent, 1, MidpointRounding.AwayFromZero)
            }
        };

        rows.AddRange(mapDefinition.Regions.Select(region =>
        {
            var currentAccessPercent = currentAccessByCode.TryGetValue(region.Code, out var currentAccessValue) ? currentAccessValue : 0m;
            var projectedAccessPercent = projectedAccessByCode.TryGetValue(region.Code, out var projectedAccessValue) ? projectedAccessValue : 0m;
            var currentMonopolyPercent = currentMonopolyByCode.TryGetValue(region.Code, out var currentMonopolyValue) ? currentMonopolyValue : 0m;
            var projectedMonopolyPercent = projectedMonopolyByCode.TryGetValue(region.Code, out var projectedMonopolyValue) ? projectedMonopolyValue : 0m;

            return new RailroadOverlayMetricRow
            {
                Label = region.Code,
                AccessPercent = currentAccessPercent,
                ProjectedAccessPercent = projectedAccessPercent,
                MonopolyPercent = currentMonopolyPercent,
                ProjectedMonopolyPercent = projectedMonopolyPercent,
                AccessDeltaPercent = Math.Round(projectedAccessPercent - currentAccessPercent, 1, MidpointRounding.AwayFromZero),
                MonopolyDeltaPercent = Math.Round(projectedMonopolyPercent - currentMonopolyPercent, 1, MidpointRounding.AwayFromZero)
            };
        }));

        return rows;
    }

    private static ForcedSaleAuctionStatusModel ParseAuctionStatus(string status)
    {
        return Enum.TryParse<ForcedSaleAuctionStatusModel>(status, out var parsedStatus)
            ? parsedStatus
            : ForcedSaleAuctionStatusModel.Open;
    }

    private static AuctionParticipantActionModel ParseAuctionParticipantAction(string action)
    {
        return Enum.TryParse<AuctionParticipantActionModel>(action, out var parsedAction)
            ? parsedAction
            : AuctionParticipantActionModel.None;
    }

    private static string BuildEngineOptionKey(LocomotiveType locomotiveType)
    {
        return $"engine:{locomotiveType}";
    }
}