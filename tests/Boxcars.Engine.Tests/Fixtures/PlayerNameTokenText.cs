using Boxcars.Data;
using Boxcars.Engine.Persistence;

namespace Boxcars.Engine.Tests.Fixtures;

/// <summary>Notification text as the board shows it: player placeholders filled in with the players' names.</summary>
public static class PlayerNameTokenText
{
    public static string Resolve(string text, GameState snapshot)
    {
        return PlayerNameTokens.Resolve(text, playerIndex => playerIndex >= 0 && playerIndex < snapshot.Players.Count
            ? snapshot.Players[playerIndex].Name
            : null);
    }

    public static IReadOnlyList<EventTimelineItem> Resolve(IReadOnlyList<EventTimelineItem> items, GameState? snapshot)
    {
        return snapshot is null
            ? items
            : items.Select(item => item with { Description = Resolve(item.Description, snapshot) }).ToList();
    }
}
