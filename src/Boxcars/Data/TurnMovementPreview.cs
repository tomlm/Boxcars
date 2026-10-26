namespace Boxcars.Data;

public sealed class TurnMovementPreview
{
    public static TurnMovementPreview Empty { get; } = new();

    public IReadOnlyList<string> NodeIds { get; init; } = [];
    public IReadOnlyList<string> SegmentKeys { get; init; } = [];
    public int MoveCount { get; init; }
    public int FeeEstimate { get; init; }
    public bool HasUnfriendlyFee { get; init; }
    /// <summary>The rover cash the move collects from declared opponents it lands on or passes through.</summary>
    public int RoverCashEstimate { get; init; }
    public bool ExhaustsMovement { get; init; }
}