using System.Globalization;
using Boxcars.Data;
using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Domain;
using Boxcars.Services.Maps;

namespace Boxcars.Services;

public sealed class PurchaseRecommendationService
{
    // Blended score weights; each metric is first scaled against the best candidate (cheapest for price).
    private const decimal BlendedAccessWeight = 0.35m;
    private const decimal BlendedMonopolyWeight = 0.20m;
    private const decimal BlendedConnectivityWeight = 0.15m;
    private const decimal BlendedStrategyWeight = 0.15m;
    private const decimal BlendedPriceWeight = 0.15m;

    // Engine upgrade rules, from the played-turns simulations on the enginesimulation branch
    // (specs/006-engine-purchase-value/analysis.md).
    // Once this share of railroads is sold, a Freight or Express mostly loses money, so Superchief is the pick.
    private const decimal SuperchiefAlwaysSoldShare = 2m / 3m;
    // Before that, only a railroad this good pays back faster than a Superchief: a toll booth, any monopoly gain, or
    // at least this much access.
    private const decimal StrongRailroadAccessGain = 10m;
    // Express gains little and its price doesn't count toward a Superchief, so it's only worth it when nothing better
    // is on offer and a Superchief is a long way off: less than this share of its price to spare. Closer than that,
    // save for the Superchief.
    private const decimal ExpressMaximumShareOfSuperchief = 2m / 3m;

    // A railroad the buyer can't afford yet is only worth saving for when the best one they can buy now is worth less
    // than this share of it in the category; otherwise buying now beats waiting.
    private const decimal SaveForMinimumAdvantageShare = 0.5m;

    private static readonly NetworkCoverageService NetworkCoverage = new();

    public RecommendationInputSet BuildInputSet(
        MapAnalysisReport mapAnalysisReport,
        IEnumerable<int> affordableRailroadIndices,
        IEnumerable<int> unownedRailroadIndices,
        IEnumerable<LocomotiveType> eligibleEngineTypes,
        NetworkCoverageSnapshot? currentCoverage,
        IReadOnlyDictionary<int, NetworkCoverageSnapshot> projectedCoverageByRailroad)
    {
        ArgumentNullException.ThrowIfNull(mapAnalysisReport);
        ArgumentNullException.ThrowIfNull(affordableRailroadIndices);
        ArgumentNullException.ThrowIfNull(unownedRailroadIndices);
        ArgumentNullException.ThrowIfNull(eligibleEngineTypes);
        ArgumentNullException.ThrowIfNull(projectedCoverageByRailroad);

        return new RecommendationInputSet
        {
            MapAnalysisReport = mapAnalysisReport,
            AffordableRailroadIndices = affordableRailroadIndices.Distinct().Order().ToList(),
            UnownedRailroadIndices = unownedRailroadIndices.Distinct().Order().ToList(),
            EligibleEngineTypes = eligibleEngineTypes.Distinct().Order().ToList(),
            CurrentCoverage = currentCoverage,
            ProjectedCoverageByRailroad = projectedCoverageByRailroad
        };
    }

