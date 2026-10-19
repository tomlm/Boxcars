using System.Text.RegularExpressions;

namespace Boxcars.Data;

/// <summary>
/// Notifications name players with a placeholder for their seat, filled in with the seat's current name when shown,
/// so a player who changes their name in settings is called by it everywhere, including in earlier notifications.
/// </summary>
public static partial class PlayerNameTokens
{
    /// <summary>The placeholder for the player in seat <paramref name="playerIndex"/>.</summary>
    public static string For(int playerIndex)
    {
        return $"[[player:{playerIndex}]]";
    }

    /// <summary>
    /// The text with each placeholder replaced by <paramref name="nameForPlayer"/>'s name for that seat, or
    /// "Player N" when it has none.
    /// </summary>
    public static string Resolve(string? text, Func<int, string?> nameForPlayer)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("[[player:", StringComparison.Ordinal))
        {
            return text ?? string.Empty;
        }

        return TokenPattern().Replace(text, match =>
        {
            var playerIndex = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            var name = nameForPlayer(playerIndex);
            return string.IsNullOrWhiteSpace(name) ? $"Player {playerIndex + 1}" : name;
        });
    }

    [GeneratedRegex(@"\[\[player:(\d+)\]\]")]
    private static partial Regex TokenPattern();
}
