namespace Boxcars.Data;

public sealed class AdvisorResponse
{
    public bool Succeeded { get; init; }
    public string? AssistantText { get; init; }
    public string? FailureReason { get; init; }
    public int? ContextTurnNumber { get; init; }
    public DateTimeOffset CompletedUtc { get; init; } = DateTimeOffset.UtcNow;

    public static AdvisorResponse Success(string assistantText, int? contextTurnNumber) => new()
    {
        Succeeded = true,
        AssistantText = assistantText,
        ContextTurnNumber = contextTurnNumber,
        CompletedUtc = DateTimeOffset.UtcNow
    };

    public static AdvisorResponse Failed(string failureReason, int? contextTurnNumber = null) => new()
    {
        FailureReason = failureReason,
        ContextTurnNumber = contextTurnNumber,
        CompletedUtc = DateTimeOffset.UtcNow
    };
}