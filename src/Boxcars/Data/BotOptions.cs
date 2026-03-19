namespace Boxcars.Data;

public sealed class BotOptions
{
    public const string SectionName = "Bots";
    public const string LegacyApiKeySettingName = "OpenAIKey";
    public const string DefaultServerActorUserId = "ai://boxcars-server";
    public const string DefaultServerActorDisplayName = "Boxcars AI";

    public string OpenAIKey { get; set; } = string.Empty;
    public string OpenAIModel { get; set; } = "gpt-4o-mini";
    public int DecisionTimeoutSeconds { get; set; } = 15;
    public int AutomaticActionDelayMilliseconds { get; set; } = 1000;
    public string ServerActorUserId { get; set; } = DefaultServerActorUserId;
    public string ServerActorDisplayName { get; set; } = DefaultServerActorDisplayName;
}