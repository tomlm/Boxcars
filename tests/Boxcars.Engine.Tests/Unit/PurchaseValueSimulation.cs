using System.Diagnostics;
using System.Runtime.CompilerServices;
using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Domain;
using Boxcars.Engine.Persistence;
using Boxcars.Engine.Tests.TestDoubles;
using Xunit.Abstractions;
using EngineGame = Boxcars.Engine.Domain.GameEngine;

namespace Boxcars.Engine.Tests.Unit;

// Engine vs railroad purchase value simulation; see specs/006-engine-purchase-value/analysis.md. Compares buying an engine with buying a railroad as income per turn and payback turns.
//
// The "eventual" world: railroads still for sale end up with opponents (round-robin), since someone will buy them.
// Buying one instead puts it in the buyer's hands in that world. Income per turn for a player is
//   (average payoff - average fees per trip) / turns per trip  +  fees opponents pay them per turn,
// with fees and routes from the real route planner over a fixed sample of trips drawn with the map's odds.
[Collection(RoutePlanningCollection.Name)]
public class PurchaseValueSimulation(ITestOutputHelper output)
{
    private const int MyTrips = 40;
    private const int OpponentTrips = 20;

    // Takes minutes per variant, so it's skipped in normal test runs. To run it, remove Skip and use:
    //   dotnet test tests/Boxcars.Engine.Tests --filter "FullyQualifiedName~PurchaseValueSimulation" --logger "console;verbosity=detailed"
    [Fact(Skip = "Long-running simulation; run by hand.")]
    public void Simulate()
    {
        var map = MapDefinition.Parse(File.ReadAllText(MapPath())).Definition!;
        var random = new Random(42);
        var trips = Enumerable.Range(0, MyTrips + OpponentTrips * 3).Select(_ => DrawTrip(map, random)).ToList();

        // Full ownership from game 4184 (player 0 = me). Scenarios keep the first N railroads each player bought.
        int[] owners = [0, 0, 3, 3, 0, 1, 2, 2, 3, 1, 0, 3, 0, 3, 3, 0, 1, 2, 0, 0, 0, 1, 2, 2, 0, 0, 0, 3, 2];
        var scenarios = new (string Label, int Keep, LocomotiveType Engine)[]
        {
            ("EARLY: everyone owns 2 railroads, Freight", 2, LocomotiveType.Freight),
            ("EARLY-MID: everyone owns 3, Freight", 3, LocomotiveType.Freight),
            ("MID: everyone owns 5, Freight", 5, LocomotiveType.Freight),
            ("MID: everyone owns 5, already Express", 5, LocomotiveType.Express),
            ("LATE: everyone owns 6, Freight", 6, LocomotiveType.Freight),
        };
        var results = new string[scenarios.Length];
        Parallel.For(0, scenarios.Length, i => results[i] = Scenario(map, trips, scenarios[i].Label, owners, scenarios[i].Keep, scenarios[i].Engine, cash: 45_000));
        foreach (var result in results)
        {
            output.WriteLine(result);
        }
    }

