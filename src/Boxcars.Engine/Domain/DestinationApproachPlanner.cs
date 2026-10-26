using System.Diagnostics;

namespace Boxcars.Engine.Domain;

/// <summary>One dot of a route: the track ridden into <see cref="ToNodeId"/>.</summary>
public readonly record struct ApproachStep(string ToNodeId, int RailroadIndex, string SegmentKey);

/// <summary>
/// What <see cref="DestinationApproachPlanner"/> needs to know about the player, this turn's dice and the map. The
/// callbacks let the engine's planner and the board's route suggestions share the evaluation.
/// </summary>
public sealed class DestinationApproachRequest
{
    public required string StartNodeId { get; init; }
    public required string DestinationNodeId { get; init; }
    public required LocomotiveType LocomotiveType { get; init; }

    /// <summary>Dots left to move this turn; the roll is already known.</summary>
    public required int MovementRemaining { get; init; }

    /// <summary>Dots already moved this turn.</summary>
    public int MovementUsed { get; init; }

    public int WhiteDiceTotal { get; init; }
    public int? RedDie { get; init; }

    /// <summary>An Express or Freight bonus die still to roll this turn, so this turn's movement isn't known yet.</summary>
    public bool BonusRollPending { get; init; }

    /// <summary>Fee groups (<see cref="ResolveFeeGroupFlag"/>) already owed this turn.</summary>
    public long FeeGroupsPaidThisTurn { get; init; }

    /// <summary>The cheapest route to the destination: the "go in" option.</summary>
    public required IReadOnlyList<ApproachStep> GoRoute { get; init; }

    public required Func<string, IEnumerable<ApproachStep>> GetEdges { get; init; }
    public required Func<string, bool> IsSegmentUsed { get; init; }
    public required Func<int, bool> IsOpponentRailroad { get; init; }
    public required Func<int, int> ResolveFee { get; init; }
    public required Func<int, long> ResolveFeeGroupFlag { get; init; }

    /// <summary>The cheapest route to the destination from a node, starting next turn, avoiding the given segments.</summary>
    public required Func<string, IReadOnlyCollection<string>, IReadOnlyList<ApproachStep>?> FindContinuation { get; init; }

    /// <summary>Fees to get off the destination next turn after arriving on a railroad, when no bonus move gets off.</summary>
    public required Func<int, int> DestinationExitCost { get; init; }

    /// <summary>Dots a bonus move needs to get off the destination after arriving on a railroad; null when it can't.</summary>
    public required Func<int, int?> MinimumBonusOutSegments { get; init; }

    /// <summary>
    /// Dots in an average turn. Only a destination this close after this turn's move is an approach worth laying up
    /// for, and a lay-up has to stay this close.
    /// </summary>
    public int AverageMovementPerTurn { get; init; } = 7;

    public TimeSpan TimeBudget { get; init; } = TimeSpan.FromSeconds(1.5);
}

public sealed class DestinationApproachDecision
{
    public static DestinationApproachDecision NotEvaluated { get; } = new();

    /// <summary>Whether the comparison ran; when it didn't, the player just goes in.</summary>
    public bool Evaluated { get; init; }

    /// <summary>Spend this turn's roll on friendly or public track instead of heading in.</summary>
    public bool LayUp { get; init; }

    public double ExpectedGoFees { get; init; }

    /// <summary>Expected turns to reach the destination going in, counting this one.</summary>
    public double ExpectedGoTurns { get; init; }

    /// <summary>The best lay-up's expected fees; null when there was no way to lay up.</summary>
    public double? ExpectedLayUpFees { get; init; }

    /// <summary>Expected turns to reach the destination after laying up, counting this one.</summary>
    public double? ExpectedLayUpTurns { get; init; }

    /// <summary>Why the planner went in or laid up, in a few words.</summary>
    public string Reason { get; init; } = string.Empty;

    public string? LayUpNodeId { get; init; }

    /// <summary>This turn's move when laying up: exactly the roll.</summary>
    public IReadOnlyList<ApproachStep> LayUpSteps { get; init; } = [];

