using Boxcars.Data;
using RailBaronGameState = global::Boxcars.Engine.Persistence.GameState;

namespace Boxcars.GameEngine;

public interface IGameEngine
{
    event Action<string, RailBaronGameState>? OnStateChanged;

    Task<string> CreateGameAsync(CreateGameRequest request, GameCreationOptions? options = null, CancellationToken cancellationToken = default);

    Task<RailBaronGameState> GetCurrentStateAsync(string gameId, CancellationToken cancellationToken = default);

    ValueTask EnqueueActionAsync(string gameId, PlayerAction action, CancellationToken cancellationToken = default);

    Task<bool> UndoLastOperationAsync(string gameId, CancellationToken cancellationToken = default);
}

public sealed record GameCreationOptions
{
    public string? PreferredGameId { get; init; }
}
