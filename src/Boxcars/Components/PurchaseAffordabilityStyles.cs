using Boxcars.Data;
using MudBlazor;

namespace Boxcars.Components;

/// <summary>
/// The green / yellow / red used wherever the app shows whether the player can afford something; what they can't afford
/// at all is in the normal text color.
/// </summary>
public static class PurchaseAffordabilityStyles
{
    /// <summary>Text color.</summary>
    public static Color TextColor(PurchaseAffordabilityLevel level)
    {
        return level switch
        {
            PurchaseAffordabilityLevel.Comfortable => Color.Success,
            PurchaseAffordabilityLevel.Tight => Color.Warning,
            PurchaseAffordabilityLevel.NeedsSale => Color.Error,
            _ => Color.Default
        };
    }

    /// <summary>The same color as a CSS value, for plain elements.</summary>
    public static string CssColor(PurchaseAffordabilityLevel level)
    {
        return level switch
        {
            PurchaseAffordabilityLevel.Comfortable => "var(--mud-palette-success)",
            PurchaseAffordabilityLevel.Tight => "var(--mud-palette-warning)",
            PurchaseAffordabilityLevel.NeedsSale => "var(--mud-palette-error)",
            _ => "var(--mud-palette-text-primary)"
        };
    }
}
