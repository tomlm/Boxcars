namespace Boxcars.Data;

/// <summary>How easily the buyer can pay for something, shown green / yellow / red (or plain text when they can't).</summary>
public enum PurchaseAffordabilityLevel
{
    /// <summary>After the price and this turn's fees, the buyer still has their operating reserve.</summary>
    Comfortable,

    /// <summary>The buyer can pay the price and fees, but is left with less than their reserve.</summary>
    Tight,

    /// <summary>The buyer has the cash for the price, but paying fees afterwards means selling something.</summary>
    NeedsSale,

    /// <summary>The buyer can't pay the price at all.</summary>
    Unaffordable
}

/// <summary>What the buyer has to spend: cash on hand, fees still to pay this turn, and the reserve to keep.</summary>
public sealed record PurchaseBudget(int Cash, int FeesOwed, int OperatingReserve)
{
    public PurchaseAffordability Assess(int price)
    {
        var remaining = Cash - price - FeesOwed;
        var level = price > Cash
            ? PurchaseAffordabilityLevel.Unaffordable
            : remaining < 0
                ? PurchaseAffordabilityLevel.NeedsSale
                : remaining < OperatingReserve
                    ? PurchaseAffordabilityLevel.Tight
                    : PurchaseAffordabilityLevel.Comfortable;
        return new PurchaseAffordability(price, remaining, level);
    }
}

/// <param name="Remaining">Cash left after paying the price and this turn's fees; negative when something must be sold.</param>
public sealed record PurchaseAffordability(int Price, int Remaining, PurchaseAffordabilityLevel Level);
