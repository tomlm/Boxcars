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
                ControllerMode = SeatControllerModes.AI,
                DecisionSource = "Fallback",
                FallbackReason = "Timeout"
            }
        };

        var payload = GameEventSerialization.SerializeEventData(action);

        Assert.Contains("\"BotMetadata\"", payload, StringComparison.Ordinal);
        Assert.Contains("\"BotDefinitionId\":\"bot-1\"", payload, StringComparison.Ordinal);
        Assert.Contains("\"BotName\":\"El Cheapo\"", payload, StringComparison.Ordinal);
        Assert.Contains("\"ControllerMode\":\"AI\"", payload, StringComparison.Ordinal);
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
                ControllerMode = SeatControllerModes.AI,
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
        Assert.Equal(SeatControllerModes.AI, restored.BotMetadata.ControllerMode);
        Assert.Equal("Fallback", restored.BotMetadata.DecisionSource);
        Assert.Equal("Timeout", restored.BotMetadata.FallbackReason);
        Assert.True(restored.IsServerAuthoredAiAction);
    }

    [Fact]
    public void DescribeAction_BotDrivenRegionChoice_AppendsAutoAttributionSuffix()
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
                ControllerMode = SeatControllerModes.AI,
                DecisionSource = "Fallback",
                FallbackReason = "Timeout"
            }
        };

        random.QueueWeightedDraw(1);
        engine.ChooseDestinationRegion("SE");
        var snapshotAfterAction = engine.ToSnapshot();

        var summary = InvokeDescribeAction(service, CreateGameEntity(), CreatePlayerStates(), action, snapshotBeforeAction, snapshotAfterAction, engine);

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
                ControllerMode = SeatControllerModes.AI,
                IsBotPlayer = true,
                DecisionSource = "Fallback",
                FallbackReason = "Timeout"
            }
        };

        random.QueueWeightedDraw(1);
        engine.ChooseDestinationRegion("SE");
        var snapshotAfterAction = engine.ToSnapshot();

        var summary = InvokeDescribeAction(service, CreateGameEntity(), CreatePlayerStates(), action, snapshotBeforeAction, snapshotAfterAction, engine);

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

        var summary = InvokeDescribeAction(service, CreateGameEntity(), CreatePlayerStates(), action, snapshotBeforeAction, snapshotAfterAction, engine);

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
    public void BuildTimelineItems_CashAnnouncement_FiresOnceUntilPlayerDropsBelowThreshold()
    {
        var firstCrossingEvent = CreateTimelineEvent(
            "Event_0000000001",
            CreateSnapshot(240_000),
            CreateSnapshot(250_000));
        var repeatWhileAboveEvent = CreateTimelineEvent(
            "Event_0000000002",
            CreateSnapshot(250_000),
            CreateSnapshot(265_000));
        var recrossingEvent = CreateTimelineEvent(
            "Event_0000000003",
            CreateSnapshot(245_000),
            CreateSnapshot(255_000));

        var firstCrossingItems = InvokeBuildTimelineItems(firstCrossingEvent.currentEvent, firstCrossingEvent.previousEvent);
        var repeatItems = InvokeBuildTimelineItems(repeatWhileAboveEvent.currentEvent, repeatWhileAboveEvent.previousEvent);
        var recrossingItems = InvokeBuildTimelineItems(recrossingEvent.currentEvent, recrossingEvent.previousEvent);

        Assert.Contains(firstCrossingItems, item => item.EventKind == EventTimelineKind.CashAnnouncement && item.Description == "Player Alice announces they have $250,000.");
        Assert.DoesNotContain(repeatItems, item => item.EventKind == EventTimelineKind.CashAnnouncement);
        Assert.Contains(recrossingItems, item => item.EventKind == EventTimelineKind.CashAnnouncement && item.Description == "Player Alice announces they have $255,000.");
    }

    [Fact]
    public void BuildTimelineItems_CashAnnouncement_UsesConfiguredAnnouncementThreshold()
    {
        const int announcingCash = 300_000;
        var belowThresholdEvent = CreateTimelineEvent(
            "Event_0000000001",
            CreateSnapshot(250_000),
            CreateSnapshot(275_000));
        var crossingEvent = CreateTimelineEvent(
            "Event_0000000002",
            CreateSnapshot(295_000),
            CreateSnapshot(300_000));

        var belowThresholdItems = InvokeBuildTimelineItems(
            belowThresholdEvent.currentEvent,
            belowThresholdEvent.previousEvent,
            announcingCash);
        var crossingItems = InvokeBuildTimelineItems(
            crossingEvent.currentEvent,
            crossingEvent.previousEvent,
            announcingCash);

        Assert.DoesNotContain(belowThresholdItems, item => item.EventKind == EventTimelineKind.CashAnnouncement);
        Assert.Contains(crossingItems, item => item.EventKind == EventTimelineKind.CashAnnouncement && item.Description == "Player Alice announces they have $300,000.");
    }

    [Fact]
    public void BuildTimelineItems_RoverEvent_AddsRoverAndAlternateDestinationNotifications()
    {
        var previousSnapshot = CreateSnapshot(300_000);
        previousSnapshot.Players[0].HasDeclared = true;
        previousSnapshot.Players[0].DestinationCityName = "New York";
        previousSnapshot.Players[0].AlternateDestinationCityName = "Atlanta";
        previousSnapshot.Players[1].Cash = 50_000;

        var currentSnapshot = CreateSnapshot(250_000);
        currentSnapshot.Players[0].HasDeclared = false;
        currentSnapshot.Players[0].DestinationCityName = "Atlanta";
        currentSnapshot.Players[0].AlternateDestinationCityName = null;
        currentSnapshot.Players[1].Cash = 100_000;

        var roverEvent = CreateTimelineEvent(
            "Event_0000000004",
            previousSnapshot,
            currentSnapshot);

        var timelineItems = InvokeBuildTimelineItems(roverEvent.currentEvent, roverEvent.previousEvent);

        Assert.Contains(timelineItems, item => item.Description == "Bob rovered Alice for $50,000.");
        Assert.Contains(timelineItems, item => item.Description == "Alice must go to to alternate destination Atlanta.");
    }

    [Fact]
    public void BuildLatestBotControlStates_PrefersLatestControlStatusPerPlayer()
    {
        var mapper = new GameBoardStateMapper(
            new NetworkCoverageService(),
            new MapAnalysisService(new MapRouteService()),
            new PurchaseRecommendationService());

        var controlStates = mapper.BuildLatestBotControlStates(
        [
            new GamePlayerStateEntity
            {
                GameId = "game-1",
                SeatIndex = 0,
                PlayerUserId = "alice@example.com",
                ControllerUserId = "bob@example.com",
                BotDefinitionId = "bot-new",
                BotControlActivatedUtc = new DateTimeOffset(2026, 3, 16, 11, 0, 0, TimeSpan.Zero),
                BotControlClearedUtc = new DateTimeOffset(2026, 3, 16, 11, 5, 0, TimeSpan.Zero),
                BotControlStatus = BotControlStatuses.MissingDefinition,
                BotControlClearReason = "The assigned bot definition no longer exists."
            },
            new GamePlayerStateEntity
            {
                GameId = "game-1",
                SeatIndex = 1,
                PlayerUserId = "charlie@example.com",
                ControllerUserId = "bob@example.com",
                BotDefinitionId = "bot-charlie",
                BotControlActivatedUtc = new DateTimeOffset(2026, 3, 16, 11, 10, 0, TimeSpan.Zero),
                BotControlStatus = BotControlStatuses.Active
            }
        ]);

        Assert.Equal(2, controlStates.Count);
        Assert.Equal("bot-new", controlStates["alice@example.com"].BotDefinitionId);
        Assert.Equal(BotControlStatuses.MissingDefinition, controlStates["alice@example.com"].BotControlStatus);
        Assert.Equal("Bot removed from library", GameBoardStateMapper.GetBotControlStatusLabel(controlStates["alice@example.com"]));
        Assert.Equal("El Cheapo", GameBoardStateMapper.GetBotControlStatusLabel(controlStates["charlie@example.com"], "El Cheapo"));
    }

    [Fact]
    public void BuildPlayerControlBindings_ActiveBotControl_ProjectsControllerMode()
    {
        var mapper = new GameBoardStateMapper(
            new NetworkCoverageService(),
            new MapAnalysisService(new MapRouteService()),
            new PurchaseRecommendationService());

        var playerStates = BotTurnServiceTestHarness.CreateDedicatedBotSeatPlayerStates(
        [
            new GamePlayerSelection { UserId = "alice@example.com", DisplayName = "Alice", Color = "#111111" },
            new GamePlayerSelection { UserId = "bob@example.com", DisplayName = "Bob", Color = "#222222" }
        ],
        "alice@example.com",
        "bot-1");

        var bindings = mapper.BuildPlayerControlBindings("game-1", playerStates, "bob@example.com");

        Assert.Equal(SeatControllerModes.AI, bindings[0].ControllerMode);
        Assert.True(bindings[0].HasActiveBotControl);
        Assert.Equal("bot-1", bindings[0].BotDefinitionId);
    }

    private static string InvokeDescribeAction(
        GameEngineService service,
        GameEntity gameEntity,
        IReadOnlyList<GamePlayerStateEntity> playerStates,
        PlayerAction action,
        Boxcars.Engine.Persistence.GameState snapshotBeforeAction,
        Boxcars.Engine.Persistence.GameState snapshotAfterAction,
        Boxcars.Engine.Domain.GameEngine engine)
    {
        var method = typeof(GameEngineService).GetMethod("DescribeAction", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("DescribeAction was not found.");

        return (string)(method.Invoke(service, [gameEntity, playerStates, action, snapshotBeforeAction, snapshotAfterAction, engine])
            ?? throw new InvalidOperationException("DescribeAction returned null."));
    }

    private static GameEntity CreateGameEntity()
    {
        return new GameEntity
        {
            PartitionKey = "game-1",
            RowKey = "GAME",
            GameId = "game-1"
        };
    }

    private static IReadOnlyList<GamePlayerStateEntity> CreatePlayerStates()
    {
        return BotTurnServiceTestHarness.CreatePlayerStates(
        [
            new GamePlayerSelection { UserId = "alice@example.com", DisplayName = "Alice", Color = "#111111" },
            new GamePlayerSelection { UserId = "tom@example.com", DisplayName = "TOM", Color = "#222222" }
        ]);
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
        return InvokeBuildTimelineItems(gameEvent, previousGameEvent, null);
    }

    private static IReadOnlyList<EventTimelineItem> InvokeBuildTimelineItems(
        GameEventEntity gameEvent,
        GameEventEntity? previousGameEvent,
        int? announcingCash)
    {
        var method = typeof(GameService).GetMethod(
            "BuildTimelineItems",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            announcingCash.HasValue
                ? [typeof(GameEventEntity), typeof(GameEventEntity), typeof(int)]
                : [typeof(GameEventEntity), typeof(GameEventEntity)],
            modifiers: null)
            ?? throw new InvalidOperationException("BuildTimelineItems was not found.");

        var arguments = announcingCash.HasValue
            ? new object?[] { gameEvent, previousGameEvent, announcingCash.Value }
            : [gameEvent, previousGameEvent];

        return (IReadOnlyList<EventTimelineItem>)(method.Invoke(null, arguments)
            ?? throw new InvalidOperationException("BuildTimelineItems returned null."));
    }

    private static (GameEventEntity currentEvent, GameEventEntity previousEvent) CreateTimelineEvent(
        string rowKey,
        global::Boxcars.Engine.Persistence.GameState previousSnapshot,
        global::Boxcars.Engine.Persistence.GameState currentSnapshot)
    {
        return (
            new GameEventEntity
            {
                PartitionKey = "game-1",
                RowKey = rowKey,
                GameId = "game-1",
                EventKind = "Move",
                ChangeSummary = "Alice moved.",
                SerializedGameState = JsonSerializer.Serialize(currentSnapshot),
                OccurredUtc = DateTimeOffset.UtcNow,
                ActingPlayerIndex = 0,
                CreatedBy = "alice@example.com"
            },
            new GameEventEntity
            {
                PartitionKey = "game-1",
                RowKey = $"{rowKey}-previous",
                GameId = "game-1",
                EventKind = "RollDice",
                SerializedGameState = JsonSerializer.Serialize(previousSnapshot),
                OccurredUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                ActingPlayerIndex = 0,
                CreatedBy = "alice@example.com"
            });
    }

    private static global::Boxcars.Engine.Persistence.GameState CreateSnapshot(int cash)
    {
        return new global::Boxcars.Engine.Persistence.GameState
        {
            ActivePlayerIndex = 0,
            Players =
            [
                new global::Boxcars.Engine.Persistence.PlayerState
                {
                    Name = "Alice",
                    Cash = cash
                },
                new global::Boxcars.Engine.Persistence.PlayerState
                {
                    Name = "Bob",
                    Cash = 50_000
                }
            ]
        };
    }
}
