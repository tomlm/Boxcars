namespace Boxcars.Data;

public sealed class PlayerControlBinding
{
    public string UserId { get; init; } = string.Empty;
    public int PlayerIndex { get; init; } = -1;
    public string DisplayName { get; init; } = string.Empty;
    public string Color { get; init; } = string.Empty;
    public bool IsCurrentUser { get; init; }
    public bool IsConnected { get; init; } = true;
    public string DelegatedControllerUserId { get; init; } = string.Empty;
    public string ControllerMode { get; init; } = SeatControllerModes.Self;
    public string BotDefinitionId { get; init; } = string.Empty;
    public bool HasBotAssignment { get; init; }
}