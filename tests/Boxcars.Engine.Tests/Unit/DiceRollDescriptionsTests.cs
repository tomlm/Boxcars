using System.Reflection;
using System.Text.Json;
using global::Boxcars.Data;
using global::Boxcars.Engine.Persistence;
using global::Boxcars.Services;

namespace Boxcars.Engine.Tests.Unit;

public class DiceRollDescriptionsTests
{
    [Theory]
    // Freight, no bonus.
    [InlineData(new[] { 6, 4 }, null, false, "Alice rolled 10 (6+4)")]
    // Express doubles: a bonus roll is still to come.
    [InlineData(new[] { 4, 4 }, null, true, "Alice rolled 8 (4+4+bonus)")]
    // Freight or Express 6+6.
    [InlineData(new[] { 6, 6 }, null, true, "Alice rolled BOXCARS! (6+6+bonus)")]
    // Superchief rolls the red die with the white dice.
    [InlineData(new[] { 4, 4 }, 3, false, "Alice rolled 11 (4+4+3)")]
    [InlineData(new[] { 6, 6 }, 6, false, "Alice rolled boxcars and the caboose!")]
    public void DescribeRoll_MatchesPlayByPlayWording(int[] whiteDice, int? redDie, bool bonusRollAvailable, string expected)
    {
        var description = DiceRollDescriptions.DescribeRoll("Alice", new DiceResultState { WhiteDice = whiteDice, RedDie = redDie }, bonusRollAvailable);

        Assert.Equal(expected, description);
    }

    [Theory]
    [InlineData(new[] { 4, 4 }, 3, "Alice rolled bonus 3")]
    [InlineData(new[] { 6, 6 }, 3, "Alice rolled bonus 3")]
    [InlineData(new[] { 6, 6 }, 6, "Alice bonused the caboose!")]
    // A 6 bonus only bonuses the caboose after boxcars.
    [InlineData(new[] { 4, 4 }, 6, "Alice rolled bonus 6")]
    public void DescribeBonusRoll_MatchesPlayByPlayWording(int[] precedingWhiteDice, int bonusRoll, string expected)
    {
        var description = DiceRollDescriptions.DescribeBonusRoll("Alice", bonusRoll, new DiceResultState { WhiteDice = precedingWhiteDice });

        Assert.Equal(expected, description);
    }

    [Fact]
    public void BuildTimelineItems_SuperchiefMovingOnWithItsRedDie_AddsNoBonusRollEntry()
    {
        // A Superchief that arrived on the white dice continues with the red die from its roll; the roll already showed it.
        var beforeMove = CreateSnapshot(new DiceResultState { WhiteDice = [4, 2], RedDie = 3 }, bonusRollAvailable: true);
        var afterMove = CreateSnapshot(new DiceResultState { WhiteDice = [0, 0], RedDie = 3 }, bonusRollAvailable: false);

        var items = BuildTimelineItems(beforeMove, afterMove);

        Assert.DoesNotContain(items, item => item.EventId.EndsWith(":bonus-roll", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildTimelineItems_MoveThatRollsTheBonus_AddsABonusRollEntry()
    {
        var beforeMove = CreateSnapshot(new DiceResultState { WhiteDice = [6, 6] }, bonusRollAvailable: true);
        var afterMove = CreateSnapshot(new DiceResultState { WhiteDice = [0, 0], RedDie = 6 }, bonusRollAvailable: false);

        var items = BuildTimelineItems(beforeMove, afterMove);

        var bonusItem = Assert.Single(items, item => item.EventId.EndsWith(":bonus-roll", StringComparison.Ordinal));
        Assert.Equal("Alice bonused the caboose!", bonusItem.Description);
        Assert.Equal(EventTimelineKind.DiceRoll, bonusItem.EventKind);
        Assert.True(bonusItem.IsPlayByPlay);
    }

    [Fact]
    public void BuildTimelineItems_MoveWithoutAPendingBonus_AddsNoBonusRollEntry()
    {
        var beforeMove = CreateSnapshot(new DiceResultState { WhiteDice = [6, 4] }, bonusRollAvailable: false);
        var afterMove = CreateSnapshot(new DiceResultState { WhiteDice = [6, 4] }, bonusRollAvailable: false);

        var items = BuildTimelineItems(beforeMove, afterMove);

        Assert.DoesNotContain(items, item => item.EventId.EndsWith(":bonus-roll", StringComparison.Ordinal));
    }

    private static GameState CreateSnapshot(DiceResultState diceResult, bool bonusRollAvailable)
    {
        return new GameState
        {
            ActivePlayerIndex = 0,
            Players =
            [
                new PlayerState { Name = "Alice", Cash = 50_000 },
                new PlayerState { Name = "Bob", Cash = 50_000 }
            ],
            Turn = new TurnState { DiceResult = diceResult, BonusRollAvailable = bonusRollAvailable }
        };
    }

    private static IReadOnlyList<EventTimelineItem> BuildTimelineItems(GameState previousSnapshot, GameState snapshot)
    {
        var previousEvent = CreateEvent("Event_0000000001", "RollDice", previousSnapshot);
        var currentEvent = CreateEvent("Event_0000000002", "Move", snapshot);
        var method = typeof(GameService).GetMethod(
            "BuildTimelineItems",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            [typeof(GameEventEntity), typeof(GameEventEntity)],
            modifiers: null)
            ?? throw new InvalidOperationException("BuildTimelineItems was not found.");

        return (IReadOnlyList<EventTimelineItem>)method.Invoke(null, [currentEvent, previousEvent])!;
    }

    private static GameEventEntity CreateEvent(string rowKey, string eventKind, GameState snapshot)
    {
        return new GameEventEntity
        {
            PartitionKey = "game-1",
            RowKey = rowKey,
            GameId = "game-1",
            EventKind = eventKind,
            ChangeSummary = "Alice moved.",
            SerializedGameState = JsonSerializer.Serialize(snapshot),
            OccurredUtc = DateTimeOffset.UtcNow,
            ActingPlayerIndex = 0,
            CreatedBy = "alice@example.com"
        };
    }
}