    /// <summary>The route on from the lay-up spot to the destination.</summary>
    public IReadOnlyList<ApproachStep> ContinuationSteps { get; init; } = [];

    /// <summary>For the best lay-up: the chance of reaching the destination next turn.</summary>
    public double ArriveNextTurnProbability { get; init; }

    /// <summary>For the best lay-up: the chance of reaching the destination next turn and bonusing out.</summary>
    public double BonusOutNextTurnProbability { get; init; }
}

/// <summary>
/// Near a destination reached over opponents' railroads, decides whether to go in now or lay up: spend this turn's
/// roll on friendly or public track and approach next turn. Each option is scored by its expected fees, working
/// through every roll for next turn: arriving and bonusing out off the opponent's railroad, arriving and paying to
/// get out, or falling short on their railroad and paying them again. It then decides the way experienced players do
/// (issue #130): lay up whenever that saves a fee, unless going in bonuses out this turn. A lay-up whose worst roll next
/// turn costs no more than going in is always taken; otherwise it mustn't be likely to fall short, and if missing the
/// bonus out costs more than going in, the bonus out mustn't be unlikely.
/// </summary>
public static class DestinationApproachPlanner
{
    /// <summary>A lay-up has to reach the destination next turn at least this often, or it might fall short.</summary>
    public const double MinimumArriveNextTurnProbability = 0.8;

    /// <summary>
    /// When a lay-up that misses its bonus out next turn costs more than going in, the bonus out has to be at least this
    /// likely: a 5 or 6 on the red die is, a 6 alone isn't. A lay-up that costs no more without one is worth any chance.
    /// </summary>
    public const double MinimumBonusOutProbability = 1d / 3;

    // Rounding in summed roll chances shouldn't push an exact 1-in-3 under the line.
    private const double ProbabilityTolerance = 1e-9;

    private const int MaximumLayUpSpots = 12;
    private const double MinimumSaving = 0.5;

    public static DestinationApproachDecision Evaluate(DestinationApproachRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var goRoute = request.GoRoute;
        var movement = request.MovementRemaining;
        if (movement <= 0 || request.BonusRollPending || goRoute.Count == 0)
        {
            return DestinationApproachDecision.NotEvaluated;
        }

        // Only an approach that an average roll next turn finishes is worth laying up for.
        var turnLength = Math.Max(1, request.AverageMovementPerTurn);
        if (goRoute.Count - movement > turnLength)
        {
            return DestinationApproachDecision.NotEvaluated;
        }

        var goExitCost = request.DestinationExitCost(goRoute[^1].RailroadIndex);
        if (goExitCost <= 0 && !goRoute.Any(step => request.IsOpponentRailroad(step.RailroadIndex)))
        {
            return DestinationApproachDecision.NotEvaluated;
        }

        var stopwatch = Stopwatch.StartNew();
        var distances = DistancesFrom(request, request.DestinationNodeId);
        var go = EvaluateGo(request);

        LayUpOption? best = null;
        foreach (var walk in FindLayUpWalks(request, distances))
        {
            if (stopwatch.Elapsed >= request.TimeBudget)
            {
                break;
            }

            var walkSegmentKeys = walk.Select(step => step.SegmentKey).ToList();
            var continuation = request.FindContinuation(walk[^1].ToNodeId, walkSegmentKeys);
            // A lay-up waits within a turn of the destination; anything further is just a different route.
            if (continuation is null || continuation.Count == 0 || continuation.Count > turnLength)
            {
                continue;
            }

            var nextTurn = EvaluateNextTurn(request, continuation);
            var walkFees = FeesThisTurn(request, walk, 0, walk.Count);
            var layUp = new LayUpOption(
                walk,
                continuation,
                walkFees + nextTurn.ExpectedFees,
                1 + nextTurn.ExpectedTurns,
                nextTurn.ArriveProbability,
                nextTurn.BonusOutProbability,
                walkFees + nextTurn.WorstFees,
                walkFees + FreshTurnFees(request, continuation, 0, continuation.Count) + request.DestinationExitCost(continuation[^1].RailroadIndex));
            if (best is null || IsBetterLayUp(layUp, best, go.ExpectedFees))
            {
                best = layUp;
            }
        }

        var (laysUp, reason) = Decide(go, best);
        return new DestinationApproachDecision
        {
            Evaluated = true,
            LayUp = laysUp,
            Reason = reason,
            ExpectedGoFees = go.ExpectedFees,
            ExpectedGoTurns = go.ExpectedTurns,
            ExpectedLayUpFees = best?.ExpectedFees,
            ExpectedLayUpTurns = best?.ExpectedTurns,
            LayUpNodeId = best?.Walk[^1].ToNodeId,
            LayUpSteps = best?.Walk ?? [],
            ContinuationSteps = best?.Continuation ?? [],
            ArriveNextTurnProbability = best?.ArriveProbability ?? 0d,
            BonusOutNextTurnProbability = best?.BonusOutProbability ?? 0d
        };
    }

