namespace Boxcars.Data;

public static class PlayerControlRules
{
    public static SeatControllerState ResolveSeatControllerState(
        string gameId,
        string? slotUserId,
        bool isConnected,
        string? delegatedControllerUserId,
        GamePlayerStateEntity? activePlayerState)
    {
        var resolvedBotControllerMode = ResolveBotControllerMode(activePlayerState);
        var controllerMode = resolvedBotControllerMode switch
        {
            SeatControllerModes.AI when activePlayerState is not null && string.IsNullOrWhiteSpace(activePlayerState.ControllerUserId) => SeatControllerModes.AI,
            _ when !isConnected && !string.IsNullOrWhiteSpace(delegatedControllerUserId) => SeatControllerModes.Delegated,
            _ when !isConnected => SeatControllerModes.AI,
            _ => SeatControllerModes.Self
        };

        return new SeatControllerState
        {
            GameId = gameId,
            PlayerUserId = slotUserId ?? string.Empty,
            ControllerMode = controllerMode,
            DelegatedControllerUserId = delegatedControllerUserId,
            OwningHumanUserId = slotUserId,
            IsConnected = isConnected,
            BotDefinitionId = activePlayerState?.BotDefinitionId
                ?? (SeatControllerModes.IsAiControlled(controllerMode)
                    ? slotUserId
                    : null)
        };
    }

    public static bool IsDirectlyBoundToUser(string? slotUserId, string? currentUserId)
    {
        return !string.IsNullOrWhiteSpace(slotUserId)
            && !string.IsNullOrWhiteSpace(currentUserId)
            && string.Equals(slotUserId, currentUserId, StringComparison.OrdinalIgnoreCase);
    }

    public static bool CanUserControlSlot(string? slotUserId, string? currentUserId)
    {
        return CanUserControlSlot(slotUserId, currentUserId, delegatedControllerUserId: null, isPlayerActive: true);
    }

    public static bool CanUserControlSlot(string? slotUserId, string? currentUserId, bool isPlayerActive)
    {
        return CanUserControlSlot(slotUserId, currentUserId, delegatedControllerUserId: null, isPlayerActive);
    }

    public static bool CanUserControlSlot(
        string? slotUserId,
        string? currentUserId,
        string? delegatedControllerUserId,
        bool isPlayerActive)
    {
        if (!isPlayerActive)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return false;
        }

        return IsDirectlyBoundToUser(slotUserId, currentUserId)
            || IsDelegatedController(delegatedControllerUserId, currentUserId);
    }

    public static bool CanUserControlSlot(SeatControllerState controllerState, string? currentUserId, bool isPlayerActive)
    {
        return CanUserControlSlot(
            controllerState.PlayerUserId,
            currentUserId,
            controllerState.DelegatedControllerUserId,
            isPlayerActive);
    }

    public static bool CanServerControlSlot(SeatControllerState controllerState, string? actorUserId, string? serverActorUserId, bool isPlayerActive)
    {
        if (!isPlayerActive)
        {
            return false;
        }

        return IsAiControlledMode(controllerState.ControllerMode)
            && !string.IsNullOrWhiteSpace(actorUserId)
            && !string.IsNullOrWhiteSpace(serverActorUserId)
            && string.Equals(actorUserId, serverActorUserId, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAiControlledMode(string? controllerMode)
    {
        return SeatControllerModes.IsAiControlled(controllerMode);
    }

    public static string? ResolveBotControllerMode(GamePlayerStateEntity? playerState)
    {
        if (playerState is null
            || !string.Equals(playerState.BotControlStatus, BotControlStatuses.Active, StringComparison.OrdinalIgnoreCase)
            || playerState.BotControlClearedUtc is not null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(playerState.ControllerMode))
        {
            return SeatControllerModes.Normalize(playerState.ControllerMode);
        }

        return SeatControllerModes.AI;
    }

    public static bool HasActiveBotControl(GamePlayerStateEntity? playerState)
    {
        return ResolveBotControllerMode(playerState) is not null;
    }

    public static bool IsDelegatedController(string? delegatedControllerUserId, string? currentUserId)
    {
        return !string.IsNullOrWhiteSpace(delegatedControllerUserId)
            && !string.IsNullOrWhiteSpace(currentUserId)
            && string.Equals(delegatedControllerUserId, currentUserId, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsBeatlesSlot(string? slotUserId)
    {
        return !string.IsNullOrWhiteSpace(slotUserId)
            && slotUserId.EndsWith("@beatles.com", StringComparison.OrdinalIgnoreCase);
    }
}