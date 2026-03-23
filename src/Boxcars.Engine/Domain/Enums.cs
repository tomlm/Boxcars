namespace Boxcars.Engine.Domain;

public enum GameStatus
{
    NotStarted,
    InProgress,
    Completed
}

public enum TurnPhase
{
    HomeCityChoice,
    HomeSwap,
    DrawDestination,
    RegionChoice,
    Roll,
    Move,
    Arrival,
    Purchase,
    UseFees,
    EndTurn
}

public enum LocomotiveType
{
    Freight,
    Express,
    Superchief
}

public enum PendingDestinationAssignmentKind
{
    NormalDestination,
    DeclaredAlternateDestination
}
