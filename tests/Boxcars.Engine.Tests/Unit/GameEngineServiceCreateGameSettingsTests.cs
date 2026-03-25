using Azure;
using Azure.Core;
using Azure.Data.Tables;
using global::Boxcars.Data;
using global::Boxcars.Engine.Persistence;
using global::Boxcars.GameEngine;
using global::Boxcars.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Boxcars.Engine.Tests.Unit;

public class GameEngineServiceCreateGameSettingsTests
{
    [Fact]
    public async Task CreateGameAsync_PersistsDirectGameEntitySettingColumns()
    {
        var presenceService = new GamePresenceService();
        var gamesTable = new RecordingGamesTableClient();
        var service = new GameEngineService(
            new TestWebHostEnvironment(),
            gamesTable,
            presenceService,
            BotTurnServiceTestHarness.CreateService(presenceService),
            new GameSettingsResolver(),
            Options.Create(new BotOptions()),
            NullLogger<GameEngineService>.Instance);

        SetMapReady(service);
        SetMapDefinition(service, Boxcars.Engine.Tests.Fixtures.GameEngineFixture.CreateTestMap());

        var settings = GameSettings.Default with
        {
            StartingCash = 35_000,
            AnnouncingCash = 150_000,
            WinningCash = 325_000,
            RoverCash = 60_000,
            PublicFee = 1_500,
            PrivateFee = 500,
            UnfriendlyFee1 = 6_000,
            UnfriendlyFee2 = 12_000,
            HomeSwapping = false,
            HomeCityChoice = false,
            KeepCashSecret = false,
            StartEngine = Boxcars.Engine.Domain.LocomotiveType.Express,
            SuperchiefPrice = 45_000,
            ExpressPrice = 6_000
        };

        await service.CreateGameAsync(new CreateGameRequest
        {
            CreatorUserId = "creator@example.com",
            Players =
            [
                new GamePlayerSelection { UserId = "creator@example.com", DisplayName = "Alice", Color = "red" },
                new GamePlayerSelection { UserId = "bob@example.com", DisplayName = "Bob", Color = "blue" }
            ],
            Settings = settings
        }, new GameCreationOptions { PreferredGameId = "game-1" }, CancellationToken.None);

        var persistedGame = Assert.Single(gamesTable.GameEntities);
        Assert.Equal(35_000, persistedGame.StartingCash);
        Assert.Equal(150_000, persistedGame.AnnouncingCash);
        Assert.Equal(325_000, persistedGame.WinningCash);
        Assert.Equal(60_000, persistedGame.RoverCash);
        Assert.Equal(1_500, persistedGame.PublicFee);
        Assert.Equal(500, persistedGame.PrivateFee);
        Assert.Equal(6_000, persistedGame.UnfriendlyFee1);
        Assert.Equal(12_000, persistedGame.UnfriendlyFee2);
        Assert.False(persistedGame.HomeSwapping);
        Assert.False(persistedGame.HomeCityChoice);
        Assert.False(persistedGame.KeepCashSecret);
        Assert.Equal("Express", persistedGame.StartEngine);
        Assert.Equal(45_000, persistedGame.SuperchiefPrice);
        Assert.Equal(6_000, persistedGame.ExpressPrice);
        Assert.Equal(GameSettings.InitialSchemaVersion, persistedGame.SettingsSchemaVersion);
    }

    [Fact]
    public async Task CreateGameAsync_PersistsCityProbabilityOverrides()
    {
        var presenceService = new GamePresenceService();
        var gamesTable = new RecordingGamesTableClient();
        var service = new GameEngineService(
            new TestWebHostEnvironment(),
            gamesTable,
            presenceService,
            BotTurnServiceTestHarness.CreateService(presenceService),
            new GameSettingsResolver(),
            Options.Create(new BotOptions()),
            NullLogger<GameEngineService>.Instance);

        SetMapReady(service);
        SetMapDefinition(service, Boxcars.Engine.Tests.Fixtures.GameEngineFixture.CreateTestMap());

        var overrides = new[]
        {
            new CityProbabilityOverride { CityName = "New York", RegionCode = "NE", Probability = 0.15 },
            new CityProbabilityOverride { CityName = "Boston", RegionCode = "NE", Probability = 0.10 },
            new CityProbabilityOverride { CityName = "Miami", RegionCode = "SE", Probability = 0.35 },
            new CityProbabilityOverride { CityName = "Atlanta", RegionCode = "SE", Probability = 0.40 }
        };

        await service.CreateGameAsync(new CreateGameRequest
        {
            CreatorUserId = "creator@example.com",
            Players =
            [
                new GamePlayerSelection { UserId = "creator@example.com", DisplayName = "Alice", Color = "red" },
                new GamePlayerSelection { UserId = "bob@example.com", DisplayName = "Bob", Color = "blue" }
            ],
            CityProbabilityOverrides = overrides
        }, new GameCreationOptions { PreferredGameId = "game-1" }, CancellationToken.None);

        var persistedGame = Assert.Single(gamesTable.GameEntities);
        Assert.Collection(
            persistedGame.CityProbabilityOverrides.OrderBy(entry => entry.CityName, StringComparer.Ordinal),
            entry =>
            {
                Assert.Equal("Atlanta", entry.CityName);
                Assert.Equal("SE", entry.RegionCode);
                Assert.Equal(0.40, entry.Probability, 6);
            },
            entry =>
            {
                Assert.Equal("Boston", entry.CityName);
                Assert.Equal("NE", entry.RegionCode);
                Assert.Equal(0.10, entry.Probability, 6);
            },
            entry =>
            {
                Assert.Equal("Miami", entry.CityName);
                Assert.Equal("SE", entry.RegionCode);
                Assert.Equal(0.35, entry.Probability, 6);
            },
            entry =>
            {
                Assert.Equal("New York", entry.CityName);
                Assert.Equal("NE", entry.RegionCode);
                Assert.Equal(0.15, entry.Probability, 6);
            });
    }

