using Boxcars.Data;
using Boxcars.Engine.Tests;
using Boxcars.Engine.Tests.Fixtures;
using Boxcars.Services;
using Boxcars.Services.Maps;

namespace Boxcars.Engine.Tests.Unit;

public class GameSettingsVisibilityProjectionTests
{
    [Fact]
    public void PlayerBoardModel_SecretCashBelowThreshold_UsesConcealedDisplay()
    {
        var model = new PlayerBoardModel
        {
            Cash = 60_000,
            KeepCashSecret = true,
            AnnouncingCashThreshold = 100_000
        };

        Assert.False(model.CanViewExactCash);
        Assert.Equal("$$$", model.GetMoneyDisplay());
    }

    [Fact]
    public void PlayerBoardModel_SecretCashAtThreshold_ShowsExactAmount()
    {
        var model = new PlayerBoardModel
        {
            Cash = 100_000,
            KeepCashSecret = true,
            AnnouncingCashThreshold = 100_000
        };

        Assert.True(model.CanViewExactCash);
        Assert.Equal("$100,000", model.GetMoneyDisplay());
    }

    [Fact]
    public void PlayerBoardModel_SecretDisabled_ShowsExactAmount()
    {
        var model = new PlayerBoardModel
        {
            Cash = 45_000,
            KeepCashSecret = false,
            AnnouncingCashThreshold = 250_000
        };

        Assert.True(model.CanViewExactCash);
        Assert.Equal("$45,000", model.GetMoneyDisplay());
    }

    [Fact]
    public void PlayerBoardModel_DuringATrip_ShowsCashAfterThePayout()
    {
        var model = new PlayerBoardModel { Cash = 12_700, Payoff = 5_000, KeepCashSecret = false };

        Assert.Equal("$12,700 => $17,700", model.GetMoneyDisplay());
    }

    [Theory]
    [InlineData(true, false, true)]   // at the destination: the payout is already in the cash
    [InlineData(false, true, true)]   // declared and heading home: no payout for that trip
    [InlineData(false, false, false)] // cash kept secret below the announce threshold
    public void PlayerBoardModel_NoPayoutProjection_WhenItDoesNotApply(bool isAtDestination, bool hasDeclared, bool cashVisible)
    {
        var model = new PlayerBoardModel
        {
            Cash = 12_700,
            Payoff = 5_000,
            IsAtDestination = isAtDestination,
            HasDeclared = hasDeclared,
            KeepCashSecret = !cashVisible,
            AnnouncingCashThreshold = 100_000
        };

        Assert.DoesNotContain("=>", model.GetMoneyDisplay(), StringComparison.Ordinal);
    }

    [Fact]
    public void BuildTurnViewState_CustomFeeSettings_UsesConfiguredPreviewFee()
    {
        var mapper = new GameBoardStateMapper(
            new NetworkCoverageService(),
            new MapAnalysisService(new MapRouteService()),
            new PurchaseRecommendationService(),
            new GameSettingsResolver());
        var map = GameEngineFixture.CreateTestMap();
        var state = new global::Boxcars.Engine.Persistence.GameState
        {
            ActivePlayerIndex = 0,
            Players =
            [
                new global::Boxcars.Engine.Persistence.PlayerState
                {
                    Name = "Alice",
                    Cash = 20_000,
                    CurrentCityName = "New York",
                    HomeCityName = "New York",
                    SelectedRouteSegmentKeys = ["0:0|0:1|0"],
                    GrandfatheredRailroadIndices = []
                },
                new global::Boxcars.Engine.Persistence.PlayerState
                {
                    Name = "Bob",
                    Cash = 20_000,
                    CurrentCityName = "Miami",
                    HomeCityName = "Miami"
                }
            ],
            RailroadOwnership = new Dictionary<int, int?> { [0] = 1 },
            Turn = new global::Boxcars.Engine.Persistence.TurnState
            {
                Phase = "Move",
                MovementRemaining = 2,
                MovementAllowance = 2,
                RailroadsRiddenThisTurn = [],
                RailroadsRequiringFullOwnerRateThisTurn = [0]
            }
        };
        var gameEntity = GameSettingsTestData.CreatePersistedGameEntity(settings: GameSettingsTestData.Create(
            privateFee: 500,
            unfriendlyFee1: 6_000));

        var viewState = mapper.BuildTurnViewState("game-1", null, state, null, map, gameEntity: gameEntity);

        Assert.Equal(6_000, viewState.SelectedRoutePreview.FeeEstimate);
        Assert.True(viewState.SelectedRoutePreview.HasUnfriendlyFee);
    }
}