    /// <summary>Goes in or lays up the way experienced players do; see the class summary.</summary>
    private static (bool LayUp, string Reason) Decide(GoOption go, LayUpOption? layUp)
    {
        if (layUp is null)
        {
            return (false, "No spot to lay up on friendly or public track");
        }

        if (go.BonusesOut)
        {
            return (false, "Going in bonuses out this turn");
        }

        if (!SavesFees(layUp, go.ExpectedFees))
        {
            return (false, "Laying up doesn't save any fees");
        }

        if (NeverWorse(layUp, go.ExpectedFees))
        {
            return (true, "Laying up can't cost more than going in");
        }

        if (!ArrivesReliably(layUp))
        {
            return (false, $"Laying up might fall short ({Percent(layUp.ArriveProbability)} to arrive next turn)");
        }

        if (!BonusOutReliable(layUp, go.ExpectedFees))
        {
            return (false, $"Bonusing out next turn is unlikely ({Percent(layUp.BonusOutProbability)})");
        }

        return (true, "Laying up saves a fee");
    }

    /// <summary>
    /// Lay-ups the rules would take come first, then the cheapest, then the quickest, then the one that changes
    /// railroads least, the way a player would ride it.
    /// </summary>
    private static bool IsBetterLayUp(LayUpOption candidate, LayUpOption best, double goFees)
    {
        var candidateQualifies = Qualifies(candidate, goFees);
        var bestQualifies = Qualifies(best, goFees);
        if (candidateQualifies != bestQualifies)
        {
            return candidateQualifies;
        }

        if (Math.Abs(candidate.ExpectedFees - best.ExpectedFees) > MinimumSaving)
        {
            return candidate.ExpectedFees < best.ExpectedFees;
        }

        if (Math.Abs(candidate.ExpectedTurns - best.ExpectedTurns) > ProbabilityTolerance)
        {
            return candidate.ExpectedTurns < best.ExpectedTurns;
        }

        return RailroadChanges(candidate.Walk) < RailroadChanges(best.Walk);
    }

    private static int RailroadChanges(IReadOnlyList<ApproachStep> walk)
    {
        return walk.Zip(walk.Skip(1)).Count(pair => pair.First.RailroadIndex != pair.Second.RailroadIndex);
    }

    private static bool Qualifies(LayUpOption layUp, double goFees)
    {
        return SavesFees(layUp, goFees) && (NeverWorse(layUp, goFees) || (ArrivesReliably(layUp) && BonusOutReliable(layUp, goFees)));
    }

    // Even its worst roll next turn (falling short, or no bonus out) costs no more than going in.
    private static bool NeverWorse(LayUpOption layUp, double goFees)
    {
        return layUp.WorstFees <= goFees + MinimumSaving;
    }

    private static bool SavesFees(LayUpOption layUp, double goFees)
    {
        return layUp.ExpectedFees < goFees - MinimumSaving;
    }

