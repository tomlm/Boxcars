using MudBlazor;

namespace Boxcars.Data;

/// <summary>
/// A single play-by-play message shown in the game board's notification band.
/// </summary>
public sealed record PlayByPlayMessage
{
    public string Text { get; init; } = string.Empty;

    public string Icon { get; init; } = Icons.Material.Filled.Notifications;

    public Color IconColor { get; init; } = Color.Info;

    /// <summary>Hex color of the acting player, or null for a neutral message.</summary>
    public string? AccentColorHex { get; init; }
}