    /// <summary>
    /// The railroad to recommend to the buyer in each category, from every railroad for sale: the best one they can
    /// safely afford, unless it's worth less than half of the best one for sale, which is then one to save for, with
    /// negative <see cref="PurchaseRecommendation.SpareCash"/> (how far short). To afford a railroad, after paying for it
    /// and the fees owed, the buyer must still hold the operating reserve, less the fees opponents are expected to pay
    /// soon (income can stand in for the reserve, but not for fees owed now). Categories with no railroad that helps
    /// are left out, and the same railroad can win several.
    /// </summary>
    public static PurchaseRecommendationSet BuildRecommendations(
        MapDefinition mapDefinition,
        IReadOnlyCollection<int> buyerOwnedRailroadIndices,
        IReadOnlyList<PurchaseCandidate> candidates,
        IReadOnlyList<RecommendationOpponent> opponents,
        PurchaseFinances finances,
        EngineUpgradeInputs? engine = null)
    {
        ArgumentNullException.ThrowIfNull(mapDefinition);
        ArgumentNullException.ThrowIfNull(buyerOwnedRailroadIndices);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(opponents);
        ArgumentNullException.ThrowIfNull(finances);

        var currentCoverage = NetworkCoverage.BuildSnapshot(mapDefinition, buyerOwnedRailroadIndices);
        var incomingFees = finances.EstimateIncomingFees(buyerOwnedRailroadIndices);
        var operatingReserve = finances.OperatingReserve;
        PurchaseRecommendationSet Result(IReadOnlyList<PurchaseRecommendation> recommendations, bool strongRailroadOnOffer) =>
            new(recommendations, finances.Cash, finances.FeesOwed, incomingFees, operatingReserve,
                engine is null
                    ? EngineUpgradeRecommendation.None
                    : RecommendEngineUpgrade(engine, finances, incomingFees, strongRailroadOnOffer, recommendations.Any(r => r.CanBuyNow)));

        if (candidates.Count == 0)
        {
            return Result([], strongRailroadOnOffer: false);
        }

        var nodesByRailroad = BuildNodesByRailroad(mapDefinition);
        var ownedNetworks = BuildOwnedNetworks(buyerOwnedRailroadIndices, nodesByRailroad);
        var leader = FindStrongestOpponent(mapDefinition, opponents);
        var destinationNodes = BuildOpponentDestinationNodes(mapDefinition, opponents, nodesByRailroad);
        var monopolyNodes = NetworkCoverageService.FindMonopolyCityNodes(mapDefinition, buyerOwnedRailroadIndices);

        var scored = candidates
            .Select(candidate =>
            {
                var projected = NetworkCoverage.BuildProjectedSnapshot(mapDefinition, buyerOwnedRailroadIndices, candidate.RailroadIndex);
                var ownedAfterPurchase = buyerOwnedRailroadIndices.Append(candidate.RailroadIndex).ToList();
                var spareCash = finances.Cash - candidate.PurchasePrice - finances.FeesOwed
                    - OperatingCapitalService.ReserveAfterIncomingFees(operatingReserve, finances.EstimateIncomingFees(ownedAfterPurchase));
                var candidateNodes = nodesByRailroad.GetValueOrDefault(candidate.RailroadIndex) ?? [];
                var networksJoined = ownedNetworks.Count(network => network.Overlaps(candidateNodes));
                var connections = nodesByRailroad.Count(pair => pair.Key != candidate.RailroadIndex && pair.Value.Overlaps(candidateNodes));
                var leaderAccessGain = leader is null
                    ? 0m
                    : NetworkCoverage.BuildProjectedSnapshot(mapDefinition, leader.Value.Opponent.OwnedRailroadIndices, candidate.RailroadIndex).AccessibleDestinationPercent
                        - leader.Value.Coverage.AccessibleDestinationPercent;
                // Opponents whose destination this purchase closes off: they'd have to cross the buyer's railroads to get there.
                var monopolyNodesAfterPurchase = NetworkCoverageService.FindMonopolyCityNodes(mapDefinition, ownedAfterPurchase);
                var tollPayers = destinationNodes
                    .Where(entry => monopolyNodesAfterPurchase.Contains(entry.NodeId) && !monopolyNodes.Contains(entry.NodeId))
                    .Select(entry => (entry.Opponent.Name, City: entry.Opponent.DestinationCityName!))
                    .ToList();
                return new ScoredCandidate(
                    candidate,
                    AccessGain: projected.AccessibleDestinationPercent - currentCoverage.AccessibleDestinationPercent,
                    MonopolyGain: projected.MonopolyDestinationPercent - currentCoverage.MonopolyDestinationPercent,
                    // With no railroads yet, connectivity is how many other lines it links to.
                    Connectivity: buyerOwnedRailroadIndices.Count == 0 ? connections : networksJoined,
                    LeaderAccessGain: leaderAccessGain,
                    SpareCash: spareCash,
                    TollPayers: tollPayers);
            })
            .ToList();

        var recommendations = new List<PurchaseRecommendation>();
        void Add(PurchaseRecommendationCategory category, ScoredCandidate? pick, string reason)
        {
            if (pick is not null)
            {
                recommendations.Add(new PurchaseRecommendation(category, pick.Candidate.RailroadIndex, pick.Candidate.RailroadName, pick.Candidate.PurchasePrice, reason, pick.SpareCash));
            }
        }

        // Price alone never makes a railroad worth buying, so blended needs some gain.
        // Whether to save is judged on gains alone: being cheap doesn't make a railroad worth buying instead.
        var blended = Best(scored, BlendedScores(scored), c => c.AccessGain > 0m || c.MonopolyGain > 0m || c.LeaderAccessGain > 0m,
            BlendedScores(scored, includePrice: false));
        Add(PurchaseRecommendationCategory.Blended, blended, blended is null ? string.Empty : Describe(blended));

        var access = Best(scored, scored.ToDictionary(c => c, c => c.AccessGain), c => c.AccessGain > 0m);
        Add(PurchaseRecommendationCategory.Access, access, access is null ? string.Empty : $"+{FormatPercent(access.AccessGain)} access");

        var connectivity = Best(scored, scored.ToDictionary(c => c, c => c.Connectivity + (c.AccessGain / 1000m)), c => c.Connectivity > 0);
        Add(PurchaseRecommendationCategory.Connectivity, connectivity, connectivity is null ? string.Empty : DescribeConnectivity(connectivity, buyerOwnedRailroadIndices.Count));

        var monopoly = Best(scored, scored.ToDictionary(c => c, c => c.MonopolyGain), c => c.MonopolyGain > 0m);
        Add(PurchaseRecommendationCategory.Monopoly, monopoly, monopoly is null ? string.Empty : $"+{FormatPercent(monopoly.MonopolyGain)} monopoly");

        var strategy = Best(scored, scored.ToDictionary(c => c, c => c.LeaderAccessGain), c => c.LeaderAccessGain > 0m);
        Add(PurchaseRecommendationCategory.Strategy, strategy, strategy is null || leader is null
            ? string.Empty
            : $"Denies {leader.Value.Opponent.Name} +{FormatPercent(strategy.LeaderAccessGain)} access");

        // Cheapest railroad that still adds access; the bigger gain breaks a price tie. It's only one to save for when
        // no railroad that adds access can be bought now.
        var lowPrice = scored
            .Where(c => c.AccessGain > 0m)
            .OrderBy(c => c.SpareCash < 0)
            .ThenBy(c => c.Candidate.PurchasePrice)
            .ThenByDescending(c => c.AccessGain)
            .FirstOrDefault();
        Add(PurchaseRecommendationCategory.LowPrice, lowPrice, lowPrice is null ? string.Empty : $"+{FormatPercent(lowPrice.AccessGain)} access");

        var tollBooth = Best(scored, scored.ToDictionary(c => c, c => (decimal)c.TollPayers.Count), c => c.TollPayers.Count > 0);
        Add(PurchaseRecommendationCategory.TollBooth, tollBooth, tollBooth is null ? string.Empty : DescribeTollPayers(tollBooth.TollPayers));

        // A Superchief is only the pick over railroads the buyer can buy now if none of them is this good.
        var strongRailroadOnOffer = scored.Any(c => c.SpareCash >= 0
            && (c.TollPayers.Count > 0 || c.MonopolyGain > 0m || c.AccessGain >= StrongRailroadAccessGain));
        return Result(recommendations, strongRailroadOnOffer);
    }

