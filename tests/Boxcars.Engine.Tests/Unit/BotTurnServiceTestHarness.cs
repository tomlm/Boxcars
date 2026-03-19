using System.Net;
using System.Text;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Boxcars.Data;
using Boxcars.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Boxcars.Engine.Tests.Unit;

internal static class BotTurnServiceTestHarness
{
    public const string GameId = "game-1";
    public const string ActivePlayerUserId = "alice@example.com";
    public const string ControllerUserId = "controller@example.com";
    public const string OtherPlayerUserId = "bob@example.com";
    public const string ServerActorUserId = BotOptions.DefaultServerActorUserId;
    public const string ServerActorDisplayName = BotOptions.DefaultServerActorDisplayName;

    public static BotTurnService CreateService(GamePresenceService presenceService, params BotStrategyDefinitionEntity[] botDefinitions)
    {
        return CreateService(presenceService, new FakeHttpClientFactory(), [], botDefinitions);
    }

    public static BotTurnService CreateServiceWithUsers(
        GamePresenceService presenceService,
        IReadOnlyList<ApplicationUser> users,
        params BotStrategyDefinitionEntity[] botDefinitions)
    {
        return CreateService(presenceService, new FakeHttpClientFactory(), users, botDefinitions);
    }

    public static (BotTurnService Service, RecordingHttpMessageHandler Handler) CreateServiceWithOpenAiSelection(
        GamePresenceService presenceService,
        string selectedOptionId,
        params BotStrategyDefinitionEntity[] botDefinitions)
    {
        var handler = new RecordingHttpMessageHandler(BuildSelectedOptionResponse(selectedOptionId), HttpStatusCode.OK);
        return (CreateService(presenceService, new FakeHttpClientFactory(handler), [], botDefinitions), handler);
    }

    private static BotTurnService CreateService(
        GamePresenceService presenceService,
        IHttpClientFactory httpClientFactory,
        IReadOnlyList<ApplicationUser> users,
        params BotStrategyDefinitionEntity[] botDefinitions)
    {
        var userDirectoryService = new UserDirectoryService(new FakeBotTableClient(users, botDefinitions));

        var botOptions = Options.Create(new BotOptions
        {
            OpenAIKey = "test-key",
            OpenAIModel = "test-model",
            DecisionTimeoutSeconds = 1,
            ServerActorUserId = ServerActorUserId,
            ServerActorDisplayName = ServerActorDisplayName
        });

        return new BotTurnService(
            userDirectoryService,
            new BotDecisionPromptBuilder(),
            new OpenAiBotClient(httpClientFactory, botOptions, NullLogger<OpenAiBotClient>.Instance),
            presenceService,
            new NetworkCoverageService(),
            botOptions,
            Options.Create(new PurchaseRulesOptions()),
            NullLogger<BotTurnService>.Instance);
    }

    public static GameEntity CreateAssignedGame(
        IReadOnlyList<GamePlayerSelection> selections,
        string playerUserId,
        string controllerUserId,
        string botDefinitionId)
    {
        return new GameEntity
        {
            PartitionKey = GameId,
            RowKey = "GAME",
            GameId = GameId,
            PlayersJson = GamePlayerSelectionSerialization.Serialize(selections),
            BotAssignmentsJson = BotAssignmentSerialization.Serialize(
            [
                new BotAssignment
                {
                    GameId = GameId,
                    PlayerUserId = playerUserId,
                    ControllerUserId = controllerUserId,
                    ControllerMode = SeatControllerModes.AI,
                    BotDefinitionId = botDefinitionId,
                    Status = BotAssignmentStatuses.Active
                }
            ])
        };
    }

    public static GameEntity CreateDedicatedBotSeatGame(
        IReadOnlyList<GamePlayerSelection> selections,
        string playerUserId,
        string botDefinitionId)
    {
        return new GameEntity
        {
            PartitionKey = GameId,
            RowKey = "GAME",
            GameId = GameId,
            PlayersJson = GamePlayerSelectionSerialization.Serialize(selections),
            BotAssignmentsJson = BotAssignmentSerialization.Serialize(
            [
                CreateDedicatedBotAssignment(playerUserId, botDefinitionId)
            ])
        };
    }

