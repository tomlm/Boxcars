using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Domain;
using Boxcars.Engine.Persistence;
using global::Boxcars.Data;
using global::Boxcars.Services;

namespace Boxcars.Engine.Tests.Unit;

public class PurchaseRecommendationServiceTests
{
    // Railroad indices on the test map.
    private const int OwnedWest = 1, OwnedEast = 2, Bend = 3, Bridge = 4, BigCity = 5, Cheap = 6, LeaderLine = 7, FarCity = 8;

    [Fact]
    public void BuildRecommendations_PicksTheBestRailroadInEachCategory()
    {
        var recommendations = Build();
        int Pick(PurchaseRecommendationCategory category) => recommendations.Single(r => r.Category == category).RailroadIndex;

        // The only line into the 40% city adds the most access and the most monopoly.
        Assert.Equal(BigCity, Pick(PurchaseRecommendationCategory.Access));
        Assert.Equal(BigCity, Pick(PurchaseRecommendationCategory.Monopoly));
        // The bridge joins the buyer's two separate lines, even though it reaches no new city.
        Assert.Equal(Bridge, Pick(PurchaseRecommendationCategory.Connectivity));
        Assert.Equal("Joins 2 of your networks", recommendations.Single(r => r.Category == PurchaseRecommendationCategory.Connectivity).Reason);
        // The cheapest railroad that still adds access.
        Assert.Equal(Cheap, Pick(PurchaseRecommendationCategory.LowPrice));
        // The rich opponent would gain the most from the big-city line.
        Assert.Equal(BigCity, Pick(PurchaseRecommendationCategory.Strategy));
        Assert.StartsWith("Denies Leader", recommendations.Single(r => r.Category == PurchaseRecommendationCategory.Strategy).Reason, StringComparison.Ordinal);
        Assert.Contains(recommendations, r => r.Category == PurchaseRecommendationCategory.Blended);
    }

    [Fact]
    public void BuildRecommendations_OpponentHeadedWhereOnlyACandidateGoes_RecommendsTheTollBooth()
    {
        // Small is served only by the Cheap line; owning it makes Laggard pay the buyer to get there.
        var recommendations = BuildSet(
            new PurchaseFinances(200_000, 0, 0, _ => 0),
            opponents: [new("Leader", 200_000, [LeaderLine], "Big"), new("Laggard", 10_000, [], "Small")]).Recommendations;

        var tollBooth = recommendations.Single(r => r.Category == PurchaseRecommendationCategory.TollBooth);
        Assert.Equal(Cheap, tollBooth.RailroadIndex);
        // Buying the Big City line would trap the leader the same way; the cheaper railroad wins the tie.
        Assert.Equal("Laggard must pay you to reach Small", tollBooth.Reason);
    }

    [Fact]
    public void BuildRecommendations_DestinationStillReachableOnAnotherRailroad_NoTollBooth()
    {
        // Far is also on the leader's own line, so buying the Far City line doesn't force anyone to pay.
        var recommendations = BuildSet(
            new PurchaseFinances(200_000, 0, 0, _ => 0),
            opponents: [new("Leader", 200_000, [LeaderLine], "Far"), new("Laggard", 10_000, [], "Home")]).Recommendations;

        // Home is served only by a railroad the buyer already owns, so there's nothing to buy for it either.
        Assert.DoesNotContain(recommendations, r => r.Category == PurchaseRecommendationCategory.TollBooth);
    }

    [Fact]
    public void BuildRecommendations_LeavesOutCategoriesNoCandidateHelps()
    {
        // Only the bridge is affordable: it connects the buyer's lines but adds no access, monopoly or leader gain.
        var recommendations = Build(candidateIndices: [Bridge]);

        Assert.Equal([PurchaseRecommendationCategory.Connectivity], recommendations.Select(r => r.Category).ToArray());
    }

    [Fact]
    public void ResolveWithoutOpenAi_PrefersTheFlaggedPurchaseOverBuyingNothing()
    {
        var builder = new BotDecisionPromptBuilder();
        var context = new BotDecisionContext
        {
            GameId = "game-1",
            PlayerUserId = "bot",
            Phase = "Purchase",
            LegalOptions =
            [
                new BotLegalOption { OptionId = "buy-railroad:5", OptionType = "PurchaseRailroad", IsPreferredFallback = true },
                new BotLegalOption { OptionId = "buy-railroad:6", OptionType = "PurchaseRailroad" },
                new BotLegalOption { OptionId = "decline-purchase", OptionType = "NoPurchase" }
            ]
        };

        var resolution = builder.ResolveWithoutOpenAi(context, "OpenAI request timed out.");

        Assert.Equal("buy-railroad:5", resolution.SelectedOptionId);
        Assert.Equal("Fallback", resolution.Source);
    }

