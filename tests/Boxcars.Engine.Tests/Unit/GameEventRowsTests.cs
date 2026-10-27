using global::Boxcars.GameEngine;

namespace Boxcars.Engine.Tests.Unit;

public class GameEventRowsTests
{
    private static readonly DateTime Earlier = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = Earlier.AddMilliseconds(1);

    [Fact]
    public void CreateRowKey_NewerEventSortsFirst()
    {
        Assert.True(string.CompareOrdinal(GameEventRows.CreateRowKey(Later, 1), GameEventRows.CreateRowKey(Earlier, 2)) < 0);
        Assert.True(string.CompareOrdinal(GameEventRows.CreateRowKey(Later), GameEventRows.CreateRowKey(Earlier)) < 0);
    }

    [Fact]
    public void CreateRowKey_SameTick_HigherSequenceSortsFirst()
    {
        Assert.True(string.CompareOrdinal(GameEventRows.CreateRowKey(Earlier, 8), GameEventRows.CreateRowKey(Earlier, 7)) < 0);
    }

    [Fact]
    public void CreateRowKey_StaysInsideTheEventRangeAndOutsideTheLegacyRange()
    {
        var rowKey = GameEventRows.CreateRowKey(Earlier, 1);

        Assert.True(string.CompareOrdinal(rowKey, GameEventRows.RowKeyPrefix) >= 0);
        Assert.True(string.CompareOrdinal(rowKey, GameEventRows.RowKeyExclusiveUpperBound) < 0);
        Assert.True(string.CompareOrdinal(rowKey, GameEventRows.LegacyRowKeyExclusiveUpperBound) >= 0);
    }

    [Fact]
    public void TryConvertLegacyRowKey_SequenceKey_MatchesTheNewKeyForTheSameEvent()
    {
        var legacy = $"Event_{Earlier.Ticks:D20}_{42:D8}";

        Assert.True(string.CompareOrdinal(legacy, GameEventRows.LegacyRowKeyExclusiveUpperBound) < 0);
        Assert.True(GameEventRowKeyMigration.TryConvertLegacyRowKey(legacy, out var rowKey));
        Assert.Equal(GameEventRows.CreateRowKey(Earlier, 42), rowKey);
    }

    [Fact]
    public void TryConvertLegacyRowKey_GuidKey_KeepsTheGuidAndReversesTheTime()
    {
        var guid = Guid.NewGuid().ToString("N");
        var legacy = $"Event_{Earlier.Ticks:D20}_{guid}";

        Assert.True(GameEventRowKeyMigration.TryConvertLegacyRowKey(legacy, out var rowKey));
        Assert.Equal($"Event_{DateTime.MaxValue.Ticks - Earlier.Ticks:D20}_{guid}", rowKey);
    }

    [Fact]
    public void TryConvertLegacyRowKey_ConvertedKeysAreNewestFirst()
    {
        Assert.True(GameEventRowKeyMigration.TryConvertLegacyRowKey($"Event_{Earlier.Ticks:D20}_{1:D8}", out var earlier));
        Assert.True(GameEventRowKeyMigration.TryConvertLegacyRowKey($"Event_{Later.Ticks:D20}_{2:D8}", out var later));

        Assert.True(string.CompareOrdinal(later, earlier) < 0);
    }

    [Theory]
    [InlineData("Event_0000000001")]
    [InlineData("Event_abcdefghijklmnopqrst_00000001")]
    public void TryConvertLegacyRowKey_RejectsMalformedKeys(string legacy)
    {
        Assert.False(GameEventRowKeyMigration.TryConvertLegacyRowKey(legacy, out _));
    }
}
