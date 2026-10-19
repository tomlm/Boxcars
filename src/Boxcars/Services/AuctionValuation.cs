using System.Globalization;

namespace Boxcars.Services;

/// <summary>
/// The most a bot should bid for a railroad at auction. Two things set what it's worth:
/// <list type="bullet">
/// <item>How much the railroad helps the bidder: half its list price for a railroad that adds nothing, up to the
/// full list price for one that adds a lot of access or monopoly.</item>
/// <item>How few railroads are left to buy. While plenty are unsold, money spent here is money not spent on one of
/// them, so the bid stays near list. Once they're gone, this is the only way to get more track: the premium climbs to
/// <see cref="SoldOutPremium"/> times, so a sold-out railroad goes for two to four times its list price.</item>
/// </list>
/// What the bidder will pay is capped by the cash it can spare: the operating reserve, and anything it's saving for
/// (<see cref="AuctionCashCommitment"/>), stay out of the bid.
/// </summary>
public static class AuctionValuation
{
    /// <summary>The multiplier on the value once every other railroad is sold.</summary>
    public const decimal SoldOutPremium = 4m;

    /// <summary>Access plus half the monopoly gain, in percentage points, at which a railroad counts as fully useful.</summary>
    private const decimal FullyUsefulGainPercent = 8m;

    /// <summary>0 to 1: how much a railroad's access and monopoly gains help the bidder.</summary>
    public static decimal Usefulness(decimal accessGainPercent, decimal monopolyGainPercent)
    {
        return Math.Clamp((Math.Max(0m, accessGainPercent) + (Math.Max(0m, monopolyGainPercent) / 2m)) / FullyUsefulGainPercent, 0m, 1m);
    }

    public static AuctionBidValuation Evaluate(
        int listPrice,
        decimal accessGainPercent,
        decimal monopolyGainPercent,
        int otherUnsoldRailroadCount,
        int privateRailroadCount,
        int cash,
        int operatingReserve,
        IReadOnlyList<AuctionCashCommitment>? commitments = null)
    {
        commitments ??= [];
        var usefulness = Usefulness(accessGainPercent, monopolyGainPercent);
        var soldShare = privateRailroadCount <= 1
            ? 1m
            : Math.Clamp(1m - ((decimal)otherUnsoldRailroadCount / (privateRailroadCount - 1)), 0m, 1m);

        // Squared, so the premium stays small until most railroads are gone.
        var scarcityPremium = 1m + ((SoldOutPremium - 1m) * soldShare * soldShare);
        var value = listPrice * (0.5m + (usefulness / 2m)) * scarcityPremium;
        var affordable = Math.Max(0, cash - operatingReserve - commitments.Sum(commitment => commitment.Amount));
        var maximumBid = RoundDownToIncrement((int)Math.Min(value, affordable));

        return new AuctionBidValuation(
            maximumBid,
            RoundDownToIncrement((int)value),
            affordable,
            Math.Round(usefulness, 2),
            Math.Round(scarcityPremium, 2),
            otherUnsoldRailroadCount,
            commitments,
            Describe(listPrice, usefulness, scarcityPremium, otherUnsoldRailroadCount, (int)value, affordable, commitments));
    }

    private static int RoundDownToIncrement(int amount)
    {
        var increment = global::Boxcars.Engine.Domain.GameEngine.AuctionBidIncrement;
        return Math.Max(0, amount / increment * increment);
    }

    private static string Describe(
        int listPrice,
        decimal usefulness,
        decimal scarcityPremium,
        int otherUnsoldRailroadCount,
        int value,
        int affordable,
        IReadOnlyList<AuctionCashCommitment> commitments)
    {
        var culture = CultureInfo.InvariantCulture;
        var left = otherUnsoldRailroadCount == 0
            ? "no other railroads are left to buy"
            : $"{otherUnsoldRailroadCount} other railroad{(otherUnsoldRailroadCount == 1 ? " is" : "s are")} still for sale";
        var text = $"Worth ${value.ToString("N0", culture)}: list ${listPrice.ToString("N0", culture)}, usefulness {(usefulness * 100m).ToString("0", culture)}%, scarcity x{scarcityPremium.ToString("0.##", culture)} because {left}.";
        if (affordable >= value)
        {
            return text;
        }

        var keeping = commitments.Count == 0
            ? "the operating reserve"
            : $"the operating reserve and {string.Join(", ", commitments.Select(commitment => $"${commitment.Amount.ToString("N0", culture)} {commitment.Reason}"))}";
        return $"{text} Capped at ${affordable.ToString("N0", culture)} to keep {keeping}.";
    }
}

/// <summary>Cash the bidder is saving for something else, kept out of the bid.</summary>
/// <param name="Reason">What it's for, e.g. "for a Superchief".</param>
/// <param name="Amount">How much to keep.</param>
public sealed record AuctionCashCommitment(string Reason, int Amount);

/// <param name="MaximumBid">The ceiling to bid to: the value, capped by what the bidder can spare.</param>
/// <param name="Value">What the railroad is worth to the bidder, before the cash cap.</param>
/// <param name="AffordableCash">Cash the bidder can spend and still keep the operating reserve and its commitments.</param>
/// <param name="Usefulness">0 to 1: how much access and monopoly the railroad adds.</param>
/// <param name="ScarcityPremium">1 while plenty of railroads are unsold, up to <see cref="AuctionValuation.SoldOutPremium"/> when none are.</param>
/// <param name="OtherUnsoldRailroadCount">Railroads other than this one still for sale.</param>
/// <param name="Commitments">What else the bidder is saving for.</param>
/// <param name="Reason">One or two sentences explaining the value and any cap.</param>
public sealed record AuctionBidValuation(
    int MaximumBid,
    int Value,
    int AffordableCash,
    decimal Usefulness,
    decimal ScarcityPremium,
    int OtherUnsoldRailroadCount,
    IReadOnlyList<AuctionCashCommitment> Commitments,
    string Reason);
