using Boxcars.Engine.Persistence;

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

    /// <summary>True when the player has declared an attempt to return home and win.</summary>
    public bool HasDeclared { get; init; }

    /// <summary>Alternate destination used if the declared player falls below the winning threshold.</summary>
    public string AlternateCity { get; init; } = string.Empty;

    /// <summary>True if the player's turn is active.</summary>
    public bool IsActiveTurn { get; init; }

    /// <summary>True when this player won the game.</summary>
    public bool IsWinner { get; init; }

    /// <summary>True when the player has been eliminated and is out of the game.</summary>
    public bool IsEliminated { get; init; }

    /// <summary>True when the player is currently connected to the game session.</summary>
    public bool IsConnected { get; init; } = true;

    /// <summary>True when the seat-control settings menu should be shown.</summary>
    public bool ShowDisconnectedControlToggle { get; init; }

    /// <summary>The control options available in the seat-control settings menu.</summary>
    public IReadOnlyList<PlayerControlOptionModel> ControlModeOptions { get; init; } = [];

    /// <summary>True when the current user can switch the seat to AI control.</summary>
    public bool CanSelectAiControl { get; init; }

    /// <summary>True when the current user can switch the seat to manual control.</summary>
    public bool CanSelectManualControl { get; init; }

    /// <summary>True when AI control is currently selected for the disconnected seat.</summary>
    public bool IsAiControlSelected { get; init; }

    /// <summary>True when manual control is currently selected for the disconnected seat.</summary>
    public bool IsManualControlSelected { get; init; }

    /// <summary>The selected seat-control mode.</summary>
    public string DisconnectedControlMode { get; init; } = DisconnectedSeatControlModes.Self;

    /// <summary>User id of the participant currently controlling this player via delegation.</summary>
    public string DelegatedControllerUserId { get; init; } = string.Empty;

    /// <summary>Display name of the participant currently controlling this player via delegation.</summary>
    public string DelegatedControllerDisplayName { get; init; } = string.Empty;

    /// <summary>True when the player currently has active bot control.</summary>
    public bool HasActiveBotControl { get; init; }

    /// <summary>True when the player's moves are currently being made by AI.</summary>
    public bool IsAiControlled { get; init; }

    /// <summary>The resolved controller mode for the seat.</summary>
    public string ControllerMode { get; init; } = SeatControllerModes.Self;

    /// <summary>True when this seat is a dedicated bot player rather than a disconnected human in bot mode.</summary>
    public bool IsBotPlayer { get; init; }

    /// <summary>True when the current viewer should see the exact cash amount instead of the coarse public indicator.</summary>
    public bool CanViewExactCash => !KeepCashSecret
        || IsCurrentUser
        || (IsDelegatedToCurrentUser && !HasActiveBotControl)
        || Cash >= AnnouncingCashThreshold;

    /// <summary>True when opponents should see concealed cash below the announcing threshold.</summary>
    public bool KeepCashSecret { get; init; } = GameSettings.Default.KeepCashSecret;

    /// <summary>The cash threshold at or above which exact cash becomes public.</summary>
    public int AnnouncingCashThreshold { get; init; } = GameSettings.Default.AnnouncingCash;

    /// <summary>Assigned bot definition id when available.</summary>
    public string AssignedBotDefinitionId { get; init; } = string.Empty;

    /// <summary>Status label displayed for the current bot control state.</summary>
    public string BotControlStatusLabel { get; init; } = string.Empty;

    /// <summary>True when the last bot control state became invalid because the definition disappeared.</summary>
    public bool HasMissingBotDefinition { get; init; }

    /// <summary>Locomotive type label (e.g. "Freight", "Express", "Superchief").</summary>
    public string LocomotiveLabel { get; init; } = "Freight";

    /// <summary>Number of railroads owned by this player.</summary>
    public int OwnedRailroadCount { get; init; }

    /// <summary>Coverage metrics shown in the player tooltip.</summary>
    public IReadOnlyList<RailroadOverlayMetricRow> CoverageMetrics { get; init; } = [];

    /// <summary>Current fees owed by the player for the active turn.</summary>
    public int FeesOwed { get; init; }

    /// <summary>Pending bonus movement label for the active player, when applicable.</summary>
    public string BonusPendingLabel { get; init; } = string.Empty;

    /// <summary>
    /// Returns a money display string.
    /// For the current user, returns the exact formatted amount.
    /// For concealed opponents, returns a coarse threshold-based cash indicator.
    /// </summary>
    public string GetMoneyDisplay()
    {
        if (CanViewExactCash)
        {
            return $"${Cash:N0}";
        }

        var threshold = Math.Max(1, AnnouncingCashThreshold);
        var dollarSigns = Math.Clamp((int)Math.Ceiling((double)(Cash * 5) / threshold), 1, 5);

        return new string('$', dollarSigns);
    }
}

public sealed record PlayerControlOptionModel(string Value, string Label);
