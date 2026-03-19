namespace Boxcars.Data;

public enum PurchaseExperienceTab
{
    Map,
    Regions,
    Cities,
    Railroads,
    Network,
    History
}

public sealed class PurchasePhaseModel
{
    public int PlayerIndex { get; init; } = -1;

    public string PlayerName { get; init; } = string.Empty;

    public int CashAvailable { get; init; }

    public string DestinationCityName { get; init; } = string.Empty;

    public int PayoutAmount { get; init; }

    public int CashAfterPayout { get; init; }

    public IReadOnlyList<RailroadPurchaseOption> RailroadOptions { get; init; } = [];

    public IReadOnlyList<EngineUpgradeOption> EngineOptions { get; init; } = [];

    public IReadOnlyList<PurchaseOptionModel> TaskbarOptions { get; init; } = [];

    public PurchaseTaskbarState TaskbarState { get; init; } = new();

    public bool CanDecline { get; init; }

    public NetworkCoverageSnapshot? CurrentCoverage { get; init; }

    public NetworkCoverageSnapshot? ProjectedCoverage { get; init; }

    public PurchaseExperienceTab SelectedTab { get; init; } = PurchaseExperienceTab.Map;

    public MapAnalysisReport? MapAnalysisReport { get; init; }

    public string? SelectedOptionKey { get; init; }

    public RailroadOverlayInfo? SelectedRailroadOverlay { get; init; }

    public bool HasActivePurchaseControls { get; init; }

    public string? NoPurchaseNotification { get; init; }

    public RecommendationInputSet? RecommendationInputs { get; init; }
}

public sealed class PurchaseTaskbarState
{
    public string Label { get; init; } = "Purchase Options";

    public IReadOnlyList<PurchaseOptionModel> Options { get; init; } = [];

    public string? SelectedOptionKey { get; init; }

    public bool CanBuy { get; init; }

    public bool CanDecline { get; init; }
}

public sealed class PurchaseDecision
{
    public PurchaseOptionKind OptionKind { get; init; }

    public string OptionKey { get; init; } = string.Empty;

    public int AmountPaid { get; init; }

    public bool CanConfirm { get; init; }
}