    [Fact]
    public void BuildRecommendations_SkipsRailroadsThatWouldBreakTheOperatingReserve()
    {
        // $35,000 can't cover the $30,000 big-city line and a $10,000 reserve.
        var result = BuildSet(new PurchaseFinances(35_000, 0, 10_000, _ => 0));

        Assert.Equal(10_000, result.OperatingReserve);
        Assert.DoesNotContain(result.Recommendations, r => r.RailroadIndex == BigCity);
        var access = result.Recommendations.Single(r => r.Category == PurchaseRecommendationCategory.Access);
        Assert.Equal(FarCity, access.RailroadIndex);
        Assert.Equal(35_000 - 12_000 - 10_000, access.SpareCash);
    }

    [Fact]
    public void BuildRecommendations_FeesOwedReduceWhatCanBeSpent()
    {
        var withoutFees = BuildSet(new PurchaseFinances(45_000, 0, 10_000, _ => 0));
        var withFees = BuildSet(new PurchaseFinances(45_000, 10_000, 10_000, _ => 0));

        Assert.Contains(withoutFees.Recommendations, r => r.RailroadIndex == BigCity);
        Assert.DoesNotContain(withFees.Recommendations, r => r.RailroadIndex == BigCity);
        Assert.Equal(10_000, withFees.FeesOwed);
    }

    [Fact]
    public void BuildRecommendations_IncomingFeesCountTowardWhatCanBeSpent()
    {
        // An opponent is about to ride the big-city line, so owning it brings in a $5,000 fee.
        var result = BuildSet(new PurchaseFinances(35_000, 0, 10_000, owned => owned.Contains(BigCity) ? 5_000 : 0));

        var access = result.Recommendations.Single(r => r.Category == PurchaseRecommendationCategory.Access);
        Assert.Equal(BigCity, access.RailroadIndex);
        Assert.Equal(0, access.SpareCash);
    }

    [Fact]
    public void BuildRecommendations_NothingFits_ReportsTheBudgetWithoutRecommendations()
    {
        var result = BuildSet(new PurchaseFinances(5_000, 1_000, 10_000, _ => 0));

        Assert.Empty(result.Recommendations);
        Assert.Equal(5_000, result.Cash);
        Assert.Equal(1_000, result.FeesOwed);
        Assert.Equal(10_000, result.OperatingReserve);
    }

    [Fact]
    public void BuildRecommendations_IncomingFeesStandInForTheReserveButNotForFeesOwed()
    {
        // $20,000 coming in covers the whole $10,000 reserve, but the $10,000 owed now must still be paid from cash.
        var result = BuildSet(new PurchaseFinances(40_000, 10_000, 10_000, _ => 20_000));

        Assert.Equal(0, result.Recommendations.First(r => r.RailroadIndex == BigCity).SpareCash);
    }

    [Fact]
    public void EstimateIncomingFees_WeightsPlannedRoutesByHowSoonTheyRideTheBuyersRailroads()
    {
        var state = new GameState
        {
            Players =
            [
                new PlayerState { Name = "Buyer", LocomotiveType = "Freight" },
                // Rides railroad 7 within the next 7 dots: next turn.
                CreateTraveler("Near", [1, 1, 7, 1], routeProgressIndex: 1),
                // Railroad 7 is 9 dots ahead: the turn after.
                CreateTraveler("Far", [1, 1, 1, 1, 1, 1, 1, 1, 1, 7], routeProgressIndex: 0),
                // Railroad 7 is 15 dots ahead, past both turns.
                CreateTraveler("Beyond", [.. Enumerable.Repeat(1, 15), 7], routeProgressIndex: 0)
            ]
        };

        Assert.Equal(3_750 + 2_500, OperatingCapitalService.EstimateIncomingFees(null, state, 0, [7], GameSettings.Default));
        Assert.Equal(0, OperatingCapitalService.EstimateIncomingFees(null, state, 0, [2], GameSettings.Default));
    }

