using Boxcars.Data;
using RailBaronGameState = global::Boxcars.Engine.Persistence.GameState;

namespace Boxcars.GameEngine;

public interface IGameEngine
{
    event Action<string, GameStateUpdate>? OnStateChanged;
    event Action<string, GameActionFailure>? OnActionFailed;

    Task<string> CreateGameAsync(CreateGameRequest request, GameCreationOptions? options = null, CancellationToken cancellationToken = default);

    Task<RailBaronGameState> GetCurrentStateAsync(string gameId, CancellationToken cancellationToken = default);

    Task SynchronizeStateAsync(string gameId, RailBaronGameState state, CancellationToken cancellationToken = default);

    bool IsGameBusy(string gameId);

    ValueTask EnqueueActionAsync(string gameId, PlayerAction action, CancellationToken cancellationToken = default);

    Task<bool> UndoLastOperationAsync(string gameId, CancellationToken cancellationToken = default);
}

public sealed record GameStateUpdate(RailBaronGameState State, IReadOnlyList<EventTimelineItem> TimelineItems);

public sealed record GameCreationOptions
{
    public string? PreferredGameId { get; init; }
}

public sealed record GameActionFailure
{
    public required PlayerActionKind ActionKind { get; init; }
    public string Message { get; init; } = string.Empty;
}
