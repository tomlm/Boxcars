using Boxcars.Engine.Persistence;

namespace Boxcars.Data;

/// <summary>A moment in the game that plays a sound.</summary>
public enum GameSound
{
    Boxcars,
    RailroadPurchase,
    ExpressPurchase,
    SuperchiefPurchase,
    ChaChing,
    Rover,
    DeclarePending,
    Declaration,
    Winner,
    PlayerTurn
}

/// <summary>A "your turn" sound a player can pick in their preferences.</summary>
public sealed record StockTurnSound(string Name, string Url);

/// <summary>
/// The game's sounds: which files they play, and which ones a game update sets off. Everyone hears the game's events;
/// only the player concerned hears <see cref="GameSound.PlayerTurn"/> and <see cref="GameSound.ChaChing"/>.
/// </summary>
public static class GameSounds
{
    /// <summary>The stock "your turn" sounds; a player can also upload their own.</summary>
    public static IReadOnlyList<StockTurnSound> StockTurnSounds { get; } =
    [
        new("Bell", "sounds/turn/bell.wav"),
        new("Chime", "sounds/turn/chime.wav"),
        new("Beep", "sounds/turn/beep.wav"),
        new("Double beep", "sounds/turn/double-beep.wav"),
        new("Whistle", "sounds/turn/whistle.wav"),
        new("Ding", "sounds/turn/ding.wav"),
        new("Beep boop", "sounds/turn/beep-boop.wav")
    ];

    public static string DefaultTurnSoundUrl => StockTurnSounds[0].Url;

    /// <summary>The file a sound plays. <paramref name="turnSoundUrl"/> is the player's own pick for their turn.</summary>
    public static string GetUrl(GameSound sound, string? turnSoundUrl = null)
    {
        return sound switch
        {
            GameSound.Boxcars => "sounds/boxcars.wav",
            GameSound.RailroadPurchase => "sounds/railroad-purchase.wav",
            GameSound.ExpressPurchase => "sounds/express-purchase.wav",
            GameSound.SuperchiefPurchase => "sounds/superchief-purchase.wav",
            GameSound.ChaChing => "sounds/cha-ching.wav",
            GameSound.Rover => "sounds/rover.wav",
            GameSound.DeclarePending => "sounds/declare-pending.wav",
            GameSound.Declaration => "sounds/declaration.wav",
            GameSound.Winner => "sounds/winner.wav",
            GameSound.PlayerTurn => string.IsNullOrWhiteSpace(turnSoundUrl) ? DefaultTurnSoundUrl : turnSoundUrl,
            _ => throw new ArgumentOutOfRangeException(nameof(sound), sound, null)
        };
    }

    /// <summary>
    /// The sounds one game update sets off for a viewer, in the order they play.
    /// </summary>
    /// <param name="viewerPlayerIndex">The viewer's seat, or -1 when they're only watching.</param>
    /// <param name="viewersTurnStarted">Whether this update starts a turn the viewer plays.</param>
    /// <param name="announcingCash">The cash at which a player must announce they're close to declaring.</param>
    /// <param name="playersAlreadyAnnounced">Players who reached <paramref name="announcingCash"/> earlier, who don't play it again.</param>
    public static IReadOnlyList<GameSound> Detect(
        GameState prior,
        GameState current,
        int viewerPlayerIndex,
        bool viewersTurnStarted,
        int announcingCash,
        IReadOnlySet<int> playersAlreadyAnnounced)
    {
        var sounds = new List<GameSound>();
        if (IsNewRoll(prior, current) && current.Turn.DiceResult!.WhiteDice is [6, 6])
        {
            sounds.Add(GameSound.Boxcars);
        }

        var playerCount = Math.Min(prior.Players.Count, current.Players.Count);
        var players = Enumerable.Range(0, playerCount).Select(index => (Index: index, Before: prior.Players[index], After: current.Players[index])).ToList();

        if (players.Any(player => player.After.OwnedRailroadIndices.Except(player.Before.OwnedRailroadIndices).Any()))
        {
            sounds.Add(GameSound.RailroadPurchase);
        }

        if (players.Any(player => BoughtEngine(player.Before, player.After, "Express")))
        {
            sounds.Add(GameSound.ExpressPurchase);
        }

        if (players.Any(player => BoughtEngine(player.Before, player.After, "Superchief")))
        {
            sounds.Add(GameSound.SuperchiefPurchase);
        }

        if (viewerPlayerIndex >= 0
            && viewerPlayerIndex < playerCount
            && current.Players[viewerPlayerIndex].TotalFeesCollected > prior.Players[viewerPlayerIndex].TotalFeesCollected)
        {
            sounds.Add(GameSound.ChaChing);
        }

        if (players.Any(player => WasRovered(prior, player.Index, player.Before, player.After)))
        {
            sounds.Add(GameSound.Rover);
        }

        var threshold = Math.Max(1, announcingCash);
        if (players.Any(player => player.Before.Cash < threshold
            && player.After.Cash >= threshold
            && !playersAlreadyAnnounced.Contains(player.Index)))
        {
            sounds.Add(GameSound.DeclarePending);
        }

        if (players.Any(player => !player.Before.HasDeclared && player.After.HasDeclared))
        {
            sounds.Add(GameSound.Declaration);
        }

        if (!prior.WinnerIndex.HasValue && current.WinnerIndex.HasValue)
        {
            sounds.Add(GameSound.Winner);
        }
        else if (viewersTurnStarted)
        {
            sounds.Add(GameSound.PlayerTurn);
        }

        return sounds;
    }

    /// <summary>Whether the dice in <paramref name="current"/> come from a roll made since <paramref name="prior"/>.</summary>
    private static bool IsNewRoll(GameState prior, GameState current)
    {
        var dice = current.Turn.DiceResult;
        if (dice is null || dice.WhiteDice.Length == 0)
        {
            return false;
        }

        var priorDice = prior.Turn.DiceResult;
        return priorDice is null
            || prior.TurnNumber != current.TurnNumber
            || prior.ActivePlayerIndex != current.ActivePlayerIndex
            || !priorDice.WhiteDice.SequenceEqual(dice.WhiteDice)
            || priorDice.RedDie != dice.RedDie;
    }

    private static bool BoughtEngine(PlayerState before, PlayerState after, string locomotiveType)
    {
        return string.Equals(after.LocomotiveType, locomotiveType, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(before.LocomotiveType, locomotiveType, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A declared player sent to their alternate destination by the moving player (see
    /// <c>GameService.BuildRoverTimelineItems</c>).
    /// </summary>
    private static bool WasRovered(GameState prior, int playerIndex, PlayerState before, PlayerState after)
    {
        return before.HasDeclared
            && !after.HasDeclared
            && !string.IsNullOrWhiteSpace(before.AlternateDestinationCityName)
            && string.Equals(after.DestinationCityName, before.AlternateDestinationCityName, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(after.AlternateDestinationCityName)
            && prior.ActivePlayerIndex != playerIndex
            && prior.ActivePlayerIndex >= 0;
    }
}

/// <summary>A sound file a player picked but hasn't saved yet.</summary>
public sealed record PendingSoundUpload(byte[] Bytes, string ContentType)
{
    /// <summary>A URL the browser can play it from before it's uploaded.</summary>
    public string DataUrl => $"data:{ContentType};base64,{Convert.ToBase64String(Bytes)}";
}
