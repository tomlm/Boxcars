namespace Boxcars.Services;

public sealed class PurchaseRulesOptions
{
    public const string SectionName = "PurchaseRules";

    public int SuperchiefPrice { get; init; } = 40_000;
}