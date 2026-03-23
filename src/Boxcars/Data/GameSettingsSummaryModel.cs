namespace Boxcars.Data;

public sealed class GameSettingsSummaryModel
{
    public static GameSettingsSummaryModel Empty { get; } = new();

    public string Title { get; init; } = "Game Settings";
    public string SourceDescription { get; init; } = string.Empty;
    public bool UsesDefaultFallback { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<GameSettingsSummarySectionModel> Sections { get; init; } = [];

    public bool HasContent => Sections.Count > 0;
}

public sealed class GameSettingsSummarySectionModel
{
    public string Title { get; init; } = string.Empty;
    public IReadOnlyList<GameSettingsSummaryItemModel> Items { get; init; } = [];
}

public sealed class GameSettingsSummaryItemModel
{
    public string Label { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}