    [Fact]
    public async Task CreateGameAsync_PersistsRailroadPriceOverrides()
    {
        var presenceService = new GamePresenceService();
        var gamesTable = new RecordingGamesTableClient();
        var service = new GameEngineService(
            new TestWebHostEnvironment(),
            gamesTable,
            presenceService,
            BotTurnServiceTestHarness.CreateService(presenceService),
            new GameSettingsResolver(),
            Options.Create(new BotOptions()),
            NullLogger<GameEngineService>.Instance);

        SetMapReady(service);
        SetMapDefinition(service, Boxcars.Engine.Tests.Fixtures.GameEngineFixture.CreateTestMap());

        var overrides = new[]
        {
            new RailroadPriceOverride { RailroadIndex = 0, PurchasePrice = 4_000 },
            new RailroadPriceOverride { RailroadIndex = 1, PurchasePrice = 57_500 }
        };

        await service.CreateGameAsync(new CreateGameRequest
        {
            CreatorUserId = "creator@example.com",
            Players =
            [
                new GamePlayerSelection { UserId = "creator@example.com", DisplayName = "Alice", Color = "red" },
                new GamePlayerSelection { UserId = "bob@example.com", DisplayName = "Bob", Color = "blue" }
            ],
            RailroadPriceOverrides = overrides
        }, new GameCreationOptions { PreferredGameId = "game-1" }, CancellationToken.None);

        var persistedGame = Assert.Single(gamesTable.GameEntities);
        Assert.Collection(
            persistedGame.RailroadPriceOverrides.OrderBy(entry => entry.RailroadIndex),
            entry =>
            {
                Assert.Equal(0, entry.RailroadIndex);
                Assert.Equal(4_000, entry.PurchasePrice);
            },
            entry =>
            {
                Assert.Equal(1, entry.RailroadIndex);
                Assert.Equal(57_500, entry.PurchasePrice);
            });
    }

    private static void SetMapDefinition(GameEngineService service, Boxcars.Engine.Data.Maps.MapDefinition mapDefinition)
    {
        var field = typeof(GameEngineService).GetField("_mapDefinition", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?? throw new InvalidOperationException("_mapDefinition field was not found.");
        field.SetValue(service, mapDefinition);
    }

    private static void SetMapReady(GameEngineService service)
    {
        var field = typeof(GameEngineService).GetField("_mapReady", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?? throw new InvalidOperationException("_mapReady field was not found.");
        var source = (TaskCompletionSource)(field.GetValue(service) ?? throw new InvalidOperationException("_mapReady was null."));
        source.TrySetResult();
    }

    private sealed class RecordingGamesTableClient : TableClient
    {
        public List<GameEntity> GameEntities { get; } = [];
        public List<GameEventEntity> GameEvents { get; } = [];

        public override Task<Response> AddEntityAsync<T>(T entity, CancellationToken cancellationToken = default)
        {
            if (entity is GameEntity gameEntity)
            {
                GameEntities.Add(gameEntity);
            }
            else if (entity is GameEventEntity gameEvent)
            {
                GameEvents.Add(gameEvent);
            }

            return Task.FromResult<Response>(new FakeResponse(204));
        }

        public override Task<Response<T>> GetEntityAsync<T>(string partitionKey, string rowKey, IEnumerable<string>? select = null, CancellationToken cancellationToken = default)
        {
            if (typeof(T) == typeof(GameEntity))
            {
                var gameEntity = GameEntities.Single(entity =>
                    string.Equals(entity.PartitionKey, partitionKey, StringComparison.Ordinal)
                    && string.Equals(entity.RowKey, rowKey, StringComparison.Ordinal));
                return Task.FromResult(Response.FromValue((T)(ITableEntity)gameEntity, new FakeResponse(200)));
            }

            throw new NotSupportedException($"Unsupported entity type: {typeof(T).Name}");
        }

        public override AsyncPageable<T> QueryAsync<T>(string? filter = null, int? maxPerPage = null, IEnumerable<string>? select = null, CancellationToken cancellationToken = default)
        {
            if (typeof(T) == typeof(GameEventEntity))
            {
                return AsyncPageable<T>.FromPages([Page<T>.FromValues(GameEvents.Cast<T>().ToList(), null, new FakeResponse(200))]);
            }

            return AsyncPageable<T>.FromPages([Page<T>.FromValues([], null, new FakeResponse(200))]);
        }
    }

    private sealed class FakeResponse(int status) : Response
    {
        public override int Status => status;
        public override string ReasonPhrase => string.Empty;
        public override Stream? ContentStream { get; set; }
        public override string ClientRequestId { get; set; } = Guid.NewGuid().ToString("N");
        public override void Dispose() { }
        protected override bool ContainsHeader(string name) => false;
        protected override IEnumerable<HttpHeader> EnumerateHeaders() { yield break; }
        protected override bool TryGetHeader(string name, out string value) { value = string.Empty; return false; }
        protected override bool TryGetHeaderValues(string name, out IEnumerable<string> values) { values = []; return false; }
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Boxcars.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
