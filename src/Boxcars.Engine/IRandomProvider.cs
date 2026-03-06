namespace Boxcars.Engine;

/// <summary>
/// Abstraction for all randomness in the game engine, enabling deterministic testing.
/// </summary>
public interface IRandomProvider
{
    /// <summary>
    /// Rolls <paramref name="count"/> dice with <paramref name="sides"/> sides each
    /// and returns individual die values.
    /// </summary>
    int[] RollDiceIndividual(int count, int sides = 6);

    /// <summary>
    /// Performs a weighted draw given a list of cumulative probabilities.
    /// Returns the index of the selected item.
    /// </summary>
    int WeightedDraw(IReadOnlyList<double> probabilities);
}
