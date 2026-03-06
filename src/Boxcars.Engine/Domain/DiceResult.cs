namespace Boxcars.Engine.Domain;

/// <summary>
/// Immutable result of a dice roll.
/// </summary>
public sealed class DiceResult
{
    /// <summary>Individual white die values (2 dice).</summary>
    public int[] WhiteDice { get; }

    /// <summary>Red bonus die value (null if not rolled).</summary>
    public int? RedDie { get; }

    /// <summary>Sum of all dice.</summary>
    public int Total { get; }

    /// <summary>Whether the white dice show the same value.</summary>
    public bool IsDoubles { get; }

    public DiceResult(int[] whiteDice, int? redDie = null)
    {
        WhiteDice = whiteDice;
        RedDie = redDie;
        Total = whiteDice.Sum() + (redDie ?? 0);
        IsDoubles = whiteDice.Length == 2 && whiteDice[0] == whiteDice[1];
    }
}
