using System.Reflection;
using System.Text.Json;
using Boxcars.Data;
using Boxcars.Engine.Domain;
using Boxcars.GameEngine;
using Boxcars.Engine.Tests.Fixtures;
using Boxcars.Services;
using Boxcars.Services.Maps;
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
                DecisionSource = "Fallback",
                FallbackReason = "Timeout"
            }
        };

        var payload = GameEventSerialization.SerializeEventData(action);

        Assert.Contains("\"BotMetadata\"", payload, StringComparison.Ordinal);
        Assert.Contains("\"BotDefinitionId\":\"bot-1\"", payload, StringComparison.Ordinal);
        Assert.Contains("\"BotName\":\"El Cheapo\"", payload, StringComparison.Ordinal);
        Assert.Contains("\"DecisionSource\":\"Fallback\"", payload, StringComparison.Ordinal);
        Assert.Contains("\"FallbackReason\":\"Timeout\"", payload, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeAction_BotDrivenRegionChoice_AppendsBotAttributionSuffix()
    {
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
                DecisionSource = "Fallback",
                FallbackReason = "Timeout"
            }
        };

        random.QueueWeightedDraw(1);
        engine.ChooseDestinationRegion("SE");
        var snapshotAfterAction = engine.ToSnapshot();

        var summary = InvokeDescribeAction(action, snapshotBeforeAction, snapshotAfterAction, engine);

        Assert.Equal("Alice chose SE as the replacement destination region and received Atlanta. [El Cheapo; Timeout]", summary);
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

    private static string InvokeDescribeAction(
        PlayerAction action,
        Boxcars.Engine.Persistence.GameState snapshotBeforeAction,
        Boxcars.Engine.Persistence.GameState snapshotAfterAction,
        Boxcars.Engine.Domain.GameEngine engine)
    {
        var method = typeof(GameEngineService).GetMethod("DescribeAction", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("DescribeAction was not found.");

        return (string)(method.Invoke(null, [action, snapshotBeforeAction, snapshotAfterAction, engine])
            ?? throw new InvalidOperationException("DescribeAction returned null."));
    }

    private static IReadOnlyList<EventTimelineItem> InvokeBuildTimelineItems(GameEventEntity gameEvent, GameEventEntity? previousGameEvent)
    {
        var method = typeof(GameService).GetMethod("BuildTimelineItems", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("BuildTimelineItems was not found.");

        return (IReadOnlyList<EventTimelineItem>)(method.Invoke(null, [gameEvent, previousGameEvent])
            ?? throw new InvalidOperationException("BuildTimelineItems returned null."));
    }
}