    [Theory]
    // Every railroad at Start is the buyer's, so Bob must pay a full fee to leave.
    [InlineData(new[] { 1, 2 }, 5_000)]
    // RR2 lets Bob leave without paying.
    [InlineData(new[] { 1 }, 0)]
    public void EstimateIncomingFees_AnOpponentWhoCanOnlyLeaveOnTheBuyersRailroadsPaysInFull(int[] ownedRailroadIndices, int expected)
    {
        var state = CreateReserveState(allRailroadsSold: false, []);
        state.Players[1].CurrentNodeId = "0:0";

        Assert.Equal(expected, OperatingCapitalService.EstimateIncomingFees(CreateReserveMap(), state, 0, ownedRailroadIndices, GameSettings.Default));
    }

    [Fact]
    public void EstimateIncomingFees_CountsAGrandfatheredOpponentAtTheirGrandfatheredRate()
    {
        var state = CreateReserveState(allRailroadsSold: false, []);
        state.Players[1].CurrentNodeId = "0:0";
        state.Players[1].GrandfatheredRailroadIndices = [1];
        state.Players[1].GrandfatheredRailroadFees = [new GrandfatheredRailroadFeeState { RailroadIndex = 1, FeeAmount = 1_000 }];

        Assert.Equal(1_000, OperatingCapitalService.EstimateIncomingFees(CreateReserveMap(), state, 0, [1, 2], GameSettings.Default));
    }

    [Fact]
    public void CalculateFeesOwed_ChargesPublicAndOpponentFeesForRailroadsRiddenThisTurn()
    {
        var state = new GameState
        {
            Players = [new PlayerState { Name = "Buyer" }, new PlayerState { Name = "Owner" }],
            RailroadOwnership = new Dictionary<int, int?> { [1] = null, [2] = 1 },
            Turn = new TurnState { RailroadsRiddenThisTurn = [1, 2], RailroadsRequiringFullOwnerRateThisTurn = [2] }
        };

        Assert.Equal(1_000 + 5_000, OperatingCapitalService.CalculateFeesOwed(state, 0, GameSettings.Default));
    }

    [Theory]
    // Bob owns RR1; RR2 is unowned, so Start is friendly: one opponent fee.
    [InlineData(new[] { 1 }, false, 5_000, true)]
    [InlineData(new[] { 1 }, true, 10_000, true)]
    // Bob owns both railroads at Start: two opponent fees.
    [InlineData(new[] { 1, 2 }, false, 10_000, false)]
    [InlineData(new[] { 1, 2 }, true, 20_000, false)]
    public void CalculateOperatingReserve_KeepsOneOpponentFeeAtAFriendlyCityAndTwoAtAnUnfriendlyOne(
        int[] railroadsOwnedByOpponent, bool allRailroadsSold, int expectedReserve, bool expectedFriendly)
    {
        var state = CreateReserveState(allRailroadsSold, railroadsOwnedByOpponent.ToDictionary(index => index, _ => (int?)1));

        var reserve = OperatingCapitalService.CalculateOperatingReserve(CreateReserveMap(), state, 0, GameSettings.Default);

        Assert.Equal(expectedReserve, reserve.Amount);
        Assert.Equal(expectedFriendly, reserve.AtFriendlyCity);
    }

    [Fact]
    public void CalculateOperatingReserve_TheBuyersOwnRailroadMakesTheCityFriendly()
    {
        var state = CreateReserveState(allRailroadsSold: true, new Dictionary<int, int?> { [1] = 1, [2] = 0 });

        var reserve = OperatingCapitalService.CalculateOperatingReserve(CreateReserveMap(), state, 0, GameSettings.Default);

        Assert.True(reserve.AtFriendlyCity);
        Assert.Equal(10_000, reserve.Amount);
    }

    private static GameState CreateReserveState(bool allRailroadsSold, Dictionary<int, int?> ownership)
    {
        return new GameState
        {
            Players = [new PlayerState { Name = "Buyer", CurrentCityName = "Start" }, new PlayerState { Name = "Bob" }],
            RailroadOwnership = ownership,
            AllRailroadsSold = allRailroadsSold
        };
    }

