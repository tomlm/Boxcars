using Boxcars.Data;
using Boxcars.Services;
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

    bool IsGameBusy(string gameId);

    ValueTask EnqueueActionAsync(string gameId, PlayerAction action, CancellationToken cancellationToken = default);

    Task<bool> UndoLastOperationAsync(string gameId, CancellationToken cancellationToken = default);

    Task<bool> UndoToEventAsync(string gameId, string targetEventRowKey, string targetDescription, string actorUserId, CancellationToken cancellationToken = default);

    /// <summary>In debug mode, the bot move waiting for the game's admin, if any.</summary>
    BotRouteReview? GetPendingBotRouteReview(string gameId) => null;

    /// <summary>Lets the waiting bot move go ahead. Only the game's admin may approve.</summary>
    Task<bool> ApproveBotRouteAsync(string gameId, string actorUserId, CancellationToken cancellationToken = default) => Task.FromResult(false);

    /// <summary>Turns debug mode on or off mid-game. Only the game's admin may change it.</summary>
    Task<bool> SetDebugModeAsync(string gameId, string actorUserId, bool enabled, CancellationToken cancellationToken = default) => Task.FromResult(false);
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