    /// <summary>
    /// The engine upgrade to recommend. Buying one must leave the operating reserve after fees owed (less fees coming
    /// in, as for railroads), and a player who already has winning cash must keep it.
    /// <list type="bullet">
    /// <item>A Superchief is the pick once two thirds of the railroads are sold, or before that unless a railroad on
    /// offer is a toll booth, adds a monopoly, or adds 10% access or more.</item>
    /// <item>When it's the pick but can't be afforded yet: Express, from Freight, if no railroad is recommended and less
    /// than two thirds of the Superchief's price is to spare (a Superchief is a long way off); otherwise save for the
    /// Superchief.</item>
    /// </list>
    /// </summary>
    public static EngineUpgradeRecommendation RecommendEngineUpgrade(
        EngineUpgradeInputs engine,
        PurchaseFinances finances,
        int incomingFees,
        bool strongRailroadOnOffer,
        bool anyRailroadRecommended)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(finances);

        var reserve = OperatingCapitalService.ReserveAfterIncomingFees(finances.OperatingReserve, incomingFees);
        var spareCash = finances.Cash - finances.FeesOwed - reserve;
        bool Affordable(int price) =>
            price > 0
            && spareCash - price >= 0
            && (finances.Cash < engine.WinningCash || finances.Cash - finances.FeesOwed - price >= engine.WinningCash);

        var superchiefIsThePick = engine.CurrentEngine < LocomotiveType.Superchief
            && engine.SuperchiefPrice > 0
            && (1m - engine.UnsoldShare >= SuperchiefAlwaysSoldShare || !strongRailroadOnOffer);
        if (!superchiefIsThePick)
        {
            return EngineUpgradeRecommendation.None;
        }