    public static GameEntity CreateBotControlledGame(
        IReadOnlyList<GamePlayerSelection> selections,
        string playerUserId,
        string controllerUserId,
        string botDefinitionId)
    {
        return new GameEntity
        {
            PartitionKey = GameId,
            RowKey = "GAME",
            GameId = GameId,
            PlayersJson = GamePlayerSelectionSerialization.Serialize(selections),
            BotAssignmentsJson = BotAssignmentSerialization.Serialize(
            [
                CreateBotAssignment(playerUserId, controllerUserId, botDefinitionId)
            ])
        };
    }

    public static BotAssignment CreateDedicatedBotAssignment(string playerUserId, string botDefinitionId)
    {
        return new BotAssignment
        {
            GameId = GameId,
            PlayerUserId = playerUserId,
            ControllerMode = SeatControllerModes.AI,
            BotDefinitionId = botDefinitionId,
            Status = BotAssignmentStatuses.Active
        };
    }

    public static BotAssignment CreateBotAssignment(string playerUserId, string controllerUserId, string botDefinitionId)
    {
        return new BotAssignment
        {
            GameId = GameId,
            PlayerUserId = playerUserId,
            ControllerUserId = controllerUserId,
            ControllerMode = SeatControllerModes.AI,
            BotDefinitionId = botDefinitionId,
            Status = BotAssignmentStatuses.Active
        };
    }

    public static void ConfigureDelegatedControl(GamePresenceService presenceService, string playerUserId, string controllerUserId)
    {
        presenceService.SetMockConnectionState(GameId, controllerUserId, isConnected: true);
        presenceService.SetMockConnectionState(GameId, playerUserId, isConnected: false);

        if (!presenceService.TryTakeDelegatedControl(GameId, playerUserId, controllerUserId))
        {
            throw new InvalidOperationException("Expected delegated control to be granted for test setup.");
        }
    }

    public static IReadOnlyList<GamePlayerSelection> CreateSelections(params string[] userIds)
    {
        return userIds
            .Select((userId, index) => new GamePlayerSelection
            {
                UserId = userId,
                DisplayName = index switch
                {
                    0 => "Alice",
                    1 => "Bob",
                    2 => "Charlie",
                    _ => $"Player {index + 1}"
                },
                Color = $"#{index + 1}{index + 1}{index + 1}{index + 1}{index + 1}{index + 1}"
            })
            .ToList();
    }

    public static BotStrategyDefinitionEntity CreateBotDefinition(string botDefinitionId = "bot-1", string name = "El Cheapo")
    {
        return new BotStrategyDefinitionEntity
        {
            BotDefinitionId = botDefinitionId,
            Name = name,
            StrategyText = "Always choose a legal option.",
            CreatedByUserId = ControllerUserId,
            ModifiedByUserId = ControllerUserId,
            CreatedUtc = new DateTimeOffset(2026, 3, 16, 0, 0, 0, TimeSpan.Zero),
            ModifiedUtc = new DateTimeOffset(2026, 3, 16, 0, 0, 0, TimeSpan.Zero)
        };
    }

    public static ApplicationUser CreateUser(
        string userId,
        string name = "Test User",
        string? nickname = null,
        string? strategyText = null,
        bool isBot = false)
    {
        return new ApplicationUser
        {
            PartitionKey = "USER",
            RowKey = userId,
            Email = userId,
            NormalizedEmail = userId.ToUpperInvariant(),
            UserName = userId,
            NormalizedUserName = userId.ToUpperInvariant(),
            Name = name,
            Nickname = nickname ?? name,
            NormalizedNickname = (nickname ?? name).ToUpperInvariant(),
            StrategyText = strategyText ?? PlayerProfileService.DefaultStrategyText,
            IsBot = isBot,
            CreatedByUserId = ControllerUserId,
            CreatedUtc = new DateTimeOffset(2026, 3, 16, 0, 0, 0, TimeSpan.Zero),
            ModifiedByUserId = ControllerUserId,
            ModifiedUtc = new DateTimeOffset(2026, 3, 16, 0, 0, 0, TimeSpan.Zero)
        };
    }

