namespace Boxcars.Data;

public sealed class AdvisorMessage
{
    public const string AssistantRole = "Assistant";
    public const string UserRole = "User";

    public string MessageId { get; init; } = string.Empty;
    public string Role { get; init; } = AssistantRole;
    public string Content { get; init; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; init; }
    public int? ContextTurnNumber { get; init; }

    public bool IsAssistant => string.Equals(Role, AssistantRole, StringComparison.OrdinalIgnoreCase);
}