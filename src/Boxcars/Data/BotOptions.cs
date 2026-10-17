namespace Boxcars.Data;

public sealed class BotOptions
{
    public const string SectionName = "Bots";
    public const string LegacyApiKeySettingName = "OpenAIKey";
    public const string DefaultServerActorUserId = "ai://boxcars-server";
    public const string DefaultServerActorDisplayName = "Boxcars AI";

    public string OpenAIKey { get; set; } = string.Empty;
    public string OpenAIModel { get; set; } = "gpt-5.4-mini";

    /// <summary>
    /// Reasoning effort sent with each request ("none", "low", "medium", ...). Empty sends none, for models without
    /// reasoning. Temperature is sent only when reasoning is off, since reasoning models reject it.
    /// </summary>
    public string OpenAIReasoningEffort { get; set; } = "low";
    public int DecisionTimeoutSeconds { get; set; } = 15;
    public int AutomaticActionDelayMilliseconds { get; set; } = 1000;
    public string ServerActorUserId { get; set; } = DefaultServerActorUserId;
    public string ServerActorDisplayName { get; set; } = DefaultServerActorDisplayName;
}