    private static bool ArrivesReliably(LayUpOption layUp)
    {
        return layUp.ArriveProbability + ProbabilityTolerance >= MinimumArriveNextTurnProbability;
    }

    // A lay-up that costs no more than going in even without a bonus out is worth any chance of one.
    private static bool BonusOutReliable(LayUpOption layUp, double goFees)
    {
        return layUp.FeesWithoutBonusOut <= goFees + MinimumSaving
            || layUp.BonusOutProbability + ProbabilityTolerance >= MinimumBonusOutProbability;
    }

    private static string Percent(double probability)
    {
        return $"{Math.Round(probability * 100):0}%";
    }

    private static GoOption EvaluateGo(DestinationApproachRequest request)
    {
        var goRoute = request.GoRoute;
        var movement = request.MovementRemaining;
        if (goRoute.Count > movement)
        {
            var nextTurn = EvaluateNextTurn(request, goRoute.Skip(movement).ToList());
            return new GoOption(FeesThisTurn(request, goRoute, 0, movement) + nextTurn.ExpectedFees, 1 + nextTurn.ExpectedTurns, BonusesOut: false);
        }

        // Arrives this turn. A Superchief that arrives within the white dice bonuses out with the red die.
        var fees = FeesThisTurn(request, goRoute, 0, goRoute.Count);
        var arrivalRailroad = goRoute[^1].RailroadIndex;
        var exitCost = request.DestinationExitCost(arrivalRailroad);
        var bonusesOut = exitCost > 0
            && request.LocomotiveType == LocomotiveType.Superchief
            && request.RedDie is int redDie
            && request.WhiteDiceTotal > 0
            && request.MovementUsed + goRoute.Count <= request.WhiteDiceTotal
            && request.MinimumBonusOutSegments(arrivalRailroad) is int escape
            && redDie >= escape;
        return new GoOption(fees + (bonusesOut ? 0 : exitCost), 1, bonusesOut);
    }

    /// <summary>Expected fees and turns from the start of next turn, following <paramref name="path"/> to the destination.</summary>
    private static NextTurnOutcome EvaluateNextTurn(DestinationApproachRequest request, IReadOnlyList<ApproachStep> path)
    {
        var arrivalRailroad = path[^1].RailroadIndex;
        var exitCost = request.DestinationExitCost(arrivalRailroad);
        var escape = exitCost > 0 ? request.MinimumBonusOutSegments(arrivalRailroad) : 0;
        var allFees = FreshTurnFees(request, path, 0, path.Count);

        var expectedFees = 0d;
        var worstFees = 0d;
        var expectedTurns = 0d;
        var arriveProbability = 0d;
        var bonusOutProbability = 0d;
        foreach (var roll in EnumerateRolls(request.LocomotiveType))
        {
            if (roll.White >= path.Count)
            {
                // Arrives on the white dice; the extra die, if any, is a bonus move off the destination.
                var bonusesOut = exitCost <= 0 || (roll.ExtraDie is int extraDie && escape is int dots && extraDie >= dots);
                var arrivalFees = allFees + (bonusesOut ? 0 : exitCost);
                expectedFees += roll.Probability * arrivalFees;
                worstFees = Math.Max(worstFees, arrivalFees);
                expectedTurns += roll.Probability;
                arriveProbability += roll.Probability;
                bonusOutProbability += bonusesOut ? roll.Probability : 0d;
                continue;
            }

            var dotsMoved = roll.White + (roll.ExtraDie ?? 0);
            if (dotsMoved >= path.Count)
            {
                expectedFees += roll.Probability * (allFees + exitCost);
                worstFees = Math.Max(worstFees, allFees + exitCost);
                expectedTurns += roll.Probability;
                arriveProbability += roll.Probability;
                continue;
            }

            // Short: whatever track the turn ends on is paid for again on the turns after.
            var shortFees = FreshTurnFees(request, path, 0, dotsMoved) + LaterTurnsFees(request, path, dotsMoved, exitCost);
            expectedFees += roll.Probability * shortFees;
            worstFees = Math.Max(worstFees, shortFees);
            expectedTurns += roll.Probability * (1 + LaterTurns(request, path.Count - dotsMoved));
        }

        return new NextTurnOutcome(expectedFees, worstFees, expectedTurns, arriveProbability, bonusOutProbability);
    }

