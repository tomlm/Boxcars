using Boxcars.Engine.Domain;

namespace Boxcars.Engine.Tests.Unit;

public class DestinationApproachPlannerTests
{
    // Railroad 1 is Bob's ($5,000) and railroad 2 is public ($1,000 unless a test says otherwise). Finish (D) is
    // served only by Bob, so arriving without a bonus out costs another $5,000 to get off, and a bonus move needs
    // 2 dots to reach other track.
    private const int Bob = 1;
    private const int Public = 2;
    private const double BobFee = 5000;

    [Fact]
    public void EnumerateRolls_CoverEveryOutcomeOnce()
    {
        foreach (var locomotiveType in Enum.GetValues<LocomotiveType>())
        {
            Assert.Equal(1d, DestinationApproachPlanner.EnumerateRolls(locomotiveType).Sum(roll => roll.Probability), precision: 10);
        }
    }

    [Fact]
    public void Superchief_ArrivingStuckThisTurn_LaysUpForAWhiteDiceArrivalNextTurn()
    {
        // Two dots left after the white dice (4) are used up, so arriving now strands the player on Bob's railroad.
        var request = CreateArrivalRequest(LocomotiveType.Superchief, movementUsed: 3, whiteDiceTotal: 4, redDie: 1);

        var decision = DestinationApproachPlanner.Evaluate(request);

        // Go: Bob now, then Bob again to get off. Lay up: public now, Bob next turn, and the red die gets off 5 times in 6.
        Assert.True(decision.LayUp);
        Assert.Equal(2 * BobFee, decision.ExpectedGoFees, precision: 2);
        Assert.Equal(1000 + BobFee + BobFee / 6, decision.ExpectedLayUpFees!.Value, precision: 2);
        Assert.Equal(["p1", "p2"], decision.LayUpSteps.Select(step => step.ToNodeId));
        Assert.Equal(["D"], decision.ContinuationSteps.Select(step => step.ToNodeId));
        Assert.Equal(1d, decision.ArriveNextTurnProbability, precision: 10);
        Assert.Equal(5d / 6, decision.BonusOutNextTurnProbability, precision: 10);
    }

    [Fact]
    public void Freight_ArrivingStuckThisTurn_GoesIn_BecauseItRarelyBonusesOut()
    {
        var request = CreateArrivalRequest(LocomotiveType.Freight, movementUsed: 0, whiteDiceTotal: 2, redDie: null);

        var decision = DestinationApproachPlanner.Evaluate(request);

        // Only double sixes (1 in 36) bring a bonus die, which gets off 5 times in 6.
        Assert.False(decision.LayUp);
        Assert.Equal(2 * BobFee, decision.ExpectedGoFees, precision: 2);
        Assert.Equal(1000 + BobFee + BobFee * (1 - 5d / 216), decision.ExpectedLayUpFees!.Value, precision: 2);
    }

    [Fact]
    public void FallingShortOnAnOpponentsRailroad_PaysThemAgain()
    {
        // 0 --Bob--> n1 --Bob--> n2 --Bob--> D: two dots this turn end on Bob's railroad, one short.
        // 0 --pub--> p1 --pub--> p2 --Bob--> D
        var graph = CreateGraph([("0", "n1", Bob), ("n1", "n2", Bob), ("n2", "D", Bob), ("0", "p1", Public), ("p1", "p2", Public), ("p2", "D", Bob)]);
        var request = CreateRequest(graph, LocomotiveType.Freight, goRoute: ["n1", "n2", "D"], movementRemaining: 2, whiteDiceTotal: 2);

        var decision = DestinationApproachPlanner.Evaluate(request);

        var nextTurnOnBob = BobFee + BobFee * (1 - 5d / 216);
        Assert.True(decision.LayUp);
        Assert.Equal(BobFee + nextTurnOnBob, decision.ExpectedGoFees, precision: 2);
        Assert.Equal(1000 + nextTurnOnBob, decision.ExpectedLayUpFees!.Value, precision: 2);
    }

    [Fact]
    public void DestinationReachedOnPublicTrack_IsNotEvaluated()
    {
        var graph = CreateGraph([("0", "n1", Public), ("n1", "D", Public), ("0", "p1", Public), ("p1", "p2", Public)]);
        var request = CreateRequest(graph, LocomotiveType.Superchief, goRoute: ["n1", "D"], movementRemaining: 2, whiteDiceTotal: 2, exitCost: 0);

        var decision = DestinationApproachPlanner.Evaluate(request);

        Assert.False(decision.Evaluated);
        Assert.False(decision.LayUp);
    }

    [Fact]
    public void LayUpSavesLessThanTheTurnItCosts_GoesIn()
    {
        // Pricey public track: laying up saves $667 in fees (5000 + 5000 vs 3500 + 5000 + 5000/6) but takes a second turn.
        var request = CreateArrivalRequest(LocomotiveType.Superchief, movementUsed: 3, whiteDiceTotal: 4, redDie: 1, publicFee: 3500);

        var decision = DestinationApproachPlanner.Evaluate(request);

        Assert.False(decision.LayUp);
        Assert.Equal(1d, decision.ExpectedGoTurns, precision: 10);
        Assert.Equal(2d, decision.ExpectedLayUpTurns!.Value, precision: 10);
        Assert.True(decision.ExpectedLayUpFees < decision.ExpectedGoFees);
    }

