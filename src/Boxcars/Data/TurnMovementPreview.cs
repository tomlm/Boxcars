namespace Boxcars.Data;

public sealed class TurnMovementPreview
{
    public static TurnMovementPreview Empty { get; } = new();

    public IReadOnlyList<string> NodeIds { get; init; } = [];
    public IReadOnlyList<string> SegmentKeys { get; init; } = [];
    public int MoveCount { get; init; }
    public int FeeEstimate { get; init; }
    public bool HasUnfriendlyFee { get; init; }
    public bool ExhaustsMovement { get; init; }
}