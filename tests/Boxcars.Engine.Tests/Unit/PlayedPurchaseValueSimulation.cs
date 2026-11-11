using System.Diagnostics;
using System.Runtime.CompilerServices;
using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Domain;
using Boxcars.Engine.Persistence;
using Xunit.Abstractions;
using EngineGame = Boxcars.Engine.Domain.GameEngine;

namespace Boxcars.Engine.Tests.Unit;

// Engine vs railroad purchase value simulation; see specs/006-engine-purchase-value/analysis.md. Plays real turns (dice, route planner, bonus moves, fees) for all four players and
// measures how much cash "Me" (player 0) gains per turn with each engine, and with a railroad bought, in a fixed world.
[Collection(RoutePlanningCollection.Name)]
public class PlayedPurchaseValueSimulation(ITestOutputHelper output)
{
    private static readonly int TurnsPerPlayer = int.TryParse(Environment.GetEnvironmentVariable("SIM_TURNS"), out var turns) ? turns : 500;

    // Game 4184's railroads (player 0 = me), in index order.
    private static readonly int[] FullOwners = [0, 0, 3, 3, 0, 1, 2, 2, 3, 1, 0, 3, 0, 3, 3, 0, 1, 2, 0, 0, 0, 1, 2, 2, 0, 0, 0, 3, 2];

    // Takes minutes per variant, so it's skipped in normal test runs. To run it, remove Skip and use:
    //   dotnet test tests/Boxcars.Engine.Tests --filter "FullyQualifiedName~PlayedPurchaseValueSimulation" --logger "console;verbosity=detailed"
    [Fact(Skip = "Long-running simulation; run by hand.")]
    public void Simulate()
    {
        var map = MapDefinition.Parse(File.ReadAllText(MapPath())).Definition!;
        var early = Keep(2);
        var mid = Keep(5);
        var np = Index(map, "NP");
        var nw = Index(map, "N&W");
        // Unsold in MID; in the all-sold world opponents own them unless I buy one.
        var allSold = With(With(Eventual(mid), np, 1), nw, 2);

        var variants = new (string World, string Option, Dictionary<int, int> Owners, LocomotiveType Engine)[]
        {
            ("EARLY (2 each, 20 unsold = public)", "Freight", early, LocomotiveType.Freight),
            ("EARLY (2 each, 20 unsold = public)", "Express", early, LocomotiveType.Express),
            ("EARLY (2 each, 20 unsold = public)", "Superchief", early, LocomotiveType.Superchief),
            ("MID (5 each, 9 unsold = public)", "Freight", mid, LocomotiveType.Freight),
            ("MID (5 each, 9 unsold = public)", "Express", mid, LocomotiveType.Express),
            ("MID (5 each, 9 unsold = public)", "Superchief", mid, LocomotiveType.Superchief),
            ("ALL SOLD (mid + unsold shared out)", "Freight", allSold, LocomotiveType.Freight),
            ("ALL SOLD (mid + unsold shared out)", "Express", allSold, LocomotiveType.Express),
            ("ALL SOLD (mid + unsold shared out)", "Superchief", allSold, LocomotiveType.Superchief),
            ("ALL SOLD (mid + unsold shared out)", "Freight + NP instead of an opponent", With(allSold, np, 0), LocomotiveType.Freight),
            ("ALL SOLD (mid + unsold shared out)", "Freight + N&W instead of an opponent", With(allSold, nw, 0), LocomotiveType.Freight),
        };

        var results = new (Stats Stats, double Seconds)[variants.Length];
        Parallel.For(0, variants.Length, i =>
        {
            var stopwatch = Stopwatch.StartNew();
            results[i] = (Play(map, variants[i].Owners, variants[i].Engine, seed: 1000), stopwatch.Elapsed.TotalSeconds);
        });

        string? world = null;
        for (var i = 0; i < variants.Length; i++)
        {
            if (variants[i].World != world)
            {
                world = variants[i].World;
                output.WriteLine($"=== {world}");
            }

            var s = results[i].Stats;
            output.WriteLine(
                $"  {variants[i].Option,-38} ${s.CashPerTurn,6:N0}/turn ±{s.StandardError,4:N0}  | trips {s.Trips,3} ({s.TurnsPerTrip:N2} turns each), payoff ${s.PayoffPerTurn,5:N0}/turn, fees paid ${s.FeesPaidPerTurn,5:N0}/turn, collected ${s.FeesCollectedPerTurn,5:N0}/turn, bonus moves {s.BonusMoves}  ({results[i].Seconds:N0}s)");
        }
    }

    private sealed record Stats(double CashPerTurn, double StandardError, int Trips, double TurnsPerTrip, double PayoffPerTurn, double FeesPaidPerTurn, double FeesCollectedPerTurn, int BonusMoves);

