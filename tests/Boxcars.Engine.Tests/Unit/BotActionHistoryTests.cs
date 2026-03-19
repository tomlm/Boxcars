using System.Reflection;
using System.Text.Json;
using Azure.Data.Tables;
using Boxcars.Data;
using Boxcars.Engine.Domain;
using Boxcars.GameEngine;
using Boxcars.Engine.Tests.Fixtures;
using Boxcars.Services;
using Boxcars.Services.Maps;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Boxcars.Engine.Tests.Unit;

public class BotActionHistoryTests
{
    [Fact]
    public void SerializeEventData_PlayerActionWithBotMetadata_PreservesBotAttribution()
    {
        var action = new ChooseDestinationRegionAction
        {
            PlayerId = "Alice",
            PlayerIndex = 0,
            ActorUserId = "controller@example.com",
            SelectedRegionCode = "SE",
            BotMetadata = new BotRecordedActionMetadata
            {
                BotDefinitionId = "bot-1",
                BotName = "El Cheapo",
                ControllerMode = SeatControllerModes.AiGhost,
                DecisionSource = "Fallback",
                FallbackReason = "Timeout"
            }
        };

        var payload = GameEventSerialization.SerializeEventData(action);

        Assert.Contains("\"BotMetadata\"", payload, StringComparison.Ordinal);
        Assert.Contains("\"BotDefinitionId\":\"bot-1\"", payload, StringComparison.Ordinal);
        Assert.Contains("\"BotName\":\"El Cheapo\"", payload, StringComparison.Ordinal);
        Assert.Contains("\"ControllerMode\":\"AiGhost\"", payload, StringComparison.Ordinal);
        Assert.Contains("\"DecisionSource\":\"Fallback\"", payload, StringComparison.Ordinal);
        Assert.Contains("\"FallbackReason\":\"Timeout\"", payload, StringComparison.Ordinal);
    }

    [Fact]
    public void DeserializePlayerAction_PlayerActionWithBotMetadata_RestoresServerActorAndBotAttribution()
    {
        var action = new ChooseDestinationRegionAction
        {
            PlayerId = "Alice",
            PlayerIndex = 0,
            ActorUserId = BotOptions.DefaultServerActorUserId,
            SelectedRegionCode = "SE",
            BotMetadata = new BotRecordedActionMetadata
            {
                BotDefinitionId = "bot-1",
                BotName = "El Cheapo",
                ControllerMode = SeatControllerModes.AiGhost,
                DecisionSource = "Fallback",
                FallbackReason = "Timeout"
            }
        };

        var payload = GameEventSerialization.SerializeEventData(action);

        var restored = Assert.IsType<ChooseDestinationRegionAction>(
            GameEventSerialization.DeserializePlayerAction(nameof(ChooseDestinationRegionAction), payload));

        Assert.Equal(BotOptions.DefaultServerActorUserId, restored.ActorUserId);
        Assert.NotNull(restored.BotMetadata);
        Assert.Equal("bot-1", restored.BotMetadata!.BotDefinitionId);
        Assert.Equal("El Cheapo", restored.BotMetadata.BotName);
        Assert.Equal(SeatControllerModes.AiGhost, restored.BotMetadata.ControllerMode);
        Assert.Equal("Fallback", restored.BotMetadata.DecisionSource);
        Assert.Equal("Timeout", restored.BotMetadata.FallbackReason);
        Assert.True(restored.IsServerAuthoredAiAction);
    }

    [Fact]
    public void DescribeAction_GhostDrivenRegionChoice_AppendsAutoAttributionSuffix()
    {
        var service = CreateGameEngineServiceForTests();
        var (engine, random) = GameEngineFixture.CreateTestEngine();

        random.QueueWeightedDraw(0);
        random.QueueWeightedDraw(1);
        engine.DrawDestination();

        var snapshotBeforeAction = engine.ToSnapshot();
        var action = new ChooseDestinationRegionAction
        {
            PlayerId = engine.CurrentTurn.ActivePlayer.Name,
            PlayerIndex = engine.CurrentTurn.ActivePlayer.Index,
            ActorUserId = "controller@example.com",
            SelectedRegionCode = "SE",
            BotMetadata = new BotRecordedActionMetadata
            {
                BotDefinitionId = "bot-1",
                BotName = "El Cheapo",
                ControllerMode = SeatControllerModes.AiGhost,
                DecisionSource = "Fallback",
                FallbackReason = "Timeout"
            }
        };

        random.QueueWeightedDraw(1);
        engine.ChooseDestinationRegion("SE");
        var snapshotAfterAction = engine.ToSnapshot();

        var summary = InvokeDescribeAction(service, CreateGameEntity(), action, snapshotBeforeAction, snapshotAfterAction, engine);

        Assert.Equal("Alice chose SE as the replacement destination region and received Atlanta. [AUTO; Timeout]", summary);
    }

