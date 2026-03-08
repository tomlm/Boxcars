using Boxcars.Data;
using Boxcars.Data.Maps;
using RailBaronGameState = Boxcars.Engine.Persistence.GameState;
using PlayerStateSnapshot = Boxcars.Engine.Persistence.PlayerState;

namespace Boxcars.Services;

public sealed class GameBoardStateMapper
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
                IsCurrentUser = !string.IsNullOrWhiteSpace(currentUserId)
                    && string.Equals(selection.UserId, currentUserId, StringComparison.OrdinalIgnoreCase)
            })
            .ToList();
    }

    public BoardTurnViewState BuildTurnViewState(
        GameEntity? game,
        RailBaronGameState? state,
        string? currentUserId,
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

        var selectedRoutePreview = NormalizePreview(activePlayerIndex, state, preview ?? BuildSelectedRoutePreview(activePlayer, state));
        var movementAllowance = state.Turn.MovementAllowance > 0
            ? state.Turn.MovementAllowance
            : CalculateRollTotal(state);
        var movementRemaining = Math.Max(0, state.Turn.MovementRemaining - selectedRoutePreview.MoveCount);

        return new BoardTurnViewState
        {
            ActivePlayerIndex = activePlayerIndex,
            CurrentUserPlayerIndex = currentUserPlayerIndex,
            ActivePlayerName = activePlayer.Name,
            TurnPhase = state.Turn.Phase,
            MovementAllowance = movementAllowance,
            MovementRemaining = movementRemaining,
            PreviewFee = selectedRoutePreview.FeeEstimate,
            CurrentRollTotal = CalculateRollTotal(state),
            SelectedRoutePreview = selectedRoutePreview,
            TraveledSegmentKeys = activePlayer.UsedSegments,
            IsCurrentUserActivePlayer = currentUserPlayerIndex >= 0 && currentUserPlayerIndex == activePlayerIndex,
            CanEndTurn = CanEndTurn(state, selectedRoutePreview),
            ArrivalResolution = arrivalResolution
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
                    IsCurrentUser = !string.IsNullOrWhiteSpace(currentUserId)
                        && !string.IsNullOrWhiteSpace(selection?.UserId)
                        && string.Equals(selection.UserId, currentUserId, StringComparison.OrdinalIgnoreCase),
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
}