    /// <summary>Fees for the rest of the path, a fresh fee per group each average turn, then getting off the destination.</summary>
    private static int LaterTurnsFees(DestinationApproachRequest request, IReadOnlyList<ApproachStep> path, int from, int exitCost)
    {
        var turnLength = Math.Max(1, request.AverageMovementPerTurn);
        var fees = 0;
        for (var turnStart = from; turnStart < path.Count; turnStart += turnLength)
        {
            fees += FreshTurnFees(request, path, turnStart, Math.Min(path.Count, turnStart + turnLength));
        }

        return fees + exitCost;
    }

    /// <summary>Average turns to ride the rest of a path.</summary>
    private static int LaterTurns(DestinationApproachRequest request, int dotsLeft)
    {
        var turnLength = Math.Max(1, request.AverageMovementPerTurn);
        return (dotsLeft + turnLength - 1) / turnLength;
    }

    private static int FeesThisTurn(DestinationApproachRequest request, IReadOnlyList<ApproachStep> steps, int from, int to)
    {
        return Fees(request, steps, from, to, request.FeeGroupsPaidThisTurn);
    }

    private static int FreshTurnFees(DestinationApproachRequest request, IReadOnlyList<ApproachStep> steps, int from, int to)
    {
        return Fees(request, steps, from, to, paidFeeGroups: 0);
    }

    /// <summary>One turn's fees for riding the steps: each fee group is paid once.</summary>
    private static int Fees(DestinationApproachRequest request, IReadOnlyList<ApproachStep> steps, int from, int to, long paidFeeGroups)
    {
        var fees = 0;
        for (var index = from; index < to; index++)
        {
            var railroadIndex = steps[index].RailroadIndex;
            var feeGroupFlag = request.ResolveFeeGroupFlag(railroadIndex);
            if ((paidFeeGroups & feeGroupFlag) == 0)
            {
                fees += request.ResolveFee(railroadIndex);
                paidFeeGroups |= feeGroupFlag;
            }
        }

        return fees;
    }