    private static string Scenario(MapDefinition map, List<(CityDefinition From, CityDefinition To)> trips, string label, int[] fullOwners, int keepPerPlayer, LocomotiveType myEngine, int cash)
    {
        var stopwatch = Stopwatch.StartNew();
        var log = new System.Text.StringBuilder();
        var engine = new EngineGame(map, ["Me", "P1", "P2", "P3"], new FixedRandomProvider(), GameSettings.Default with { HomeCityChoice = false, HomeSwapping = false });
        // Current ownership: each player's first keepPerPlayer railroads (by index order), the rest unsold.
        var current = new Dictionary<int, int>();
        foreach (var group in Enumerable.Range(1, fullOwners.Length - 1).GroupBy(index => fullOwners[index]))
        {
            foreach (var index in group.Take(keepPerPlayer))
            {
                current[index] = group.Key;
            }
        }

        var unsold = engine.Railroads.Select(r => r.Index).Where(index => !current.ContainsKey(index)).ToList();
        // Eventually: unsold railroads are shared out among all four players in turn, opponents first.
        var eventual = new Dictionary<int, int>(current);
        for (var i = 0; i < unsold.Count; i++)
        {
            eventual[unsold[i]] = (i + 1) % 4;
        }

        log.AppendLine($"=== {label}  (me: {string.Join(", ", current.Where(p => p.Value == 0).Select(p => Short(engine, p.Key)))}; {unsold.Count} unsold; cash ${cash:N0})");

        var baseline = Income(engine, eventual, myEngine, trips);
        log.AppendLine($"  baseline {myEngine}: ${baseline.PerTurn:N0}/turn  (trip: payoff ${baseline.Payoff:N0}, fees ${baseline.Fees:N0}, {baseline.Turns:N2} turns; incoming ${baseline.Incoming:N0}/turn)");

        var options = new List<(string Name, int Price, IncomeResult Result, IncomeResult Without)>();
        foreach (var target in Enum.GetValues<LocomotiveType>().Where(t => t > myEngine))
        {
            var price = EngineGame.GetUpgradeCost(myEngine, target, engine.Settings);
            options.Add(($"Engine -> {target}", price, Income(engine, eventual, target, trips), baseline));
        }

        foreach (var index in unsold)
        {
            var price = engine.Railroads.First(r => r.Index == index).PurchasePrice;
            if (price > cash)
            {
                continue;
            }

            // Not buying it, an opponent eventually does; buying it, it's mine.
            var notMine = new Dictionary<int, int>(eventual);
            if (notMine[index] == 0)
            {
                notMine[index] = 1;
            }

            var mine = new Dictionary<int, int>(eventual) { [index] = 0 };
            options.Add(($"Buy {Short(engine, index)}", price, Income(engine, mine, myEngine, trips), notMine[index] == eventual[index] ? baseline : Income(engine, notMine, myEngine, trips)));
        }

        foreach (var (name, price, result, without) in options.OrderBy(o => Payback(o.Price, o.Result.PerTurn - o.Without.PerTurn)))
        {
            var gain = result.PerTurn - without.PerTurn;
            var payback = Payback(price, gain);
            log.AppendLine($"  {name,-22} ${price,7:N0}  +${gain,6:N0}/turn  payback {(payback > 999 ? "never" : payback.ToString("N1")),6} turns   (my fees/trip ${result.Fees - without.Fees,7:N0}, turns/trip {result.Turns - without.Turns,5:N2}, incoming {result.Incoming - without.Incoming,6:N0}/turn)");
        }

        log.AppendLine($"  ({stopwatch.Elapsed.TotalSeconds:N0}s)");
        return log.ToString();
    }

    private static double Payback(int price, double gain) => gain <= 0 ? 9999 : price / gain;

    private sealed record IncomeResult(double PerTurn, double Payoff, double Fees, double Turns, double Incoming);

