using Holler;
using Holler.Server;

namespace Boxcars.Services;

/// <summary>
/// Lets only the seated players of a game into its video room, and allows no direct calls.
/// </summary>
public sealed class GameVideoAuthorizer(IServiceScopeFactory scopeFactory) : IHollerAuthorizer
{
    private const string RoomPrefix = "boxcars-game:";

    /// <summary>The Holler room for a game's players.</summary>
    public static string RoomName(string gameId) => RoomPrefix + gameId;

    public ValueTask<bool> CanCallAsync(HollerPeerInfo caller, string calleeUserId) => ValueTask.FromResult(false);

    public async ValueTask<bool> CanJoinRoomAsync(HollerPeerInfo session, string room)
    {
        if (!room.StartsWith(RoomPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        // The authorizer is a singleton; GameService is scoped.
        await using var scope = scopeFactory.CreateAsyncScope();
        var gameService = scope.ServiceProvider.GetRequiredService<GameService>();
        var seats = await gameService.GetGameSeatStatesAsync(room[RoomPrefix.Length..], CancellationToken.None);
        return seats.Any(seat => string.Equals(seat.PlayerUserId, session.UserId, StringComparison.OrdinalIgnoreCase));
    }
}
