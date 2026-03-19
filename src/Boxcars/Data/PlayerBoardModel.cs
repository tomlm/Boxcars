namespace Boxcars.Data;

/// <summary>
/// View model representing a single player's status on the player board.
/// </summary>
public sealed class PlayerBoardModel
{
    /// <summary>Player index (0-based turn order).</summary>
    public int Index { get; init; }

    /// <summary>Display name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>URL to the player's thumbnail image (may be empty).</summary>
    public string ThumbnailUrl { get; init; } = string.Empty;

    /// <summary>Player's assigned color (CSS-compatible value).</summary>
    public string Color { get; init; } = "#888";

    /// <summary>True when at a destination city, false when en route.</summary>
    public bool IsAtDestination { get; init; }

    /// <summary>
    /// The player's current trip origin or current city when not traveling.
    /// </summary>
    public string TripStartLabel { get; init; } = string.Empty;

    /// <summary>
    /// The player's current trip destination or current city when not traveling.
    /// </summary>
    public string TripDestinationLabel { get; init; } = string.Empty;

    /// <summary>
    /// Distance (nodes remaining) to destination. Relevant only when not at destination.
    /// </summary>
    public int DistanceToDestination { get; init; }

    /// <summary>Expected payoff for the current route, in dollars.</summary>
    public int Payoff { get; init; }

    /// <summary>Exact cash in dollars (only visible for the logged-in player or manual delegated controller).</summary>
    public int Cash { get; init; }

    /// <summary>True if this player is the currently logged-in user.</summary>
    public bool IsCurrentUser { get; init; }

    /// <summary>True if the current user can act for this player via delegated control.</summary>
    public bool IsDelegatedToCurrentUser { get; init; }

    /// <summary>Home city name.</summary>
    public string HomeCity { get; init; } = string.Empty;

    /// <summary>True if the player's turn is active.</summary>
    public bool IsActiveTurn { get; init; }

    /// <summary>True when the player has been eliminated and is out of the game.</summary>
    public bool IsEliminated { get; init; }

    /// <summary>True when the player is currently connected to the game session.</summary>
    public bool IsConnected { get; init; } = true;

    /// <summary>True when the disconnected-seat AI/Manual toggle should be shown.</summary>
    public bool ShowDisconnectedControlToggle { get; init; }

    /// <summary>True when the current user can switch the seat to AI control.</summary>
    public bool CanSelectAiControl { get; init; }

    /// <summary>True when the current user can switch the seat to manual control.</summary>
    public bool CanSelectManualControl { get; init; }

    /// <summary>True when AI control is currently selected for the disconnected seat.</summary>
    public bool IsAiControlSelected { get; init; }

    /// <summary>True when manual control is currently selected for the disconnected seat.</summary>
    public bool IsManualControlSelected { get; init; }

    /// <summary>User id of the participant currently controlling this player via delegation.</summary>
    public string DelegatedControllerUserId { get; init; } = string.Empty;

    /// <summary>Display name of the participant currently controlling this player via delegation.</summary>
    public string DelegatedControllerDisplayName { get; init; } = string.Empty;

    /// <summary>True when the player currently has a bot assignment record.</summary>
    public bool HasBotAssignment { get; init; }

    /// <summary>True when the player's moves are currently being made by AI.</summary>
    public bool IsAiControlled { get; init; }

    /// <summary>The resolved controller mode for the seat.</summary>
    public string ControllerMode { get; init; } = SeatControllerModes.Self;

    /// <summary>True when this seat is a dedicated bot player rather than a disconnected human in bot mode.</summary>
    public bool IsBotPlayer { get; init; }

    /// <summary>True when the current viewer should see the exact cash amount instead of the coarse public indicator.</summary>
    public bool CanViewExactCash => IsCurrentUser || (IsDelegatedToCurrentUser && !HasBotAssignment);

    /// <summary>Assigned bot definition id when available.</summary>
    public string AssignedBotDefinitionId { get; init; } = string.Empty;

    /// <summary>Status label displayed for the current bot assignment state.</summary>
    public string BotAssignmentStatusLabel { get; init; } = string.Empty;

    /// <summary>True when the last bot assignment became invalid because the definition disappeared.</summary>
    public bool HasMissingBotDefinition { get; init; }

    /// <summary>Locomotive type label (e.g. "Freight", "Express", "Superchief").</summary>
    public string LocomotiveLabel { get; init; } = "Freight";

    /// <summary>Coverage metrics shown in the player tooltip.</summary>
    public IReadOnlyList<RailroadOverlayMetricRow> CoverageMetrics { get; init; } = [];

    /// <summary>Current fees owed by the player for the active turn.</summary>
    public int FeesOwed { get; init; }

    /// <summary>Pending bonus movement label for the active player, when applicable.</summary>
    public string BonusPendingLabel { get; init; } = string.Empty;

    /// <summary>
    /// Returns a money display string.
    /// For the current user, returns the exact formatted amount.
    /// For opponents, returns $ symbols where each $ represents $50k.
    /// </summary>
    public string GetMoneyDisplay()
    {
        if (CanViewExactCash)
        {
            return $"${Cash:N0}";
        }

        // Each $ represents $50,000
        int dollarSigns = Cash switch
        {
            < 50_000 => 1,
            < 100_000 => 2,
            < 150_000 => 3,
            < 200_000 => 4,
            < 250_000 => 5,
            _ => 6
        };

        return new string('$', dollarSigns);
    }
}
