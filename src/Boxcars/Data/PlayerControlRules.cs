namespace Boxcars.Data;

public static class PlayerControlRules
{
    public static SeatControllerState ResolveSeatControllerState(
        string gameId,
        string? slotUserId,
        bool isConnected,
        string? delegatedControllerUserId,
        BotAssignment? activeBotAssignment)
    {
        var resolvedBotControllerMode = ResolveBotControllerMode(activeBotAssignment);
        var controllerMode = resolvedBotControllerMode switch
        {
            SeatControllerModes.AiBotSeat => SeatControllerModes.AiBotSeat,
            SeatControllerModes.AiGhost when !isConnected && !string.IsNullOrWhiteSpace(delegatedControllerUserId) => SeatControllerModes.AiGhost,
            _ when !isConnected && !string.IsNullOrWhiteSpace(delegatedControllerUserId) => SeatControllerModes.HumanDelegated,
            _ => SeatControllerModes.HumanDirect
        };

        return new SeatControllerState
        {
            GameId = gameId,
            PlayerUserId = slotUserId ?? string.Empty,
            ControllerMode = controllerMode,
            DelegatedControllerUserId = delegatedControllerUserId,
            OwningHumanUserId = slotUserId,
            IsConnected = isConnected,
            BotDefinitionId = activeBotAssignment?.BotDefinitionId
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

        return IsAiControllerMode(controllerState.ControllerMode)
            && !string.IsNullOrWhiteSpace(actorUserId)
            && !string.IsNullOrWhiteSpace(serverActorUserId)
            && string.Equals(actorUserId, serverActorUserId, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAiControllerMode(string? controllerMode)
    {
        return string.Equals(controllerMode, SeatControllerModes.AiBotSeat, StringComparison.OrdinalIgnoreCase)
            || string.Equals(controllerMode, SeatControllerModes.AiGhost, StringComparison.OrdinalIgnoreCase);
    }

    public static string? ResolveBotControllerMode(BotAssignment? assignment)
    {
        if (assignment is null
            || !string.Equals(assignment.Status, BotAssignmentStatuses.Active, StringComparison.OrdinalIgnoreCase)
            || assignment.ClearedUtc is not null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(assignment.ControllerMode))
        {
            return assignment.ControllerMode;
        }

        return string.IsNullOrWhiteSpace(assignment.ControllerUserId)
            ? SeatControllerModes.AiBotSeat
            : SeatControllerModes.AiGhost;
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