    private static MapDefinition CreateReserveMap()
    {
        // Start (dot 0) is served by RR1 and RR2; RR3 runs elsewhere.
        var map = new MapDefinition { Name = "Reserve Test Map", Version = "1.0" };
        map.Regions.Add(new RegionDefinition { Index = 0, Name = "Region", Code = "RG", Probability = 100d });
        map.Cities.Add(new CityDefinition { Name = "Start", RegionCode = "RG", Probability = 100d, PayoutIndex = 0, MapDotIndex = 0 });

        foreach (var (railroad, start, end) in new[] { (1, 0, 1), (2, 2, 0), (3, 1, 2) })
        {
            map.Railroads.Add(new RailroadDefinition { Index = railroad, Name = $"Line {railroad}", ShortName = $"L{railroad}" });
            map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition
            {
                RailroadIndex = railroad,
                StartRegionIndex = 0,
                StartDotIndex = start,
                EndRegionIndex = 0,
                EndDotIndex = end
            });
        }

        return map;
    }

    private static PlayerState CreateTraveler(string name, int[] railroadIndices, int routeProgressIndex)
    {
        return new PlayerState
        {
            Name = name,
            LocomotiveType = "Freight",
            RouteProgressIndex = routeProgressIndex,
            ActiveRoute = new RouteState
            {
                Segments = railroadIndices.Select((railroad, index) => new RouteSegmentState { FromNodeId = $"0:{index}", ToNodeId = $"0:{index + 1}", RailroadIndex = railroad }).ToList()
            }
        };
    }

    private static IReadOnlyList<PurchaseRecommendation> Build(int[]? candidateIndices = null)
    {
        return BuildSet(new PurchaseFinances(200_000, 0, 0, _ => 0), candidateIndices).Recommendations;
    }

    private static PurchaseRecommendationSet BuildSet(PurchaseFinances finances, int[]? candidateIndices = null, List<RecommendationOpponent>? opponents = null)
    {
        var map = CreateMap();
        var prices = new Dictionary<int, int> { [Bend] = 10_000, [Bridge] = 8_000, [BigCity] = 30_000, [Cheap] = 2_000, [FarCity] = 12_000 };
        var candidates = (candidateIndices ?? prices.Keys.ToArray())
            .Select(index => new PurchaseCandidate(index, map.Railroads.Single(r => r.Index == index).Name, prices[index]))
            .ToList();
        opponents ??=
        [
            new("Leader", 200_000, [LeaderLine]),
            new("Laggard", 10_000, [])
        ];

        return PurchaseRecommendationService.BuildRecommendations(map, [OwnedWest, OwnedEast], candidates, opponents, finances);
    }

    private static MapDefinition CreateMap()
    {
        var map = new MapDefinition { Name = "Recommendation Test Map", Version = "1.0" };
        map.Regions.Add(new RegionDefinition { Index = 0, Name = "Region", Code = "RG", Probability = 100d });

        foreach (var (name, dot, probability) in new[] { ("Home", 0, 10d), ("Bend", 2, 10d), ("East", 6, 10d), ("Big", 8, 40d), ("Small", 9, 5d), ("Far", 10, 25d) })
        {
            map.Cities.Add(new CityDefinition { Name = name, RegionCode = "RG", Probability = probability, PayoutIndex = dot, MapDotIndex = dot });
        }

        foreach (var (index, name) in new[] { (OwnedWest, "Owned West"), (OwnedEast, "Owned East"), (Bend, "Bend Line"), (Bridge, "Bridge Line"), (BigCity, "Big City Line"), (Cheap, "Cheap Line"), (LeaderLine, "Leader Line"), (FarCity, "Far City Line") })
        {
            map.Railroads.Add(new RailroadDefinition { Index = index, Name = name, ShortName = name[..2] });
        }

        for (var dot = 0; dot <= 14; dot++)
        {
            map.TrainDots.Add(new TrainDot { Id = $"0:{dot}", RegionIndex = 0, DotIndex = dot, X = dot * 10, Y = 0 });
        }

        foreach (var (railroad, start, end) in new[]
                 {
                     (OwnedWest, 0, 1), (OwnedEast, 5, 6),   // the buyer's two separate lines
                     (Bend, 1, 2),                           // extends the west line to Bend
                     (Bridge, 1, 5),                         // joins the two owned lines
                     (BigCity, 8, 11),                       // only line into Big (40%)
                     (Cheap, 9, 12),                         // only line into Small (5%)
                     (LeaderLine, 10, 13), (FarCity, 10, 14) // Far is shared with the leader's line
                 })
        {
            map.RailroadRouteSegments.Add(new RailroadRouteSegmentDefinition
            {
                RailroadIndex = railroad,
                StartRegionIndex = 0,
                StartDotIndex = start,
                EndRegionIndex = 0,
                EndDotIndex = end
            });
        }

        return map;
    }
}
