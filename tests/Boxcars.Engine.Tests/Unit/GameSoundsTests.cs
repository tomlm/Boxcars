using Boxcars.Data;
using Boxcars.Engine.Persistence;

namespace Boxcars.Engine.Tests.Unit;

public class GameSoundsTests
{
    private const int AnnouncingCash = 250_000;
    private const int Viewer = 1;

    [Fact]
    public void Detect_RollOfBoxcars_PlaysBoxcarsOnce()
    {
        var prior = CreateState();
        var rolled = CreateState();
        rolled.Turn.DiceResult = new DiceResultState { WhiteDice = [6, 6] };
        var moved = CreateState();
        moved.Turn.DiceResult = new DiceResultState { WhiteDice = [6, 6] };

        Assert.Equal([GameSound.Boxcars], Detect(prior, rolled));
        // Moving afterwards keeps the same dice, which isn't another roll.
        Assert.Empty(Detect(rolled, moved));
    }

    [Fact]
    public void Detect_OtherRoll_PlaysNothing()
    {
        var rolled = CreateState();
        rolled.Turn.DiceResult = new DiceResultState { WhiteDice = [6, 5] };

        Assert.Empty(Detect(CreateState(), rolled));
    }

    [Fact]
    public void Detect_Purchases_PlayForEveryone()
    {
        var bought = CreateState();
        bought.Players[0].OwnedRailroadIndices.Add(4);
        var express = CreateState();
        express.Players[0].LocomotiveType = "Express";
        var superchief = CreateState();
        superchief.Players[0].LocomotiveType = "Superchief";

        Assert.Equal([GameSound.RailroadPurchase], Detect(CreateState(), bought));
        Assert.Equal([GameSound.ExpressPurchase], Detect(CreateState(), express));
        Assert.Equal([GameSound.SuperchiefPurchase], Detect(express, superchief));
    }

    [Fact]
    public void Detect_FeesCollected_PlayChaChingOnlyForThePlayerPaid()
    {
        var paidViewer = CreateState();
        paidViewer.Players[Viewer].TotalFeesCollected = 5_000;
        var paidOther = CreateState();
        paidOther.Players[0].TotalFeesCollected = 5_000;

        Assert.Equal([GameSound.ChaChing], Detect(CreateState(), paidViewer));
        Assert.Empty(Detect(CreateState(), paidOther));
    }

    [Fact]
    public void Detect_ReachingAnnouncingCash_PlaysOnlyTheFirstTime()
    {
        var prior = CreateState();
        prior.Players[0].Cash = 240_000;
        var reached = CreateState();
        reached.Players[0].Cash = 255_000;

        Assert.Equal([GameSound.DeclarePending], Detect(prior, reached));
        Assert.Empty(Detect(prior, reached, playersAlreadyAnnounced: new HashSet<int> { 0 }));
    }

    [Fact]
    public void Detect_DeclarationRoverAndWinner_PlayForEveryone()
    {
        var declared = CreateState();
        declared.Players[0].HasDeclared = true;

        var roverPrior = CreateState();
        roverPrior.ActivePlayerIndex = 2;
        roverPrior.Players[0].HasDeclared = true;
        roverPrior.Players[0].DestinationCityName = "Chicago";
        roverPrior.Players[0].AlternateDestinationCityName = "Denver";
        var rovered = CreateState();
        rovered.ActivePlayerIndex = 2;
        rovered.Players[0].DestinationCityName = "Denver";

        var won = CreateState();
        won.WinnerIndex = 0;

        Assert.Equal([GameSound.Declaration], Detect(CreateState(), declared));
        Assert.Equal([GameSound.Rover], Detect(roverPrior, rovered));
        Assert.Equal([GameSound.Winner], Detect(CreateState(), won, viewersTurnStarted: true));
    }

    [Fact]
    public void Detect_ViewersTurn_PlaysTheTurnSoundLast()
    {
        var rolled = CreateState();
        rolled.Turn.DiceResult = new DiceResultState { WhiteDice = [6, 6] };

        Assert.Equal([GameSound.Boxcars, GameSound.PlayerTurn], Detect(CreateState(), rolled, viewersTurnStarted: true));
    }

    [Fact]
    public void GetUrl_PlayerTurn_UsesThePlayersSoundOrTheDefault()
    {
        Assert.Equal("/user-photo/sound-1", GameSounds.GetUrl(GameSound.PlayerTurn, "/user-photo/sound-1"));
        Assert.Equal(GameSounds.DefaultTurnSoundUrl, GameSounds.GetUrl(GameSound.PlayerTurn, ""));
    }

    private static IReadOnlyList<GameSound> Detect(
        GameState prior,
        GameState current,
        bool viewersTurnStarted = false,
        IReadOnlySet<int>? playersAlreadyAnnounced = null)
    {
        return GameSounds.Detect(prior, current, Viewer, viewersTurnStarted, AnnouncingCash, playersAlreadyAnnounced ?? new HashSet<int>());
    }

    private static GameState CreateState()
    {
        return new GameState
        {
            GameStatus = "InProgress",
            TurnNumber = 3,
            ActivePlayerIndex = 0,
            Players = [new PlayerState { Name = "A" }, new PlayerState { Name = "B" }, new PlayerState { Name = "C" }],
            Turn = new TurnState { Phase = "Roll" }
        };
    }
}
