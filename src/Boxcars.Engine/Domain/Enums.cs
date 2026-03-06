namespace Boxcars.Engine.Domain;

public enum GameStatus
{
    NotStarted,
    InProgress,
    Completed
}

public enum TurnPhase
{
    DrawDestination,
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