    [Fact]
    public void DescribeAction_DedicatedBotDrivenRegionChoice_DropsAttributionSuffix()
    {
        var service = CreateGameEngineServiceForTests();
        var (engine, random) = GameEngineFixture.CreateTestEngine();

        random.QueueWeightedDraw(0);
        random.QueueWeightedDraw(1);
        engine.DrawDestination();

        var snapshotBeforeAction = engine.ToSnapshot();
        var action = new ChooseDestinationRegionAction
        {
            PlayerId = engine.CurrentTurn.ActivePlayer.Name,
            PlayerIndex = engine.CurrentTurn.ActivePlayer.Index,
            ActorUserId = BotOptions.DefaultServerActorUserId,
            SelectedRegionCode = "SE",
            BotMetadata = new BotRecordedActionMetadata
            {
                BotDefinitionId = "bot-1",
                BotName = "El Cheapo",
                ControllerMode = SeatControllerModes.AiBotSeat,
                DecisionSource = "Fallback",
                FallbackReason = "Timeout"
            }
        };

        random.QueueWeightedDraw(1);
        engine.ChooseDestinationRegion("SE");
        var snapshotAfterAction = engine.ToSnapshot();

        var summary = InvokeDescribeAction(service, CreateGameEntity(), action, snapshotBeforeAction, snapshotAfterAction, engine);

        Assert.Equal("Alice chose SE as the replacement destination region and received Atlanta.", summary);
    }

    [Fact]
    public void DescribeAction_DelegatedManualMove_AppendsControllerDisplayName()
    {
        var presenceService = new GamePresenceService();
        Assert.True(presenceService.TryTakeDelegatedControl("game-1", "alice@example.com", "tom@example.com"));

        var service = CreateGameEngineServiceForTests(presenceService);
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Move);

        var snapshotBeforeAction = engine.ToSnapshot();
        var action = new MoveAction
        {
            PlayerId = engine.CurrentTurn.ActivePlayer.Name,
            PlayerIndex = engine.CurrentTurn.ActivePlayer.Index,
            ActorUserId = "tom@example.com",
            PointsTaken = ["albany", "boston"],
            SelectedSegmentKeys = ["albany|boston|0"]
        };

        engine.MoveAlongRoute(1);
        var snapshotAfterAction = engine.ToSnapshot();

        var summary = InvokeDescribeAction(service, CreateGameEntity(), action, snapshotBeforeAction, snapshotAfterAction, engine);

