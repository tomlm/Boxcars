namespace Boxcars.Data;

public sealed class BotOptions
{
    public const string SectionName = "Bots";
    public const string LegacyApiKeySettingName = "OpenAIKey";

    public string OpenAIKey { get; set; } = string.Empty;
    public string OpenAIModel { get; set; } = "gpt-4o-mini";
    public int DecisionTimeoutSeconds { get; set; } = 15;
    public int AutomaticActionDelayMilliseconds { get; set; } = 1000;
}