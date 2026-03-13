namespace Boxcars.Data;

public static class PlayerControlRules
{
    public static bool IsDirectlyBoundToUser(string? slotUserId, string? currentUserId)
    {
        return !string.IsNullOrWhiteSpace(slotUserId)
            && !string.IsNullOrWhiteSpace(currentUserId)
            && string.Equals(slotUserId, currentUserId, StringComparison.OrdinalIgnoreCase);
    }

    public static bool CanUserControlSlot(string? slotUserId, string? currentUserId)
    {
        return CanUserControlSlot(slotUserId, currentUserId, isPlayerActive: true);
    }

    public static bool CanUserControlSlot(string? slotUserId, string? currentUserId, bool isPlayerActive)
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
            || IsBeatlesSlot(slotUserId);
    }

    public static bool IsBeatlesSlot(string? slotUserId)
    {
        return !string.IsNullOrWhiteSpace(slotUserId)
            && slotUserId.EndsWith("@beatles.com", StringComparison.OrdinalIgnoreCase);
    }
}