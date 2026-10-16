using System.Globalization;
using Boxcars.Engine.Persistence;

namespace Boxcars.Services;

/// <summary>
/// Play-by-play wording for dice rolls. A roll shows the total of the dice rolled so far with the individual dice;
/// "+bonus" marks a bonus roll still to come (Freight on 6+6, Express on any doubles).
/// </summary>
public static class DiceRollDescriptions
{
    /// <summary>Describes a turn's roll, e.g. "Alice rolled 10 (6+4)" or "Alice rolled BOXCARS! (6+6+bonus)".</summary>
    public static string DescribeRoll(string playerName, DiceResultState? diceResult, bool bonusRollAvailable)
    {
        if (diceResult?.WhiteDice is not { Length: >= 2 } whiteDice || whiteDice.All(value => value <= 0))
        {
            return $"{playerName} rolled 0";
        }

        var whiteDiceText = string.Join("+", whiteDice.Select(value => value.ToString(CultureInfo.InvariantCulture)));
        var isBoxcars = whiteDice.Length == 2 && whiteDice.All(value => value == 6);

        // Superchief rolls the red die with the white dice.
        if (diceResult.RedDie is int redDie)
        {
            return isBoxcars && redDie == 6
                ? $"{playerName} rolled boxcars and the caboose!"
                : FormattableString.Invariant($"{playerName} rolled {whiteDice.Sum() + redDie} ({whiteDiceText}+{redDie})");
        }

        if (bonusRollAvailable)
        {
            return isBoxcars
                ? $"{playerName} rolled BOXCARS! ({whiteDiceText}+bonus)"
                : FormattableString.Invariant($"{playerName} rolled {whiteDice.Sum()} ({whiteDiceText}+bonus)");
        }

        return FormattableString.Invariant($"{playerName} rolled {whiteDice.Sum()} ({whiteDiceText})");
    }

    /// <summary>Describes a bonus roll; a 6 rolled after boxcars bonuses the caboose.</summary>
    public static string DescribeBonusRoll(string playerName, int bonusRoll, DiceResultState? precedingRoll)
    {
        // A Superchief that arrives on the white dice alone moves on with the red die it already rolled.
        if (precedingRoll?.RedDie is not null)
        {
            return FormattableString.Invariant($"{playerName} moves on with the red die bonus of {bonusRoll}");
        }

        var followedBoxcars = precedingRoll?.WhiteDice is { Length: 2 } whiteDice && whiteDice.All(value => value == 6);
        return followedBoxcars && bonusRoll == 6
            ? $"{playerName} bonused the caboose!"
            : FormattableString.Invariant($"{playerName} rolled bonus {bonusRoll}");
    }

    /// <summary>
    /// True when <paramref name="snapshot"/> holds a bonus roll made since <paramref name="previousSnapshot"/>: a bonus
    /// was pending and the white dice were cleared in favor of the red bonus die.
    /// </summary>
    public static bool TryGetBonusRoll(GameState? previousSnapshot, GameState snapshot, out int bonusRoll)
    {
        bonusRoll = 0;
        var diceResult = snapshot.Turn.DiceResult;
        if (previousSnapshot is null
            || !previousSnapshot.Turn.BonusRollAvailable
            || diceResult?.WhiteDice is not { Length: >= 2 } whiteDice
            || whiteDice.Any(value => value != 0)
            || diceResult.RedDie is not int redDie
            || previousSnapshot.Turn.DiceResult?.WhiteDice is not { } previousWhiteDice
            || previousWhiteDice.All(value => value == 0))
        {
            return false;
        }

        bonusRoll = redDie;
        return true;
    }
}
