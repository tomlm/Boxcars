using Boxcars.Data;
using Boxcars.Data.Maps;
using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Domain;
using Microsoft.Extensions.Options;
using RailBaronGameState = Boxcars.Engine.Persistence.GameState;
using PlayerStateSnapshot = Boxcars.Engine.Persistence.PlayerState;

namespace Boxcars.Services;

public sealed class GameBoardStateMapper(
    NetworkCoverageService networkCoverageService,
    MapAnalysisService mapAnalysisService,
    PurchaseRecommendationService purchaseRecommendationService,
    IOptions<PurchaseRulesOptions> purchaseRulesOptions)
{
    public IReadOnlyList<GamePlayerSelection> GetPlayerSelections(GameEntity? game)
    {
        return string.IsNullOrWhiteSpace(game?.PlayersJson)
            ? []
            : GamePlayerSelectionSerialization.Deserialize(game.PlayersJson);
    }

    public IReadOnlyList<PlayerControlBinding> BuildPlayerControlBindings(GameEntity? game, string? currentUserId)
    {
        var selections = GetPlayerSelections(game);

        return selections
            .Select((selection, index) => new PlayerControlBinding
            {
                UserId = selection.UserId,
                PlayerIndex = index,
                DisplayName = string.IsNullOrWhiteSpace(selection.DisplayName) ? selection.UserId : selection.DisplayName,
                Color = selection.Color,
                IsCurrentUser = PlayerControlRules.IsDirectlyBoundToUser(selection.UserId, currentUserId)
            })
            .ToList();
    }

    public BoardTurnViewState BuildTurnViewState(
        GameEntity? game,
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

        var bindings = BuildPlayerControlBindings(game, currentUserId);
        var currentUserPlayerIndex = bindings
            .Where(binding => binding.IsCurrentUser)
            .Select(binding => binding.PlayerIndex)
            .DefaultIfEmpty(-1)
            .First();

        var activePlayerIndex = state.ActivePlayerIndex;
        var activePlayer = activePlayerIndex >= 0 && activePlayerIndex < state.Players.Count
            ? state.Players[activePlayerIndex]
            : state.Players[0];
        var activePlayerSlotUserId = activePlayerIndex >= 0 && activePlayerIndex < bindings.Count
            ? bindings[activePlayerIndex].UserId
            : null;

        var selectedRoutePreview = NormalizePreview(activePlayerIndex, state, preview ?? BuildSelectedRoutePreview(activePlayer, state));
        var movementAllowance = state.Turn.MovementAllowance > 0
            ? state.Turn.MovementAllowance
            : CalculateRollTotal(state);
        var movementRemaining = Math.Max(0, state.Turn.MovementRemaining - selectedRoutePreview.MoveCount);

        var resolvedArrival = arrivalResolution ?? BuildArrivalResolution(state, mapDefinition, activePlayerIndex, activePlayer);

        return new BoardTurnViewState
        {
            ActivePlayerIndex = activePlayerIndex,
            CurrentUserPlayerIndex = currentUserPlayerIndex,
            ActivePlayerName = activePlayer.Name,
            TurnPhase = state.Turn.Phase,
            WhiteDieOne = GetWhiteDie(state, 0),
            WhiteDieTwo = GetWhiteDie(state, 1),
            RedDie = state.Turn.DiceResult?.RedDie,
            MovementAllowance = movementAllowance,
            MovementRemaining = movementRemaining,
            PreviewFee = selectedRoutePreview.FeeEstimate,
            CurrentRollTotal = CalculateRollTotal(state),
            IsActivePlayerAtDestination = IsPlayerAtDestination(activePlayer),
            ActivePlayerDestinationCity = activePlayer.DestinationCityName ?? string.Empty,
            SelectedRoutePreview = selectedRoutePreview,
            TraveledSegmentKeys = activePlayer.UsedSegments,
            IsCurrentUserActivePlayer = PlayerControlRules.CanUserControlSlot(activePlayerSlotUserId, currentUserId)
                || (currentUserPlayerIndex >= 0 && currentUserPlayerIndex == activePlayerIndex),
            CanEndTurn = CanEndTurn(state, selectedRoutePreview),
            ArrivalResolution = resolvedArrival,
            PurchasePhase = resolvedArrival?.PurchasePhase
        };
    }

    public IReadOnlyList<PlayerMapState> BuildPlayerMapStates(GameEntity? game, RailBaronGameState? state, string? currentUserId)
    {
        if (state is null)
        {
            return [];
        }

        var selections = GetPlayerSelections(game);

        return state.Players
            .Select((player, index) =>
            {
                var selection = index < selections.Count ? selections[index] : null;
                return new PlayerMapState
                {
                    PlayerId = selection?.UserId ?? player.Name,
                    Color = selection?.Color ?? PlayerColorOptions.Colors[index % PlayerColorOptions.Colors.Length],
                    HomeCityName = player.HomeCityName,
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

        return new TurnMovementPreview
        {
            NodeIds = nodeIds,
            SegmentKeys = segmentKeys,
            MoveCount = moveCount,
            FeeEstimate = state is null ? 0 : CalculatePreviewFee(state, state.ActivePlayerIndex, segmentKeys),
            ExhaustsMovement = movementRemaining > 0 && moveCount >= movementRemaining
        };
    }

    private static TurnMovementPreview NormalizePreview(int activePlayerIndex, RailBaronGameState state, TurnMovementPreview preview)
    {
        var moveCount = Math.Max(0, preview.SegmentKeys.Count);
        var movementRemaining = Math.Max(0, state.Turn.MovementRemaining);

        return new TurnMovementPreview
        {
            NodeIds = preview.NodeIds,
            SegmentKeys = preview.SegmentKeys,
            MoveCount = moveCount,
            FeeEstimate = CalculatePreviewFee(state, activePlayerIndex, preview.SegmentKeys),
            ExhaustsMovement = movementRemaining > 0 && moveCount >= movementRemaining
        };
    }

    private static int CalculatePreviewFee(RailBaronGameState state, int activePlayerIndex, IReadOnlyList<string> segmentKeys)
    {
        var railroadIndices = new HashSet<int>(state.Turn.RailroadsRiddenThisTurn);

        foreach (var segmentKey in segmentKeys)
        {
            if (TryParseRailroadIndex(segmentKey, out var railroadIndex))
            {
                railroadIndices.Add(railroadIndex);
            }
        }

        var usesBankRailroad = false;
        var opposingOwnerIndices = new HashSet<int>();

        foreach (var railroadIndex in railroadIndices)
        {
            if (!state.RailroadOwnership.TryGetValue(railroadIndex, out var ownerIndex) || ownerIndex is null)
            {
                usesBankRailroad = true;
                continue;
            }

            if (ownerIndex.Value != activePlayerIndex)
            {
                opposingOwnerIndices.Add(ownerIndex.Value);
            }
        }

        var bankFee = usesBankRailroad ? 1000 : 0;
        var opponentRate = state.AllRailroadsSold ? 10000 : 5000;
        return bankFee + (opposingOwnerIndices.Count * opponentRate);
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
                IsAffordable = true
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
        var hasActivePurchaseControls = taskbarOptions.Count > 0;
        var noPurchaseNotification = !hasActivePurchaseControls && (hasUnownedRailroads || hasUpgradeableEngine)
            ? $"{activePlayer.Name} does not have enough money to purchase anything."
            : null;

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
            .Where(option => option.IsEligible)
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

    private static string BuildEngineOptionKey(LocomotiveType locomotiveType)
    {
        return $"engine:{locomotiveType}";
    }
}