    private static TableServiceClient CreateTableServiceClient()
    {
        return new TableServiceClient(
            new Uri("https://example.com"),
            new TableSharedKeyCredential("devstoreaccount1", Convert.ToBase64String(new byte[32])));
    }

    private sealed class FakeBotTableClient : TableClient
    {
        private readonly Dictionary<(string PartitionKey, string RowKey), ITableEntity> _entities;

        public FakeBotTableClient(IEnumerable<ApplicationUser> users, IEnumerable<BotStrategyDefinitionEntity> botDefinitions)
        {
            _entities = users.ToDictionary<ApplicationUser, (string PartitionKey, string RowKey), ITableEntity>(
                user => ("USER", user.RowKey),
                user => user,
                EqualityComparer<(string PartitionKey, string RowKey)>.Default);

            foreach (var definition in botDefinitions)
            {
                _entities[("USER", definition.BotDefinitionId)] = new ApplicationUser
                {
                    PartitionKey = "USER",
                    RowKey = definition.BotDefinitionId,
                    Email = definition.BotDefinitionId,
                    NormalizedEmail = definition.BotDefinitionId.ToUpperInvariant(),
                    UserName = definition.BotDefinitionId,
                    NormalizedUserName = definition.BotDefinitionId.ToUpperInvariant(),
                    Name = definition.Name,
                    Nickname = definition.Name,
                    NormalizedNickname = definition.Name.ToUpperInvariant(),
                    StrategyText = definition.StrategyText,
                    IsBot = true,
                    CreatedByUserId = definition.CreatedByUserId,
                    CreatedUtc = definition.CreatedUtc,
                    ModifiedByUserId = definition.ModifiedByUserId,
                    ModifiedUtc = definition.ModifiedUtc
                };
            }
        }

        public override Task<Response<T>> GetEntityAsync<T>(
            string partitionKey,
            string rowKey,
            IEnumerable<string>? select = null,
            CancellationToken cancellationToken = default)
        {
            if (!_entities.TryGetValue((partitionKey, rowKey), out var entity))
            {
                throw new RequestFailedException((int)HttpStatusCode.NotFound, "Entity was not found.");
            }

            return Task.FromResult(Response.FromValue((T)entity, new FakeResponse((int)HttpStatusCode.OK)));
        }
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public FakeHttpClientFactory(HttpMessageHandler? handler = null)
        {
            _handler = handler ?? new FakeHttpMessageHandler();
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(_handler, disposeHandler: false);
        }
    }

    internal sealed class RecordingHttpMessageHandler(string responseBody, HttpStatusCode statusCode) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("{}")
            });
        }
    }

    private static string BuildSelectedOptionResponse(string selectedOptionId)
    {
        return "{\"choices\":[{\"message\":{\"content\":\"{\\\"selectedOptionId\\\":\\\""
            + selectedOptionId
            + "\\\"}\"}}]}";
    }

    private sealed class FakeResponse(int status) : Response
    {
        public override int Status { get; } = status;

        public override string ReasonPhrase => string.Empty;

        public override Stream? ContentStream { get; set; }

        public override string ClientRequestId { get; set; } = string.Empty;

        public override void Dispose()
        {
        }

        protected override bool ContainsHeader(string name)
        {
            return false;
        }

        protected override IEnumerable<HttpHeader> EnumerateHeaders()
        {
            yield break;
        }

        protected override bool TryGetHeader(string name, out string value)
        {
            value = string.Empty;
            return false;
        }

        protected override bool TryGetHeaderValues(string name, out IEnumerable<string> values)
        {
            values = [];
            return false;
        }
    }
}