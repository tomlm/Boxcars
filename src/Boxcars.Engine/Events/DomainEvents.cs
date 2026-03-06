using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Domain;

namespace Boxcars.Engine.Events;

public sealed class DestinationAssignedEventArgs : EventArgs
{
    public Player Player { get; }
    public CityDefinition City { get; }

    public DestinationAssignedEventArgs(Player player, CityDefinition city)
    {
        Player = player;
        City = city;
    }
}

public sealed class DestinationReachedEventArgs : EventArgs
{
    public Player Player { get; }
    public CityDefinition City { get; }
    public int PayoutAmount { get; }

    public DestinationReachedEventArgs(Player player, CityDefinition city, int payoutAmount)
    {
        Player = player;
        City = city;
        PayoutAmount = payoutAmount;
    }
}

public sealed class UsageFeeChargedEventArgs : EventArgs
{
    public Player Rider { get; }
    public Player? Owner { get; }
    public int Amount { get; }
    public IReadOnlyList<int> RailroadIndices { get; }

    public UsageFeeChargedEventArgs(Player rider, Player? owner, int amount, IReadOnlyList<int> railroadIndices)
    {
        Rider = rider;
        Owner = owner;
        Amount = amount;
        RailroadIndices = railroadIndices;
    }
}

public sealed class AuctionStartedEventArgs : EventArgs
{
    public Railroad Railroad { get; }
    public IReadOnlyList<Player> EligibleBidders { get; }

    public AuctionStartedEventArgs(Railroad railroad, IReadOnlyList<Player> eligibleBidders)
    {
        Railroad = railroad;
        EligibleBidders = eligibleBidders;
    }
}

public sealed class AuctionCompletedEventArgs : EventArgs
{
    public Railroad Railroad { get; }
    public Player? Winner { get; }
    public int WinningBid { get; }

    public AuctionCompletedEventArgs(Railroad railroad, Player? winner, int winningBid)
    {
        Railroad = railroad;
        Winner = winner;
        WinningBid = winningBid;
    }
}

public sealed class TurnStartedEventArgs : EventArgs
{
    public Player Player { get; }
    public int TurnNumber { get; }

    public TurnStartedEventArgs(Player player, int turnNumber)
    {
        Player = player;
        TurnNumber = turnNumber;
    }
}

public sealed class GameOverEventArgs : EventArgs
{
    public Player Winner { get; }

    public GameOverEventArgs(Player winner)
    {
        Winner = winner;
    }
}

public sealed class PlayerBankruptEventArgs : EventArgs
{
    public Player Player { get; }

    public PlayerBankruptEventArgs(Player player)
    {
        Player = player;
    }
}

public sealed class LocomotiveUpgradedEventArgs : EventArgs
{
    public Player Player { get; }
    public LocomotiveType OldType { get; }
    public LocomotiveType NewType { get; }

    public LocomotiveUpgradedEventArgs(Player player, LocomotiveType oldType, LocomotiveType newType)
    {
        Player = player;
        OldType = oldType;
        NewType = newType;
    }
}
