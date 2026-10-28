using System.Collections.Concurrent;
using Boxcars.Data.Maps;

namespace Boxcars.Services;

/// <summary>
/// The route the player on turn is looking at (the suggestion and their hand-picked selection), shared so everyone
/// else in the game sees it on their map as they pick. Nothing here is saved; only the latest preview per game is kept.
/// </summary>
public sealed class SharedRoutePreviewService
{
    private readonly ConcurrentDictionary<string, SharedRoutePreview> _previewsByGameId = new(StringComparer.Ordinal);

    public event Action<string>? PreviewChanged;

    public void Publish(string gameId, SharedRoutePreview preview)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentNullException.ThrowIfNull(preview);

        if (_previewsByGameId.TryGetValue(gameId, out var current) && current.Equals(preview))
        {
            return;
        }

        _previewsByGameId[gameId] = preview;
        PreviewChanged?.Invoke(gameId);
    }

    public SharedRoutePreview? Get(string gameId)
    {
        return _previewsByGameId.TryGetValue(gameId, out var preview) ? preview : null;
    }
}
