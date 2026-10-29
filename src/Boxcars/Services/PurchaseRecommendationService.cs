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
    /// The best railroad to buy in each category for the buyer. Only railroads the buyer can safely afford count:
    /// after paying for it and the fees owed, the buyer must still hold the operating reserve, less the fees opponents
    /// are expected to pay soon (income can stand in for the reserve, but not for fees owed now). Categories with no railroad that helps are left out, and the same railroad can
    /// win several.
    /// </summary>
    public static PurchaseRecommendationSet BuildRecommendations(
        MapDefinition mapDefinition,
        IReadOnlyCollection<int> buyerOwnedRailroadIndices,
        IReadOnlyList<PurchaseCandidate> candidates,
        IReadOnlyList<RecommendationOpponent> opponents,
        PurchaseFinances finances)
    {
        ArgumentNullException.ThrowIfNull(mapDefinition);
        ArgumentNullException.ThrowIfNull(buyerOwnedRailroadIndices);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(opponents);
        ArgumentNullException.ThrowIfNull(finances);

        var currentCoverage = NetworkCoverage.BuildSnapshot(mapDefinition, buyerOwnedRailroadIndices);
        var incomingFees = finances.EstimateIncomingFees(buyerOwnedRailroadIndices);
        var operatingReserve = finances.OperatingReserve;
        PurchaseRecommendationSet Result(IReadOnlyList<PurchaseRecommendation> recommendations) =>
            new(recommendations, finances.Cash, finances.FeesOwed, incomingFees, operatingReserve);

        if (candidates.Count == 0)
        {
            return Result([]);
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
            .Where(candidate => candidate.SpareCash >= 0)
            .ToList();

        if (scored.Count == 0)
        {
            return Result([]);
        }

        var recommendations = new List<PurchaseRecommendation>();
        void Add(PurchaseRecommendationCategory category, ScoredCandidate? pick, string reason)
        {
            if (pick is not null)
            {
                recommendations.Add(new PurchaseRecommendation(category, pick.Candidate.RailroadIndex, pick.Candidate.RailroadName, pick.Candidate.PurchasePrice, reason, pick.SpareCash));
            }
        }

        // Price alone never makes a railroad worth buying, so blended needs some gain.
        var blended = Best(scored, BlendedScores(scored), c => c.AccessGain > 0m || c.MonopolyGain > 0m || c.LeaderAccessGain > 0m);
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

        // Cheapest railroad that still adds access; the bigger gain breaks a price tie.
        var lowPrice = scored
            .Where(c => c.AccessGain > 0m)
            .OrderBy(c => c.Candidate.PurchasePrice)
            .ThenByDescending(c => c.AccessGain)
            .FirstOrDefault();
        Add(PurchaseRecommendationCategory.LowPrice, lowPrice, lowPrice is null ? string.Empty : $"+{FormatPercent(lowPrice.AccessGain)} access");

        var tollBooth = Best(scored, scored.ToDictionary(c => c, c => (decimal)c.TollPayers.Count), c => c.TollPayers.Count > 0);
        Add(PurchaseRecommendationCategory.TollBooth, tollBooth, tollBooth is null ? string.Empty : DescribeTollPayers(tollBooth.TollPayers));

        return Result(recommendations);
    }

    private static Dictionary<ScoredCandidate, decimal> BlendedScores(List<ScoredCandidate> scored)
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
            + (BlendedPriceWeight * (maxPrice > minPrice ? (decimal)(maxPrice - c.Candidate.PurchasePrice) / (maxPrice - minPrice) : 1m)));
    }

    /// <summary>Highest score among eligible candidates; the cheaper railroad breaks a tie.</summary>
    private static ScoredCandidate? Best(List<ScoredCandidate> scored, Dictionary<ScoredCandidate, decimal> scores, Func<ScoredCandidate, bool> eligible)
    {
        return scored
            .Where(eligible)
            .OrderByDescending(c => scores[c])
            .ThenBy(c => c.Candidate.PurchasePrice)
            .FirstOrDefault();
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
