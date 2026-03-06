using Boxcars.Engine;

namespace Boxcars.Engine.Tests.Unit;

/// <summary>
/// Tests for PayoutTable correctness (T052).
/// </summary>
public class PayoutTests
{
    [Theory]
    [InlineData(0, 0, 0)]     // Same city = 0 payout
    [InlineData(0, 1, 5500)]  // NYC to Bos
    [InlineData(1, 0, 5500)]  // Symmetry: Bos to NYC
    [InlineData(0, 27, 7500)] // Cross-country payout
    public void GetPayout_ReturnsExpectedAmount(int from, int to, int expected)
    {
        var payout = PayoutTable.GetPayout(from, to);
        Assert.Equal(expected, payout);
    }

    [Fact]
    public void GetPayout_SameCity_ReturnsZero()
    {
        for (int i = 0; i < 28; i++)
        {
            Assert.Equal(0, PayoutTable.GetPayout(i, i));
        }
    }

    [Fact]
    public void GetPayout_IsSymmetric()
    {
        for (int i = 0; i < 28; i++)
        {
            for (int j = 0; j < 28; j++)
            {
                Assert.Equal(PayoutTable.GetPayout(i, j), PayoutTable.GetPayout(j, i));
            }
        }
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(28, 0)]
    [InlineData(0, 28)]
    public void GetPayout_OutOfBounds_ThrowsArgumentOutOfRange(int from, int to)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PayoutTable.GetPayout(from, to));
    }

    [Fact]
    public void GetPayout_AllValues_AreNonNegative()
    {
        for (int i = 0; i < 28; i++)
        {
            for (int j = 0; j < 28; j++)
            {
                Assert.True(PayoutTable.GetPayout(i, j) >= 0,
                    $"Payout({i},{j}) should be non-negative");
            }
        }
    }

    [Fact]
    public void GetPayout_AllNonDiagonal_ArePositive()
    {
        for (int i = 0; i < 28; i++)
        {
            for (int j = 0; j < 28; j++)
            {
                if (i != j)
                {
                    Assert.True(PayoutTable.GetPayout(i, j) > 0,
                        $"Payout({i},{j}) should be positive for different cities");
                }
            }
        }
    }
}