    [Fact]
    public void LayingUpCostsTheSame_GoesIn()
    {
        // Free public track and no bonus out from the destination: both options pay Bob twice.
        var request = CreateArrivalRequest(LocomotiveType.Freight, movementUsed: 0, whiteDiceTotal: 2, redDie: null, publicFee: 0, bonusOutDots: null);

        var decision = DestinationApproachPlanner.Evaluate(request);

        Assert.True(decision.Evaluated);
        Assert.False(decision.LayUp);
        Assert.Equal(decision.ExpectedGoFees, decision.ExpectedLayUpFees!.Value, precision: 2);
    }

    [Fact]
    public void BonusRollStillToCome_IsNotEvaluated()
    {
        var request = CreateArrivalRequest(LocomotiveType.Express, movementUsed: 0, whiteDiceTotal: 2, redDie: null, bonusRollPending: true);

        var decision = DestinationApproachPlanner.Evaluate(request);

        Assert.False(decision.Evaluated);
    }

    /// <summary>
    /// 0 --Bob--> n1 --Bob--> D        (go in: arrives this turn on Bob's railroad)
    /// 0 --pub--> p1 --pub--> p2 --Bob--> D (lay up at p2, one dot out)
    /// </summary>
    private static DestinationApproachRequest CreateArrivalRequest(
        LocomotiveType locomotiveType,
        int movementUsed,
        int whiteDiceTotal,
        int? redDie,
        int publicFee = 1000,
        int? bonusOutDots = 2,
        bool bonusRollPending = false)
    {
        var graph = CreateGraph([("0", "n1", Bob), ("n1", "D", Bob), ("0", "p1", Public), ("p1", "p2", Public), ("p2", "D", Bob)]);
        return CreateRequest(
            graph,
            locomotiveType,
            goRoute: ["n1", "D"],
            movementRemaining: 2,
            whiteDiceTotal: whiteDiceTotal,
            redDie: redDie,
            movementUsed: movementUsed,
            publicFee: publicFee,
            bonusOutDots: bonusOutDots,
            bonusRollPending: bonusRollPending);
    }

    private static DestinationApproachRequest CreateRequest(
        Dictionary<string, List<ApproachStep>> graph,
        LocomotiveType locomotiveType,
        IReadOnlyList<string> goRoute,
        int movementRemaining,
        int whiteDiceTotal,
        int? redDie = null,
        int movementUsed = 0,
        int publicFee = 1000,
        int exitCost = 5000,
        int? bonusOutDots = 2,
        bool bonusRollPending = false)
    {
        return new DestinationApproachRequest
        {
            StartNodeId = "0",
            DestinationNodeId = "D",
            LocomotiveType = locomotiveType,
            MovementRemaining = movementRemaining,
            MovementUsed = movementUsed,
            WhiteDiceTotal = whiteDiceTotal,
            RedDie = redDie,
            BonusRollPending = bonusRollPending,
            GoRoute = Walk(graph, "0", goRoute),
            GetEdges = nodeId => graph.GetValueOrDefault(nodeId) ?? [],
            IsSegmentUsed = _ => false,
            IsOpponentRailroad = railroadIndex => railroadIndex == Bob,
            ResolveFee = railroadIndex => railroadIndex == Bob ? (int)BobFee : publicFee,
            ResolveFeeGroupFlag = railroadIndex => 1L << railroadIndex,
            FindContinuation = (fromNodeId, _) => graph[fromNodeId].Where(edge => edge.ToNodeId == "D").Take(1).ToList() is { Count: 1 } direct
                ? direct
                : null,
            DestinationExitCost = railroadIndex => railroadIndex == Bob ? exitCost : 0,
            MinimumBonusOutSegments = _ => bonusOutDots
        };
    }

    private static List<ApproachStep> Walk(Dictionary<string, List<ApproachStep>> graph, string fromNodeId, IReadOnlyList<string> nodeIds)
    {
        var steps = new List<ApproachStep>();
        foreach (var nodeId in nodeIds)
        {
            steps.Add(graph[fromNodeId].First(edge => edge.ToNodeId == nodeId));
            fromNodeId = nodeId;
        }

        return steps;
    }

    private static Dictionary<string, List<ApproachStep>> CreateGraph(IReadOnlyList<(string From, string To, int RailroadIndex)> segments)
    {
        var graph = new Dictionary<string, List<ApproachStep>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (from, to, railroadIndex) in segments)
        {
            var segmentKey = $"{from}|{to}|{railroadIndex}";
            Add(from, new ApproachStep(to, railroadIndex, segmentKey));
            Add(to, new ApproachStep(from, railroadIndex, segmentKey));
        }

        return graph;

        void Add(string nodeId, ApproachStep edge)
        {
            if (!graph.TryGetValue(nodeId, out var edges))
            {
                graph[nodeId] = edges = [];
            }

            edges.Add(edge);
        }
    }
}
