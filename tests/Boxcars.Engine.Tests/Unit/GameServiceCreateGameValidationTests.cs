using Azure.Data.Tables;
using Boxcars.Data;
using Boxcars.Engine.Domain;
using Boxcars.Engine.Persistence;
using Boxcars.GameEngine;
using Boxcars.Hubs;
using Boxcars.Services;
using Microsoft.AspNetCore.SignalR;

namespace Boxcars.Engine.Tests.Unit;

public class GameServiceCreateGameValidationTests
{
    [Fact]
    public async Task CreateGameAsync_InvalidWinningCash_ReturnsFailure()
    {
        var engine = new CapturingGameEngine();
        var service = new GameService(new FakeTableClient(), new FakeTableClient(), new FakeHubContext(), engine, new GameSettingsResolver());

        var result = await service.CreateGameAsync(CreateRequest(GameSettings.Default with
        {
            AnnouncingCash = 250_000,
            WinningCash = 200_000
        }), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Winning cash must be greater than or equal to announcing cash.", result.Reason);
        Assert.Null(engine.CapturedRequest);
    }

    [Fact]
    public async Task CreateGameAsync_InvalidNumericSetting_ReturnsFailure()
    {
        var engine = new CapturingGameEngine();
        var service = new GameService(new FakeTableClient(), new FakeTableClient(), new FakeHubContext(), engine, new GameSettingsResolver());

        var result = await service.CreateGameAsync(CreateRequest(GameSettings.Default with { StartingCash = 0 }), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Starting cash must be greater than zero.", result.Reason);
        Assert.Null(engine.CapturedRequest);
    }

    [Fact]
    public async Task CreateGameAsync_ValidSettings_NormalizesAndPassesSettingsToEngine()
    {
        var engine = new CapturingGameEngine();
        var service = new GameService(new FakeTableClient(), new FakeTableClient(), new FakeHubContext(), engine, new GameSettingsResolver());
        var request = CreateRequest(GameSettings.Default with
        {
            StartEngine = LocomotiveType.Express,
            StartingCash = 35_000,
            ExpressPrice = 6_000,
            SuperchiefPrice = 45_000
        });

        var result = await service.CreateGameAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(engine.CapturedRequest);
        Assert.Equal(35_000, engine.CapturedRequest!.Settings.StartingCash);
        Assert.Equal(LocomotiveType.Express, engine.CapturedRequest.Settings.StartEngine);
        Assert.Equal(6_000, engine.CapturedRequest.Settings.ExpressPrice);
        Assert.Equal(45_000, engine.CapturedRequest.Settings.SuperchiefPrice);
    }

    private static CreateGameRequest CreateRequest(GameSettings settings)
    {
        return new CreateGameRequest
        {
            CreatorUserId = "creator@example.com",
            Players =
            [
                new GamePlayerSelection { UserId = "creator@example.com", DisplayName = "Alice", Color = "red" },
                new GamePlayerSelection { UserId = "bob@example.com", DisplayName = "Bob", Color = "blue" }
            ],
            Settings = settings
        };
    }

    private sealed class CapturingGameEngine : IGameEngine
    {
        public CreateGameRequest? CapturedRequest { get; private set; }

        public event Action<string, GameStateUpdate>? OnStateChanged { add { } remove { } }
        public event Action<string, GameActionFailure>? OnActionFailed { add { } remove { } }

        public Task<string> CreateGameAsync(CreateGameRequest request, GameCreationOptions? options = null, CancellationToken cancellationToken = default)
        {
            CapturedRequest = request;
            return Task.FromResult(options?.PreferredGameId ?? "game-1");
        }

        public Task<Boxcars.Engine.Persistence.GameState> GetCurrentStateAsync(string gameId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SynchronizeStateAsync(string gameId, Boxcars.Engine.Persistence.GameState state, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public bool IsGameBusy(string gameId) => false;
        public ValueTask EnqueueActionAsync(string gameId, PlayerAction action, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> UndoLastOperationAsync(string gameId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> UndoToEventAsync(string gameId, string targetEventRowKey, string targetDescription, string actorUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeTableClient : TableClient;

    private sealed class FakeHubContext : IHubContext<DashboardHub>
    {
        public IHubClients Clients { get; } = new FakeHubClients();
        public IGroupManager Groups { get; } = new FakeGroupManager();
    }

    private sealed class FakeHubClients : IHubClients
    {
        private static readonly IClientProxy Proxy = new FakeClientProxy();
        public IClientProxy All => Proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Client(string connectionId) => Proxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Proxy;
        public IClientProxy Group(string groupName) => Proxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => Proxy;
        public IClientProxy User(string userId) => Proxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => Proxy;
    }

    private sealed class FakeClientProxy : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
