using Boxcars.Engine.Domain;
using Boxcars.Engine.Persistence;
using Boxcars.Engine.Tests.Fixtures;
using Boxcars.Services;
using Boxcars.Services.Maps;

namespace Boxcars.Engine.Tests.Unit;

public class PurchaseStateMapperTests
{
    [Fact]
    public void BuildTurnViewState_PurchasePhase_UsesCashAfterPayoutForAffordability()
    {
        var map = GameEngineFixture.CreateTestMap();
        var cheapestRailroad = map.Railroads
            .OrderBy(railroad => railroad.PurchasePrice ?? global::Boxcars.Engine.Domain.GameEngine.GetRailroadPurchasePrice(railroad.Index))
            .First();
        var railroadPrice = cheapestRailroad.PurchasePrice ?? global::Boxcars.Engine.Domain.GameEngine.GetRailroadPurchasePrice(cheapestRailroad.Index);
        var cashBeforePayout = Math.Max(0, railroadPrice - 500);
        var cashAfterPayout = railroadPrice + 500;

        var state = new GameState
        {
            ActivePlayerIndex = 0,
            Players =
            [
                new PlayerState
                {
                    Name = "Alice",
                    Cash = cashBeforePayout,
                    CurrentCityName = "New York",
                    TripStartCityName = "New York",
                    DestinationCityName = "Miami",
                    LocomotiveType = LocomotiveType.Freight.ToString(),
                    IsActive = true
                },
                new PlayerState
                {
                    Name = "Bob",
                    Cash = 20_000,
                    CurrentCityName = "Miami",
                    IsActive = true
                }
            ],
            RailroadOwnership = new Dictionary<int, int?>(),
            Turn = new TurnState
            {
                Phase = TurnPhase.Purchase.ToString(),
                ArrivalResolution = new ArrivalResolutionState
                {
                    PlayerIndex = 0,
                    DestinationCityName = "Miami",
                    PayoutAmount = cashAfterPayout - cashBeforePayout,
                    CashAfterPayout = cashAfterPayout,
                    PurchaseOpportunityAvailable = true,
                    Message = "Arrival purchase available."
                }
            }
        };

        var mapper = new GameBoardStateMapper(
            new NetworkCoverageService(),
            new MapAnalysisService(new MapRouteService()),
            new PurchaseRecommendationService());

        var viewState = mapper.BuildTurnViewState("game-1", null, state, null, map);

        var purchasePhase = viewState.PurchasePhase;
        Assert.NotNull(purchasePhase);
        Assert.Equal(cashAfterPayout, purchasePhase.CashAvailable);
        Assert.Equal(cashAfterPayout, purchasePhase.CashAfterPayout);

        var railroadOption = Assert.Single(purchasePhase.RailroadOptions.Where(option => option.RailroadIndex == cheapestRailroad.Index));
        Assert.True(railroadOption.IsAffordable);

        Assert.Contains(cheapestRailroad.Index, purchasePhase.RecommendationInputs!.AffordableRailroadIndices);

        var taskbarOption = Assert.Single(purchasePhase.TaskbarOptions.Where(option => option.OptionKey == $"railroad:{cheapestRailroad.Index}"));
        Assert.True(taskbarOption.IsAffordable);

        var expressUpgrade = Assert.Single(purchasePhase.EngineOptions.Where(option => option.EngineType == LocomotiveType.Express));
        Assert.True(expressUpgrade.IsEligible);
        Assert.True(purchasePhase.HasActivePurchaseControls);
    }
}
