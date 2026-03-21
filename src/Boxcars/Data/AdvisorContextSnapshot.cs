namespace Boxcars.Data;

public sealed class AdvisorContextSnapshot
{
    public string GameId { get; init; } = string.Empty;
    public int TurnNumber { get; init; }
    public string TurnPhase { get; init; } = string.Empty;
    public int ActivePlayerIndex { get; init; } = -1;
    public int? ControlledPlayerIndex { get; init; }
    public string ControlledPlayerName { get; init; } = string.Empty;
    public string ControlledPlayerSummary { get; init; } = string.Empty;
    public IReadOnlyList<string> OtherPlayerSummaries { get; init; } = [];
    public string BoardSituationSummary { get; init; } = string.Empty;
    public string SeedContextContent { get; init; } = string.Empty;
    public string AuthoritativePayloadJson { get; init; } = string.Empty;
    public IReadOnlyList<AdvisorMessage> RecentConversation { get; init; } = [];

    // Rich structured context for AI consumption
    public AdvisorMapContext? MapContext { get; init; }
    public AdvisorPlayerContext? ControlledPlayerContext { get; init; }
    public IReadOnlyList<AdvisorOpponentContext> OpponentContexts { get; init; } = [];
    public IReadOnlyList<AdvisorPurchasableRailroad> AvailableRailroads { get; init; } = [];
}

public sealed class AdvisorMapContext
{
    public string MapName { get; init; } = string.Empty;
    public int RegionCount { get; init; }
    public int CityCount { get; init; }
    public int RailroadCount { get; init; }
    public IReadOnlyList<AdvisorRegionInfo> Regions { get; init; } = [];
    public IReadOnlyList<AdvisorRailroadInfo> Railroads { get; init; } = [];
}

public sealed class AdvisorRegionInfo
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public double Probability { get; init; }
    public IReadOnlyList<string> Cities { get; init; } = [];
}

public sealed class AdvisorRailroadInfo
{
    public int Index { get; init; }
    public string Name { get; init; } = string.Empty;
    public int PurchasePrice { get; init; }
    public int CityCount { get; init; }
    public IReadOnlyList<string> ConnectedCities { get; init; } = [];
}

public sealed class AdvisorPlayerContext
{
    public string Name { get; init; } = string.Empty;
    public int Cash { get; init; }
    public string Engine { get; init; } = string.Empty;
    public string CurrentCity { get; init; } = string.Empty;
    public string HomeCity { get; init; } = string.Empty;
    public string? TripOrigin { get; init; }
    public string? TripDestination { get; init; }
    public int TripPayout { get; init; }
    public int RouteProgressIndex { get; init; }
    public int PendingFees { get; init; }
    public bool HasDeclared { get; init; }
    public IReadOnlyList<string> OwnedRailroads { get; init; } = [];
    public decimal NetworkAccessPercent { get; init; }
    public decimal MonopolyPercent { get; init; }
    public IReadOnlyList<AdvisorRegionCoverage> RegionCoverage { get; init; } = [];
}

public sealed class AdvisorOpponentContext
{
    public int PlayerIndex { get; init; }
    public string Name { get; init; } = string.Empty;
    public int Cash { get; init; }
    public string Engine { get; init; } = string.Empty;
    public string CurrentCity { get; init; } = string.Empty;
    public string HomeCity { get; init; } = string.Empty;
    public string? TripOrigin { get; init; }
    public string? TripDestination { get; init; }
    public int TripPayout { get; init; }
    public int RouteProgressIndex { get; init; }
    public bool HasDeclared { get; init; }
    public bool IsActive { get; init; }
    public IReadOnlyList<string> OwnedRailroads { get; init; } = [];
    public decimal NetworkAccessPercent { get; init; }
}

public sealed class AdvisorRegionCoverage
{
    public string RegionCode { get; init; } = string.Empty;
    public decimal AccessPercent { get; init; }
    public decimal MonopolyPercent { get; init; }
}

public sealed class AdvisorPurchasableRailroad
{
    public int Index { get; init; }
    public string Name { get; init; } = string.Empty;
    public int Price { get; init; }
    public int CityCount { get; init; }
    public decimal ProjectedAccessPercent { get; init; }
    public decimal ProjectedMonopolyPercent { get; init; }
    public decimal AccessGain { get; init; }
    public decimal MonopolyGain { get; init; }
}