    private static IncomeResult Income(EngineGame engine, Dictionary<int, int> owners, LocomotiveType myEngine, List<(CityDefinition From, CityDefinition To)> trips)
    {
        foreach (var player in engine.Players)
        {
            player.OwnedRailroads.Clear();
            player.LocomotiveType = LocomotiveType.Freight;
        }

        foreach (var railroad in engine.Railroads)
        {
            railroad.Owner = owners.TryGetValue(railroad.Index, out var owner) ? engine.Players[owner] : null;
            railroad.Owner?.OwnedRailroads.Add(railroad);
        }

        typeof(EngineGame).GetProperty(nameof(engine.AllRailroadsSold))!.SetValue(engine, engine.Railroads.All(r => r.Owner is not null));
        engine.Players[0].LocomotiveType = myEngine;

        // My trips.
        double payoff = 0, fees = 0, turns = 0;
        foreach (var (from, to) in trips.Take(MyTrips))
        {
            var (fee, dots) = PlanTrip(engine, 0, from, to, out _);
            map(engine).TryGetPayout(from.PayoutIndex ?? 0, to.PayoutIndex ?? 0, out var pay);
            payoff += pay;
            fees += fee;
            turns += dots / (double)AverageMovement(myEngine);
        }

        payoff /= MyTrips;
        fees /= MyTrips;
        turns /= MyTrips;

        // Fees each opponent pays me per turn of theirs; each of their turns is roughly one of mine.
        double incoming = 0;
        for (var opponent = 1; opponent <= 3; opponent++)
        {
            double paidToMe = 0, opponentTurns = 0;
            foreach (var (from, to) in trips.Skip(MyTrips + (opponent - 1) * OpponentTrips).Take(OpponentTrips))
            {
                var (_, dots) = PlanTrip(engine, opponent, from, to, out var toMe);
                paidToMe += toMe;
                opponentTurns += dots / (double)AverageMovement(LocomotiveType.Freight);
            }

            incoming += opponentTurns > 0 ? paidToMe / opponentTurns : 0;
        }

        return new IncomeResult((payoff - fees) / turns + incoming, payoff, fees, turns, incoming);
    }

    private static MapDefinition map(EngineGame engine) => engine.MapDefinition;

    private static (int Fees, int Dots) PlanTrip(EngineGame engine, int playerIndex, CityDefinition from, CityDefinition to, out int paidToMe)
    {
        var player = engine.Players[playerIndex];
        player.CurrentCity = from;
        player.CurrentNodeId = CityNode(engine, from);
        player.TripOriginCity = from;
        player.Destination = to;
        player.UsedSegments.Clear();
        engine.CurrentTurn.ActivePlayer = player;
        engine.CurrentTurn.Phase = TurnPhase.Roll;
        engine.CurrentTurn.RailroadsRiddenThisTurn.Clear();

        var route = engine.SuggestRouteForPlayer(playerIndex);
        var costs = engine.GetRouteSegmentCosts(playerIndex, route);
        paidToMe = 0;
        var fees = 0;
        for (var i = 0; i < costs.Count; i++)
        {
            var railroad = engine.Railroads.First(r => r.Index == route.Segments[i].RailroadIndex);
            if (playerIndex != 0 && railroad.Owner == engine.Players[0])
            {
                paidToMe += costs[i].FeeAdded;
            }

            fees += costs[i].FeeAdded;
        }

        return (fees, route.Segments.Count);
    }

    private static int AverageMovement(LocomotiveType type) => type switch
    {
        LocomotiveType.Superchief => 11,
        LocomotiveType.Express => 8,
        _ => 7
    };

    private static (CityDefinition From, CityDefinition To) DrawTrip(MapDefinition map, Random random)
    {
        var from = DrawCity(map, random);
        CityDefinition to;
        do
        {
            to = DrawCity(map, random);
        }
        while (to == from);
        return (from, to);
    }

    private static CityDefinition DrawCity(MapDefinition map, Random random)
    {
        var cities = map.Cities.Where(c => c.MapDotIndex.HasValue).ToList();
        var weights = cities.Select(c => map.Regions.First(r => r.Code == c.RegionCode).Probability * c.Probability).ToList();
        var roll = random.NextDouble() * weights.Sum();
        for (var i = 0; i < cities.Count; i++)
        {
            roll -= weights[i];
            if (roll <= 0)
            {
                return cities[i];
            }
        }

        return cities[^1];
    }

    private static string CityNode(EngineGame engine, CityDefinition city)
    {
        var region = engine.MapDefinition.Regions.First(r => r.Code == city.RegionCode);
        return $"{region.Index}:{city.MapDotIndex}";
    }

    private static string Short(EngineGame engine, int index) => engine.Railroads.First(r => r.Index == index).ShortName;

    private static string MapPath([CallerFilePath] string sourceFilePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFilePath)!, "..", "..", "..", "src", "Boxcars", "U21MAP.RB3"));
}
