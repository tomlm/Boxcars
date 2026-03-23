using Boxcars.Engine.Domain;

namespace Boxcars.Engine.Persistence;

public sealed record GameSettings
{
    public const int InitialSchemaVersion = 1;

    public static GameSettings Default { get; } = new();

    public int StartingCash { get; init; } = 20_000;
    public int AnnouncingCash { get; init; } = 250_000;
    public int WinningCash { get; init; } = 300_000;
    public int RoverCash { get; init; } = 50_000;
    public int PublicFee { get; init; } = 1_000;
    public int PrivateFee { get; init; } = 1_000;
    public int UnfriendlyFee1 { get; init; } = 5_000;
    public int UnfriendlyFee2 { get; init; } = 10_000;
    public bool HomeSwapping { get; init; } = true;
    public bool HomeCityChoice { get; init; } = true;
    public bool KeepCashSecret { get; init; } = true;
    public LocomotiveType StartEngine { get; init; } = LocomotiveType.Freight;
    public int SuperchiefPrice { get; init; } = 40_000;
    public int ExpressPrice { get; init; } = 4_000;
    public int SchemaVersion { get; init; } = InitialSchemaVersion;
}

public sealed record ResolvedGameSettings(
    GameSettings Settings,
    string Source,
    IReadOnlyList<string> Warnings);