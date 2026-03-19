using Azure;

namespace Boxcars.Data;

public sealed record BotLibraryRowModel
{
    public string BotDefinitionId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string StrategyText { get; init; } = string.Empty;
    public DateTimeOffset ModifiedUtc { get; init; }
    public ETag ETag { get; init; }
}

public sealed record BotLibraryEditorModel
{
    public string BotDefinitionId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string StrategyText { get; init; } = string.Empty;
    public ETag ETag { get; init; }
    public bool IsEditingExisting { get; init; }
}