    private static Stats Play(MapDefinition map, Dictionary<int, int> owners, LocomotiveType myEngine, int seed)
    {
        var settings = GameSettings.Default with
        {
            HomeCityChoice = false,
            HomeSwapping = false,
            StartingCash = 10_000_000,
            AnnouncingCash = 900_000_000,
            WinningCash = 1_000_000_000
        };
        var engine = new EngineGame(map, ["Me", "P1", "P2", "P3"], new DefaultRandomProvider(seed), settings);
        foreach (var railroad in engine.Railroads)
        {
            railroad.Owner = owners.TryGetValue(railroad.Index, out var owner) ? engine.Players[owner] : null;
            railroad.Owner?.OwnedRailroads.Add(railroad);
        }

        typeof(EngineGame).GetProperty(nameof(engine.AllRailroadsSold))!.SetValue(engine, engine.Railroads.All(r => r.Owner is not null));
        var me = engine.Players[0];
        me.LocomotiveType = myEngine;

        var perTurn = new List<int>();
        var trips = 0;
        var bonusMoves = 0;
        var payoffs = 0L;
        engine.DestinationReached += (_, e) =>
        {
            if (e.Player == me)
            {
                trips++;
                payoffs += e.PayoutAmount;
            }
        };

        // Cash change from the start of one of my turns to the start of my next: my turn plus everyone else's, so fees
        // they pay me count too.
        var turnsPlayed = 0;
        var guard = 0;
        int? roundStartCash = null;
        while (turnsPlayed < TurnsPerPlayer)
        {
            var active = engine.CurrentTurn.ActivePlayer;
            if (active == me)
            {
                if (roundStartCash is { } start)
                {
                    perTurn.Add(me.Cash - start);
                }

                roundStartCash = me.Cash;
                turnsPlayed++;
            }

            PlayTurn(engine, ref bonusMoves, active == me);

            if (++guard > TurnsPerPlayer * 10)
            {
                throw new InvalidOperationException("Too many turns.");
            }
        }

        var mean = perTurn.Average();
        var sd = Math.Sqrt(perTurn.Sum(v => (v - mean) * (v - mean)) / (perTurn.Count - 1));
        return new Stats(
            mean,
            sd / Math.Sqrt(perTurn.Count),
            trips,
            trips > 0 ? (double)TurnsPerPlayer / trips : 0,
            (double)payoffs / TurnsPerPlayer,
            (double)me.TotalFeesPaid / TurnsPerPlayer,
            (double)me.TotalFeesCollected / TurnsPerPlayer,
            bonusMoves);
    }

    private static void PlayTurn(EngineGame engine, ref int bonusMoves, bool isMe)
    {
        var rolled = false;
        for (var step = 0; step < 100; step++)
        {
            switch (engine.CurrentTurn.Phase)
            {
                case TurnPhase.HomeSwap:
                    engine.ResolveHomeSwap(false);
                    break;
                case TurnPhase.DrawDestination:
                    if (rolled && isMe)
                    {
                        // A destination drawn after the roll this turn: arrived with a bonus to use on the next trip.
                        bonusMoves++;
                    }

                    engine.DrawDestination();
                    break;
                case TurnPhase.RegionChoice:
                    engine.ChooseDestinationRegion(engine.CurrentTurn.PendingRegionChoice!.EligibleRegionCodes[0]);
                    break;
                case TurnPhase.Roll:
                    engine.RollDice();
                    rolled = true;
                    break;
                case TurnPhase.Move:
                    var route = engine.SuggestRoute();
                    if (route.Segments.Count == 0)
                    {
                        throw new InvalidOperationException("No route.");
                    }

                    engine.SaveRoute(route);
                    engine.MoveAlongRoute(Math.Min(route.Segments.Count, engine.CurrentTurn.MovementRemaining));
                    break;
                case TurnPhase.Purchase:
                    engine.DeclinePurchase();
                    break;
                case TurnPhase.EndTurn:
                    engine.EndTurn();
                    return;
                default:
                    throw new InvalidOperationException($"Unhandled phase {engine.CurrentTurn.Phase}.");
            }
        }

        throw new InvalidOperationException("Turn didn't end.");
    }

    private static Dictionary<int, int> Keep(int perPlayer)
    {
        var owners = new Dictionary<int, int>();
        foreach (var group in Enumerable.Range(1, FullOwners.Length - 1).GroupBy(index => FullOwners[index]))
        {
            foreach (var index in group.Take(perPlayer))
            {
                owners[index] = group.Key;
            }
        }

        return owners;
    }

    private static Dictionary<int, int> Eventual(Dictionary<int, int> current)
    {
        var eventual = new Dictionary<int, int>(current);
        var unsold = Enumerable.Range(1, FullOwners.Length - 1).Where(index => !current.ContainsKey(index)).ToList();
        for (var i = 0; i < unsold.Count; i++)
        {
            eventual[unsold[i]] = (i + 1) % 4;
        }

        return eventual;
    }

    // The railroad goes to me; if it was already mine, Eventual gave it to me, so this compares against an opponent owning it.
    private static Dictionary<int, int> With(Dictionary<int, int> owners, int index, int owner) => new(owners) { [index] = owner };

    private static int Index(MapDefinition map, string shortName) => map.Railroads.First(r => r.ShortName == shortName).Index;

    private static string MapPath([CallerFilePath] string sourceFilePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFilePath)!, "..", "..", "..", "src", "Boxcars", "U21MAP.RB3"));
}