    /// <summary>
    /// Ways to spend exactly this turn's roll on friendly or public track without reaching the destination or riding
    /// track twice, one per lay-up spot, nearest the destination first. Each step keeps one walk per dot, railroad and
    /// the dot it came from: walks that meet head-on have ridden different track, so each can go where the other can't
    /// (issue #138).
    /// </summary>
    private static List<IReadOnlyList<ApproachStep>> FindLayUpWalks(DestinationApproachRequest request, Dictionary<string, int> distances)
    {
        var layer = new Dictionary<(string NodeId, int RailroadIndex, string FromNodeId), List<ApproachStep>>
        {
            [(request.StartNodeId, -1, string.Empty)] = []
        };

        for (var step = 0; step < request.MovementRemaining && layer.Count > 0; step++)
        {
            var nextLayer = new Dictionary<(string NodeId, int RailroadIndex, string FromNodeId), List<ApproachStep>>();
            foreach (var ((nodeId, _, _), walk) in layer)
            {
                foreach (var edge in request.GetEdges(nodeId))
                {
                    var key = (edge.ToNodeId.ToUpperInvariant(), edge.RailroadIndex, nodeId.ToUpperInvariant());
                    if (nextLayer.ContainsKey(key)
                        || request.IsOpponentRailroad(edge.RailroadIndex)
                        || string.Equals(edge.ToNodeId, request.DestinationNodeId, StringComparison.OrdinalIgnoreCase)
                        || request.IsSegmentUsed(edge.SegmentKey)
                        || walk.Any(ridden => string.Equals(ridden.SegmentKey, edge.SegmentKey, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    nextLayer[key] = [.. walk, edge];
                }
            }

            layer = nextLayer;
        }

        if (layer.Count == 0)
        {
            return [];
        }

        return layer.Values
            .Where(walk => walk.Count == request.MovementRemaining)
            .GroupBy(walk => walk[^1].ToNodeId, StringComparer.OrdinalIgnoreCase)
            .Select(spot => spot.MinBy(walk => FreshTurnFees(request, walk, 0, walk.Count))!)
            .OrderBy(walk => distances.GetValueOrDefault(walk[^1].ToNodeId, int.MaxValue))
            .Take(MaximumLayUpSpots)
            .Select(walk => (IReadOnlyList<ApproachStep>)walk)
            .ToList();
    }

    private static Dictionary<string, int> DistancesFrom(DestinationApproachRequest request, string nodeId)
    {
        var distances = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [nodeId] = 0 };
        var queue = new Queue<string>();
        queue.Enqueue(nodeId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var edge in request.GetEdges(current))
            {
                if (distances.TryAdd(edge.ToNodeId, distances[current] + 1))
                {
                    queue.Enqueue(edge.ToNodeId);
                }
            }
        }

        return distances;
    }

    /// <summary>
    /// Every roll a turn can bring. The extra die is a Superchief's red die, or the bonus die after Express doubles
    /// or Freight double sixes: a bonus move after arriving on the white dice, otherwise more movement.
    /// </summary>
    internal static IEnumerable<Roll> EnumerateRolls(LocomotiveType locomotiveType)
    {
        for (var first = 1; first <= 6; first++)
        {
            for (var second = 1; second <= 6; second++)
            {
                var white = first + second;
                var hasExtraDie = locomotiveType switch
                {
                    LocomotiveType.Superchief => true,
                    LocomotiveType.Express => first == second,
                    _ => first == 6 && second == 6
                };

                if (!hasExtraDie)
                {
                    yield return new Roll(1d / 36, white, null);
                    continue;
                }

                for (var extraDie = 1; extraDie <= 6; extraDie++)
                {
                    yield return new Roll(1d / 216, white, extraDie);
                }
            }
        }
    }

    /// <summary>The chance a turn's roll moves exactly <paramref name="dots"/>, counting any extra die as movement.</summary>
    public static double ChanceOfRolling(LocomotiveType locomotiveType, int dots)
    {
        var chances = RollTotalChances[locomotiveType];
        return dots >= 0 && dots < chances.Length ? chances[dots] : 0d;
    }

    private static readonly Dictionary<LocomotiveType, double[]> RollTotalChances = Enum.GetValues<LocomotiveType>()
        .ToDictionary(locomotiveType => locomotiveType, locomotiveType =>
        {
            var chances = new double[19];
            foreach (var roll in EnumerateRolls(locomotiveType))
            {
                chances[roll.White + (roll.ExtraDie ?? 0)] += roll.Probability;
            }

            return chances;
        });

    internal readonly record struct Roll(double Probability, int White, int? ExtraDie);

    /// <param name="WorstFees">The most any roll next turn ends up costing: falling short, or arriving without a bonus out.</param>
    private readonly record struct NextTurnOutcome(double ExpectedFees, double WorstFees, double ExpectedTurns, double ArriveProbability, double BonusOutProbability);

    /// <param name="BonusesOut">Arrives this turn and bonuses out off the opponent's railroad.</param>
    private readonly record struct GoOption(double ExpectedFees, double ExpectedTurns, bool BonusesOut);

    /// <param name="WorstFees">The most it can cost, whatever next turn's roll.</param>
    /// <param name="FeesWithoutBonusOut">Fees if it arrives next turn but doesn't bonus out.</param>
    private sealed record LayUpOption(
        IReadOnlyList<ApproachStep> Walk,
        IReadOnlyList<ApproachStep> Continuation,
        double ExpectedFees,
        double ExpectedTurns,
        double ArriveProbability,
        double BonusOutProbability,
        double WorstFees,
        double FeesWithoutBonusOut);
}
