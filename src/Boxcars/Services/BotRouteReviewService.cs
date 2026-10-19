using System.Collections.Concurrent;

namespace Boxcars.Services;

/// <summary>
/// In a game in debug mode, a bot move that would pay an opponent waits here for the game's admin, who approves it or
/// moves the bot along a route of their own. Kept in memory: after a restart the bot simply asks again.
/// </summary>
public sealed class BotRouteReviewService
{
    /// <summary>The bot decision source recorded on a move the admin approved.</summary>
    public const string ApprovedDecisionSource = "AdminApprovedRoute";

    private readonly ConcurrentDictionary<string, BotRouteReview> _pending = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, BotRouteReview> _approved = new(StringComparer.Ordinal);

    /// <summary>The bot move waiting for the admin in this game, if any.</summary>
    public BotRouteReview? GetPending(string gameId)
    {
        return _pending.TryGetValue(gameId, out var review) ? review : null;
    }

    public void SetPending(BotRouteReview review)
    {
        ArgumentNullException.ThrowIfNull(review);
        _pending[review.GameId] = review;
    }

    public void ClearPending(string gameId)
    {
        _pending.TryRemove(gameId, out _);
    }

    /// <summary>Approves the waiting move, so the bot makes it the next time it plays. False when nothing is waiting.</summary>
    public bool Approve(string gameId)
    {
        if (!_pending.TryRemove(gameId, out var review))
        {
            return false;
        }

        _approved[gameId] = review;
        return true;
    }

    /// <summary>
    /// Uses up an approval for this player's move this turn. Each approval covers one move, so a bonus move that also
    /// pays an opponent is asked about again.
    /// </summary>
    public bool TryConsumeApproval(string gameId, int turnNumber, int playerIndex)
    {
        if (!_approved.TryRemove(gameId, out var approved))
        {
            return false;
        }

        return approved.TurnNumber == turnNumber && approved.PlayerIndex == playerIndex;
    }

    /// <summary>Forgets anything waiting or approved, as when debug mode is turned off.</summary>
    public void Clear(string gameId)
    {
        _pending.TryRemove(gameId, out _);
        _approved.TryRemove(gameId, out _);
    }
}

/// <summary>A bot move that would pay opponents, waiting for the admin.</summary>
public sealed record BotRouteReview
{
    public required string GameId { get; init; }
    public required int TurnNumber { get; init; }
    public required int PlayerIndex { get; init; }
    public required string PlayerName { get; init; }

    /// <summary>The bot's planned route from where it stands to its destination.</summary>
    public required IReadOnlyList<string> RouteNodeIds { get; init; }

    public required IReadOnlyList<string> RouteSegmentKeys { get; init; }

    /// <summary>How many of the route's steps the bot moves this turn.</summary>
    public required int MoveSteps { get; init; }

    /// <summary>The nodes the bot passes through this turn, starting where it stands.</summary>
    public IReadOnlyList<string> MoveNodeIds => RouteNodeIds.Take(MoveSteps + 1).ToList();

    /// <summary>The track the bot rides this turn.</summary>
    public IReadOnlyList<string> MoveSegmentKeys => RouteSegmentKeys.Take(MoveSteps).ToList();

    /// <summary>What this move pays each opponent.</summary>
    public required IReadOnlyList<BotRouteReviewFee> Fees { get; init; }

    public int TotalFee => Fees.Sum(fee => fee.Amount);
}

public sealed record BotRouteReviewFee(string OwnerName, IReadOnlyList<string> RailroadNames, int Amount);
