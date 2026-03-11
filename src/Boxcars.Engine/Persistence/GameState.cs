using System.Text.Json.Serialization;

namespace Boxcars.Engine.Persistence;

/// <summary>
/// Serialization snapshot DTO capturing all mutable game state.
/// No interfaces, no base classes, no events — pure data.
/// </summary>
public sealed class GameState
{
    public string GameStatus { get; set; } = "NotStarted";
    public int TurnNumber { get; set; }
    public int ActivePlayerIndex { get; set; }
    public bool AllRailroadsSold { get; set; }
    public int? WinnerIndex { get; set; }
    public List<PlayerState> Players { get; set; } = new();
    public Dictionary<int, int?> RailroadOwnership { get; set; } = new();
    public TurnState Turn { get; set; } = new();
}

public sealed class PlayerState
{
    public string Name { get; set; } = string.Empty;
    public int Cash { get; set; }
    public string HomeCityName { get; set; } = string.Empty;
    public string CurrentCityName { get; set; } = string.Empty;
    public string? TripStartCityName { get; set; }
    public string? DestinationCityName { get; set; }
    public string LocomotiveType { get; set; } = "Freight";
    public bool IsActive { get; set; }
    public bool IsBankrupt { get; set; }
    public bool HasDeclared { get; set; }
    public List<int> OwnedRailroadIndices { get; set; } = new();
    public RouteState? ActiveRoute { get; set; }
    public List<string> SelectedRouteNodeIds { get; set; } = new();
    public List<string> SelectedRouteSegmentKeys { get; set; } = new();
    public List<string> UsedSegments { get; set; } = new();
    public List<int> GrandfatheredRailroadIndices { get; set; } = new();
    public string? CurrentNodeId { get; set; }
    public int RouteProgressIndex { get; set; }
}

public sealed class RouteState
{
    public List<string> NodeIds { get; set; } = new();
    public List<RouteSegmentState> Segments { get; set; } = new();
    public int TotalCost { get; set; }
}

public sealed class RouteSegmentState
{
    public string FromNodeId { get; set; } = string.Empty;
    public string ToNodeId { get; set; } = string.Empty;
    public int RailroadIndex { get; set; }
}

public sealed class TurnState
{
    public string Phase { get; set; } = "DrawDestination";
    public DiceResultState? DiceResult { get; set; }
    public int MovementAllowance { get; set; }
    public int MovementRemaining { get; set; }
    public bool BonusRollAvailable { get; set; }
    public List<int> RailroadsRiddenThisTurn { get; set; } = new();
    public ArrivalResolutionState? ArrivalResolution { get; set; }
}

public sealed class ArrivalResolutionState
{
    public int PlayerIndex { get; set; } = -1;
    public string DestinationCityName { get; set; } = string.Empty;
    public int PayoutAmount { get; set; }
    public int CashAfterPayout { get; set; }
    public bool PurchaseOpportunityAvailable { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class DiceResultState
{
    public int[] WhiteDice { get; set; } = Array.Empty<int>();
    public int? RedDie { get; set; }
}
