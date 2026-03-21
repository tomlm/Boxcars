namespace Boxcars.Data;

public sealed class AdvisorConversationSession
{
    public const string GreetingText = "How can I help?";

    public string GameId { get; init; } = string.Empty;
    public string CurrentUserId { get; init; } = string.Empty;
    public int? ControlledPlayerIndex { get; set; }
    public DateTimeOffset StartedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastContextRefreshUtc { get; set; }
    public string SeedContextKey { get; private set; } = string.Empty;
    public string SeedContextContent { get; private set; } = string.Empty;
    public int? SeedContextTurnNumber { get; private set; }
    public List<AdvisorMessage> Messages { get; } = [];

    public void EnsureGreeting()
    {
        if (Messages.Count > 0)
        {
            return;
        }

        AddAssistantMessage(GreetingText);
    }

    public void AddUserMessage(string content, int? contextTurnNumber = null)
    {
        AddMessage(AdvisorMessage.UserRole, content, contextTurnNumber);
    }

    public void AddAssistantMessage(string content, int? contextTurnNumber = null)
    {
        AddMessage(AdvisorMessage.AssistantRole, content, contextTurnNumber);
    }

    public void EnsureSeedContext(string seedContextKey, string content, int? contextTurnNumber = null)
    {
        if (string.IsNullOrWhiteSpace(seedContextKey) || string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        EnsureGreeting();

        if (string.Equals(SeedContextKey, seedContextKey, StringComparison.Ordinal)
            && string.Equals(SeedContextContent, content.Trim(), StringComparison.Ordinal)
            && SeedContextTurnNumber == contextTurnNumber)
        {
            return;
        }

        SeedContextKey = seedContextKey;
        SeedContextContent = content.Trim();
        SeedContextTurnNumber = contextTurnNumber;
    }

    private void AddMessage(string role, string content, int? contextTurnNumber)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        Messages.Add(new AdvisorMessage
        {
            MessageId = Guid.NewGuid().ToString("N"),
            Role = role,
            Content = content.Trim(),
            CreatedUtc = DateTimeOffset.UtcNow,
            ContextTurnNumber = contextTurnNumber
        });
    }
}