        if (Affordable(engine.SuperchiefPrice))
        {
            return EngineUpgradeRecommendation.Superchief;
        }

        // With winning cash already, there's nothing to save for.
        if (finances.Cash >= engine.WinningCash)
        {
            return EngineUpgradeRecommendation.None;
        }

        if (engine.CurrentEngine == LocomotiveType.Freight
            && !anyRailroadRecommended
            && spareCash < engine.SuperchiefPrice * ExpressMaximumShareOfSuperchief
            && Affordable(engine.ExpressPrice))
        {
            return EngineUpgradeRecommendation.Express;
        }

        return EngineUpgradeRecommendation.SaveForSuperchief;
    }

    private static Dictionary<ScoredCandidate, decimal> BlendedScores(List<ScoredCandidate> scored, bool includePrice = true)
    {
        decimal Scale(decimal value, decimal max) => max > 0m && value > 0m ? value / max : 0m;
        var maxAccess = scored.Max(c => c.AccessGain);
        var maxMonopoly = scored.Max(c => c.MonopolyGain);
        var maxConnectivity = scored.Max(c => (decimal)c.Connectivity);
        var maxLeader = scored.Max(c => c.LeaderAccessGain);
        var minPrice = scored.Min(c => c.Candidate.PurchasePrice);
        var maxPrice = scored.Max(c => c.Candidate.PurchasePrice);

        return scored.ToDictionary(c => c, c =>
            (BlendedAccessWeight * Scale(c.AccessGain, maxAccess))
            + (BlendedMonopolyWeight * Scale(c.MonopolyGain, maxMonopoly))
            + (BlendedConnectivityWeight * Scale(c.Connectivity, maxConnectivity))
            + (BlendedStrategyWeight * Scale(c.LeaderAccessGain, maxLeader))
            + (includePrice
                ? BlendedPriceWeight * (maxPrice > minPrice ? (decimal)(maxPrice - c.Candidate.PurchasePrice) / (maxPrice - minPrice) : 1m)
                : 0m));
    }

    /// <summary>
    /// Highest score among eligible candidates; the cheaper railroad breaks a tie. When the buyer can't afford that one
    /// yet, the best one they can afford wins instead, unless its value is under
    /// <see cref="SaveForMinimumAdvantageShare"/> of the top one's: only then is the top one worth saving for.
    /// </summary>
    /// <param name="values">What each candidate is worth when weighing buying now against saving; defaults to <paramref name="scores"/>.</param>
    private static ScoredCandidate? Best(
        List<ScoredCandidate> scored,
        Dictionary<ScoredCandidate, decimal> scores,
        Func<ScoredCandidate, bool> eligible,
        Dictionary<ScoredCandidate, decimal>? values = null)
    {
        values ??= scores;
        var ranked = scored
            .Where(eligible)
            .OrderByDescending(c => scores[c])
            .ThenBy(c => c.Candidate.PurchasePrice)
            .ToList();
        var top = ranked.FirstOrDefault();
        var buyable = ranked.FirstOrDefault(c => c.SpareCash >= 0);
        return top is null || buyable is null || values[buyable] >= values[top] * SaveForMinimumAdvantageShare
            ? buyable ?? top
            : top;
    }

    private static string Describe(ScoredCandidate candidate)
    {
        return candidate.MonopolyGain > 0m
            ? $"+{FormatPercent(candidate.AccessGain)} access, +{FormatPercent(candidate.MonopolyGain)} monopoly"
            : $"+{FormatPercent(candidate.AccessGain)} access";
    }

    private static string DescribeConnectivity(ScoredCandidate candidate, int ownedRailroadCount)
    {
        return ownedRailroadCount == 0
            ? $"Links to {candidate.Connectivity} other railroads"
            : candidate.Connectivity >= 2
                ? $"Joins {candidate.Connectivity} of your networks"
                : "Extends your network";
    }

    private static string DescribeTollPayers(IReadOnlyList<(string Name, string City)> tollPayers)
    {
        return tollPayers.Count == 1
            ? $"{tollPayers[0].Name} must pay you to reach {tollPayers[0].City}"
            : $"{string.Join(" and ", tollPayers.Select(payer => payer.Name))} must pay you to reach their destinations";
    }

    /// <summary>For each opponent headed to a city on a railroad, that city's map node.</summary>
    private static List<(RecommendationOpponent Opponent, string NodeId)> BuildOpponentDestinationNodes(
        MapDefinition mapDefinition,
        IReadOnlyList<RecommendationOpponent> opponents,
        Dictionary<int, HashSet<string>> nodesByRailroad)
    {
        var result = new List<(RecommendationOpponent, string)>();
        foreach (var opponent in opponents.Where(opponent => !string.IsNullOrWhiteSpace(opponent.DestinationCityName)))
        {
            var city = mapDefinition.Cities.FirstOrDefault(candidate => string.Equals(candidate.Name, opponent.DestinationCityName, StringComparison.OrdinalIgnoreCase));
            var region = city is null
                ? null
                : mapDefinition.Regions.FirstOrDefault(candidate => string.Equals(candidate.Code, city.RegionCode, StringComparison.OrdinalIgnoreCase));
            if (city?.MapDotIndex is not int dotIndex || region is null)
            {
                continue;
            }

            var nodeId = MapRouteService.NodeKey(region.Index, dotIndex);
            if (nodesByRailroad.Values.Any(nodes => nodes.Contains(nodeId)))
            {
                result.Add((opponent, nodeId));
            }
        }

        return result;
    }

    private static string FormatPercent(decimal value)
    {
        return string.Concat(value.ToString("0.#", CultureInfo.InvariantCulture), "%");
    }

    /// <summary>The opponent strongest by share of opponents' cash plus share of opponents' network access.</summary>
    private static (RecommendationOpponent Opponent, NetworkCoverageSnapshot Coverage)? FindStrongestOpponent(
        MapDefinition mapDefinition,
        IReadOnlyList<RecommendationOpponent> opponents)
    {
        if (opponents.Count == 0)
        {
            return null;
        }

        var withCoverage = opponents
            .Select(opponent => (Opponent: opponent, Coverage: NetworkCoverage.BuildSnapshot(mapDefinition, opponent.OwnedRailroadIndices)))
            .ToList();
        var totalCash = withCoverage.Sum(entry => (decimal)Math.Max(0, entry.Opponent.Cash));
        var totalAccess = withCoverage.Sum(entry => entry.Coverage.AccessibleDestinationPercent);

        return withCoverage
            .OrderByDescending(entry =>
                (totalCash > 0m ? Math.Max(0, entry.Opponent.Cash) / totalCash : 0m)
                + (totalAccess > 0m ? entry.Coverage.AccessibleDestinationPercent / totalAccess : 0m))
            .First();
    }

    /// <summary>Groups the buyer's railroads into networks of railroads that touch each other, as sets of dots.</summary>
    private static List<HashSet<string>> BuildOwnedNetworks(IReadOnlyCollection<int> ownedRailroadIndices, Dictionary<int, HashSet<string>> nodesByRailroad)
    {
        var networks = new List<HashSet<string>>();
        foreach (var railroadIndex in ownedRailroadIndices)
        {
            var nodes = new HashSet<string>(nodesByRailroad.GetValueOrDefault(railroadIndex) ?? [], StringComparer.OrdinalIgnoreCase);
            var touching = networks.Where(network => network.Overlaps(nodes)).ToList();
            foreach (var network in touching)
            {
                nodes.UnionWith(network);
                networks.Remove(network);
            }

            networks.Add(nodes);
        }

        return networks;
    }

    private static Dictionary<int, HashSet<string>> BuildNodesByRailroad(MapDefinition mapDefinition)
    {
        var nodesByRailroad = new Dictionary<int, HashSet<string>>();
        foreach (var segment in mapDefinition.RailroadRouteSegments)
        {
            if (!nodesByRailroad.TryGetValue(segment.RailroadIndex, out var nodes))
            {
                nodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                nodesByRailroad[segment.RailroadIndex] = nodes;
            }

            nodes.Add(MapRouteService.NodeKey(segment.StartRegionIndex, segment.StartDotIndex));
            nodes.Add(MapRouteService.NodeKey(segment.EndRegionIndex, segment.EndDotIndex));
        }

        return nodesByRailroad;
    }

    private sealed record ScoredCandidate(
        PurchaseCandidate Candidate,
        decimal AccessGain,
        decimal MonopolyGain,
        int Connectivity,
        decimal LeaderAccessGain,
        int SpareCash,
        IReadOnlyList<(string Name, string City)> TollPayers);
}
