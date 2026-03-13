using Boxcars.Engine.Domain;

namespace Boxcars.Data;

public enum PurchaseOptionKind
{
    Railroad,
    EngineUpgrade
}

public sealed class PurchaseOptionModel
{
    public string OptionKey { get; init; } = string.Empty;

    public PurchaseOptionKind OptionKind { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public int PurchasePrice { get; init; }

    public int SortPriceDescendingKey { get; init; }

    public bool IsAffordable { get; init; } = true;

    public bool IsSelected { get; init; }
}

public sealed class RailroadPurchaseOption
{
    public int RailroadIndex { get; init; } = -1;

    public string RailroadName { get; init; } = string.Empty;

    public string ShortName { get; init; } = string.Empty;

    public int PurchasePrice { get; init; }

    public bool IsAffordable { get; init; }

    public bool IsSelected { get; init; }
}

public sealed class EngineUpgradeOption
{
    public LocomotiveType EngineType { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public int PurchasePrice { get; init; }

    public LocomotiveType CurrentEngineType { get; init; }

    public bool IsEligible { get; init; }

    public bool IsSelected { get; init; }
}

public sealed class RailroadOverlayInfo
{
    public int RailroadIndex { get; init; } = -1;

    public string RailroadName { get; init; } = string.Empty;

    public string ContextLabel { get; init; } = string.Empty;

    public string ValueLabel { get; init; } = "Price";

    public int PurchasePrice { get; init; }

    public bool IsAffordable { get; init; } = true;

    public RailroadOverlayValueKind ValueKind { get; init; } = RailroadOverlayValueKind.Affordable;

    public IReadOnlyList<RailroadOverlayMetricRow> MetricRows { get; init; } = [];
}

public enum RailroadOverlayValueKind
{
    Affordable,
    TooExpensive,
    Sale
}

public sealed class RailroadOverlayMetricRow
{
    public string Label { get; init; } = string.Empty;

    public decimal AccessPercent { get; init; }

    public decimal ProjectedAccessPercent { get; init; }

    public decimal MonopolyPercent { get; init; }

    public decimal ProjectedMonopolyPercent { get; init; }

    public decimal AccessDeltaPercent { get; init; }

    public decimal MonopolyDeltaPercent { get; init; }
}