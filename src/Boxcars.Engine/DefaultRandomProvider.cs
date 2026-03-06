namespace Boxcars.Engine;

/// <summary>
/// Default random provider using System.Random for production use.
/// </summary>
public sealed class DefaultRandomProvider : IRandomProvider
{
    private readonly Random _random;

    public DefaultRandomProvider()
    {
        _random = new Random();
    }

    public DefaultRandomProvider(int seed)
    {
        _random = new Random(seed);
    }

    public int[] RollDiceIndividual(int count, int sides = 6)
    {
        var results = new int[count];
        for (int i = 0; i < count; i++)
        {
            results[i] = _random.Next(1, sides + 1);
        }
        return results;
    }

    public int WeightedDraw(IReadOnlyList<double> probabilities)
    {
        if (probabilities.Count == 0)
            throw new ArgumentException("Probabilities list cannot be empty.", nameof(probabilities));

        double total = 0;
        foreach (var p in probabilities)
            total += p;

        double roll = _random.NextDouble() * total;
        double cumulative = 0;

        for (int i = 0; i < probabilities.Count; i++)
        {
            cumulative += probabilities[i];
            if (roll < cumulative)
                return i;
        }

        return probabilities.Count - 1;
    }
}
