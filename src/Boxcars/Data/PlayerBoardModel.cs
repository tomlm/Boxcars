using Microsoft.FluentUI.AspNetCore.Components;

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
    /// The city name if at destination, or the destination city name if en route.
    /// </summary>
    public string LocationLabel { get; init; } = string.Empty;

    /// <summary>
    /// Distance (nodes remaining) to destination. Relevant only when not at destination.
    /// </summary>
    public int DistanceToDestination { get; init; }

    /// <summary>Expected payoff for the current route, in dollars.</summary>
    public int Payoff { get; init; }

    /// <summary>Exact cash in dollars (only visible for the logged-in player).</summary>
    public int Cash { get; init; }

    /// <summary>True if this player is the currently logged-in user.</summary>
    public bool IsCurrentUser { get; init; }

    /// <summary>Home city name.</summary>
    public string HomeCity { get; init; } = string.Empty;

    /// <summary>True if the player's turn is active.</summary>
    public bool IsActiveTurn { get; init; }

    /// <summary>True when the player is currently connected to the game session.</summary>
    public bool IsConnected { get; init; } = true;

    /// <summary>Visual presence status derived from connection state.</summary>
    public PresenceStatus Status => IsConnected ? PresenceStatus.Available : PresenceStatus.Offline;

    /// <summary>Locomotive type label (e.g. "Freight", "Express", "Superchief").</summary>
    public string LocomotiveLabel { get; init; } = "Freight";

    /// <summary>
    /// Returns a money display string.
    /// For the current user, returns the exact formatted amount.
    /// For opponents, returns $ symbols where each $ represents $50k.
    /// </summary>
    public string GetMoneyDisplay()
    {
        if (IsCurrentUser)
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