        Assert.Equal("Alice moved 1 space [TOM]", summary);
    }

    [Fact]
    public void BuildTimelineItems_MoveEvent_UsesPersistedBotSummary()
    {
        var (engine, random) = GameEngineFixture.CreateTestEngine();
        GameEngineFixture.AdvanceToPhase(engine, random, TurnPhase.Move);

        engine.MoveAlongRoute(1);
        var snapshot = engine.ToSnapshot();
        const string expectedSummary = "Alice moved to Boston. [SuggestedRoute]";

        var gameEvent = new GameEventEntity
        {
            PartitionKey = "game-1",
            RowKey = "Event_0000000001",
            GameId = "game-1",
            EventKind = "Move",
            ChangeSummary = expectedSummary,
            SerializedGameState = JsonSerializer.Serialize(snapshot),
            OccurredUtc = DateTimeOffset.UtcNow,
            ActingPlayerIndex = 0,
            CreatedBy = "alice@example.com"
        };

        var timelineItems = InvokeBuildTimelineItems(gameEvent, null);

        Assert.Contains(timelineItems, item => item.EventKind == EventTimelineKind.Move && item.Description == expectedSummary);
    }

    [Fact]
    public void BuildLatestBotAssignments_PrefersLatestAssignmentStatusPerPlayer()
    {
        var mapper = new GameBoardStateMapper(
            new NetworkCoverageService(),
            new MapAnalysisService(new MapRouteService()),
            new PurchaseRecommendationService(),
            Options.Create(new PurchaseRulesOptions()));

        var game = new GameEntity
        {
            PartitionKey = "game-1",
            GameId = "game-1",
            BotAssignmentsJson = BotAssignmentSerialization.Serialize(
            [
                new BotAssignment
                {
                    GameId = "game-1",
                    PlayerUserId = "alice@example.com",
                    ControllerUserId = "bob@example.com",
                    BotDefinitionId = "bot-old",
                    AssignedUtc = new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero),
                    Status = BotAssignmentStatuses.Active
                },
                new BotAssignment
                {
                    GameId = "game-1",
                    PlayerUserId = "alice@example.com",
                    ControllerUserId = "bob@example.com",
                    BotDefinitionId = "bot-new",
                    AssignedUtc = new DateTimeOffset(2026, 3, 16, 11, 0, 0, TimeSpan.Zero),
                    ClearedUtc = new DateTimeOffset(2026, 3, 16, 11, 5, 0, TimeSpan.Zero),
                    Status = BotAssignmentStatuses.MissingDefinition,
                    ClearReason = "The assigned bot definition no longer exists."
                },
                new BotAssignment
                {
                    GameId = "game-1",
                    PlayerUserId = "charlie@example.com",
                    ControllerUserId = "bob@example.com",
                    BotDefinitionId = "bot-charlie",
                    AssignedUtc = new DateTimeOffset(2026, 3, 16, 11, 10, 0, TimeSpan.Zero),
                    Status = BotAssignmentStatuses.Active
                }
            ])
        };

        var assignments = mapper.BuildLatestBotAssignments(game);

        Assert.Equal(2, assignments.Count);
        Assert.Equal("bot-new", assignments["alice@example.com"].BotDefinitionId);
        Assert.Equal(BotAssignmentStatuses.MissingDefinition, assignments["alice@example.com"].Status);
        Assert.Equal("Bot removed from library", GameBoardStateMapper.GetBotAssignmentStatusLabel(assignments["alice@example.com"]));
        Assert.Equal("El Cheapo", GameBoardStateMapper.GetBotAssignmentStatusLabel(assignments["charlie@example.com"], "El Cheapo"));
    }

    [Fact]
    public void BuildPlayerControlBindings_ActiveBotAssignment_ProjectsControllerMode()
    {
        var mapper = new GameBoardStateMapper(
            new NetworkCoverageService(),
            new MapAnalysisService(new MapRouteService()),
            new PurchaseRecommendationService(),
            Options.Create(new PurchaseRulesOptions()));

        var game = new GameEntity
        {
            PartitionKey = "game-1",
            GameId = "game-1",
            PlayersJson = GamePlayerSelectionSerialization.Serialize(
            [
                new GamePlayerSelection { UserId = "alice@example.com", DisplayName = "Alice", Color = "#111111" },
                new GamePlayerSelection { UserId = "bob@example.com", DisplayName = "Bob", Color = "#222222" }
            ]),
            BotAssignmentsJson = BotAssignmentSerialization.Serialize(
            [
                new BotAssignment
                {
                    GameId = "game-1",
                    PlayerUserId = "alice@example.com",
                    ControllerMode = SeatControllerModes.AiBotSeat,
                    BotDefinitionId = "bot-1",
                    Status = BotAssignmentStatuses.Active
                }
            ])
        };

        var bindings = mapper.BuildPlayerControlBindings(game, "bob@example.com");

        Assert.Equal(SeatControllerModes.AiBotSeat, bindings[0].ControllerMode);
        Assert.True(bindings[0].HasBotAssignment);
        Assert.Equal("bot-1", bindings[0].BotDefinitionId);
    }

    private static string InvokeDescribeAction(
        GameEngineService service,
        GameEntity gameEntity,
        PlayerAction action,
        Boxcars.Engine.Persistence.GameState snapshotBeforeAction,
        Boxcars.Engine.Persistence.GameState snapshotAfterAction,
        Boxcars.Engine.Domain.GameEngine engine)
    {
        var method = typeof(GameEngineService).GetMethod("DescribeAction", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("DescribeAction was not found.");

        return (string)(method.Invoke(service, [gameEntity, action, snapshotBeforeAction, snapshotAfterAction, engine])
            ?? throw new InvalidOperationException("DescribeAction returned null."));
    }

    private static GameEntity CreateGameEntity()
    {
        return new GameEntity
        {
            PartitionKey = "game-1",
            RowKey = "GAME",
            GameId = "game-1",
            PlayersJson = GamePlayerSelectionSerialization.Serialize(
            [
                new GamePlayerSelection { UserId = "alice@example.com", DisplayName = "Alice", Color = "#111111" },
                new GamePlayerSelection { UserId = "tom@example.com", DisplayName = "TOM", Color = "#222222" }
            ])
        };
    }

    private static GameEngineService CreateGameEngineServiceForTests()
    {
        return CreateGameEngineServiceForTests(new GamePresenceService());
    }

    private static GameEngineService CreateGameEngineServiceForTests(GamePresenceService presenceService)
    {
        return new GameEngineService(
            new TestWebHostEnvironment(),
            new TableServiceClient(new Uri("https://example.com"), new TableSharedKeyCredential("devstoreaccount1", Convert.ToBase64String(new byte[32]))),
            presenceService,
            Options.Create(new BotOptions()),
            Options.Create(new PurchaseRulesOptions()),
            NullLogger<GameEngineService>.Instance);
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

    private static IReadOnlyList<EventTimelineItem> InvokeBuildTimelineItems(GameEventEntity gameEvent, GameEventEntity? previousGameEvent)
    {
        var method = typeof(GameService).GetMethod("BuildTimelineItems", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("BuildTimelineItems was not found.");

        return (IReadOnlyList<EventTimelineItem>)(method.Invoke(null, [gameEvent, previousGameEvent])
            ?? throw new InvalidOperationException("BuildTimelineItems returned null."));
    }
}