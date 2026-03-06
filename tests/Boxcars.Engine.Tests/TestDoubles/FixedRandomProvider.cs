namespace Boxcars.Engine.Tests.TestDoubles;

/// <summary>
/// Deterministic random provider for tests. Queues predetermined outcomes.
/// </summary>
public sealed class FixedRandomProvider : IRandomProvider
{
    private readonly Queue<int[]> _diceRolls = new();
    private readonly Queue<int> _weightedDraws = new();

    /// <summary>Queue a specific set of dice values for the next RollDiceIndividual call.</summary>
    public void QueueDiceRoll(params int[] values) => _diceRolls.Enqueue(values);

    /// <summary>Queue a specific index for the next WeightedDraw call.</summary>
    public void QueueWeightedDraw(int index) => _weightedDraws.Enqueue(index);

    /// <summary>Queue multiple weighted draws at once.</summary>
    public void QueueWeightedDraws(params int[] indices)
    {
        foreach (var idx in indices)
            _weightedDraws.Enqueue(idx);
    }

    public int[] RollDiceIndividual(int count, int sides = 6)
    {
        if (_diceRolls.Count > 0)
        {
            var queued = _diceRolls.Dequeue();
            if (queued.Length == count)
                return queued;
            // If count doesn't match, take first N or pad with 1s
            var result = new int[count];
            for (int i = 0; i < count; i++)
                result[i] = i < queued.Length ? queued[i] : 1;
            return result;
        }

        // Default: return all 1s
        var defaults = new int[count];
        Array.Fill(defaults, 1);
        return defaults;
    }

    public int WeightedDraw(IReadOnlyList<double> probabilities)
    {
        if (_weightedDraws.Count > 0)
        {
            var idx = _weightedDraws.Dequeue();
            return Math.Min(idx, probabilities.Count - 1);
        }

        return 0; // Default: first item
    }

    /// <summary>Whether there are remaining queued dice rolls.</summary>
    public bool HasPendingDiceRolls => _diceRolls.Count > 0;

    /// <summary>Whether there are remaining queued weighted draws.</summary>
    public bool HasPendingWeightedDraws => _weightedDraws.Count > 0;
}
