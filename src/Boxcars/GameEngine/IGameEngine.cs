using Boxcars.Data;
using RailBaronGameState = global::Boxcars.Engine.Persistence.GameState;

namespace Boxcars.GameEngine;

public interface IGameEngine
{
    event Action<string, GameStateUpdate>? OnStateChanged;
    event Action<string, GameActionFailure>? OnActionFailed;

    Task<string> CreateGameAsync(CreateGameRequest request, GameCreationOptions? options = null, CancellationToken cancellationToken = default);
    Task StartGameAsync(string gameId, CancellationToken cancellationToken = default);

    Task<RailBaronGameState> GetCurrentStateAsync(string gameId, CancellationToken cancellationToken = default);

    Task SynchronizeStateAsync(string gameId, RailBaronGameState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// The engine's suggested route for a player from where they are now: the same advice a bot follows. Plans to
    /// <paramref name="destinationNodeId"/> when given, otherwise to their destination (or a declared opponent to
    /// rover, unless they're in <paramref name="playersNotToRover"/>).
    /// </summary>
    Task<Boxcars.Data.Maps.RouteSuggestionResult> SuggestRouteAsync(
        string gameId,
        int playerIndex,
        string? destinationNodeId,
        IReadOnlyCollection<int> playersNotToRover,
        CancellationToken cancellationToken = default);

    bool IsGameBusy(string gameId);

    ValueTask EnqueueActionAsync(string gameId, PlayerAction action, CancellationToken cancellationToken = default);

    Task<bool> UndoLastOperationAsync(string gameId, CancellationToken cancellationToken = default);

    Task<bool> UndoToEventAsync(string gameId, string targetEventRowKey, string targetDescription, string actorUserId, CancellationToken cancellationToken = default);
}

public sealed record GameStateUpdate(RailBaronGameState State, IReadOnlyList<EventTimelineItem> TimelineItems, bool ReplaceTimeline = false);

public sealed record GameCreationOptions
{
    public string? PreferredGameId { get; init; }
}

public sealed record GameActionFailure
{
    public required PlayerActionKind ActionKind { get; init; }
    public string Message { get; init; } = string.Empty;
}
