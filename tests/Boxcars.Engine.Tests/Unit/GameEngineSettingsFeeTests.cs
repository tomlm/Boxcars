using Boxcars.Engine.Domain;
using Boxcars.Engine.Persistence;
using Boxcars.Engine.Tests.Fixtures;

namespace Boxcars.Engine.Tests.Unit;

public class GameEngineSettingsFeeTests
{
    [Fact]
    public void DeclinePurchase_PublicFee_UsesConfiguredAmount()
    {
        var settings = GameSettings.Default with { PublicFee = 1_500 };
        var (engine, _) = GameEngineFixture.CreateTestEngine(settings);

        engine.CurrentTurn.Phase = TurnPhase.Purchase;
        engine.CurrentTurn.BonusRollAvailable = false;

        var player = engine.CurrentTurn.ActivePlayer;
        player.Cash = 10_000;
        engine.CurrentTurn.RailroadsRiddenThisTurn.Clear();
        engine.CurrentTurn.RailroadsRiddenThisTurn.Add(1);

        engine.DeclinePurchase();

        Assert.Equal(TurnPhase.EndTurn, engine.CurrentTurn.Phase);
        Assert.Equal(8_500, player.Cash);
    }

    [Fact]
    public void DeclinePurchase_PrivateFee_UsesConfiguredAmount()
    {
        var settings = GameSettings.Default with { PrivateFee = 500 };
        var (engine, _) = GameEngineFixture.CreateTestEngine(settings);

        engine.CurrentTurn.Phase = TurnPhase.Purchase;
        engine.CurrentTurn.BonusRollAvailable = false;

        var player = engine.CurrentTurn.ActivePlayer;
        var ownedRailroad = engine.Railroads.First(railroad => railroad.Index == 0);
        ownedRailroad.Owner = player;
        player.OwnedRailroads.Add(ownedRailroad);
        player.Cash = 10_000;
        engine.CurrentTurn.RailroadsRiddenThisTurn.Clear();
        engine.CurrentTurn.RailroadsRiddenThisTurn.Add(ownedRailroad.Index);

        engine.DeclinePurchase();

        Assert.Equal(TurnPhase.EndTurn, engine.CurrentTurn.Phase);
        Assert.Equal(9_500, player.Cash);
    }

    [Fact]
    public void DeclinePurchase_UnfriendlyFeeAfterSellout_UsesConfiguredAmount()
    {
        var settings = GameSettings.Default with { UnfriendlyFee1 = 12_000 };
        var (engine, _) = GameEngineFixture.CreateTestEngine(settings);

        engine.CurrentTurn.Phase = TurnPhase.Purchase;
        engine.CurrentTurn.BonusRollAvailable = false;

        var player = engine.CurrentTurn.ActivePlayer;
        var ownedRailroad = engine.Railroads.First(railroad => railroad.Index == 0);
        var opponentRailroad = engine.Railroads.First(railroad => railroad.Index == 1);
        var opponent = engine.Players[1];

        ownedRailroad.Owner = player;
        player.OwnedRailroads.Add(ownedRailroad);
        opponentRailroad.Owner = opponent;
        opponent.OwnedRailroads.Add(opponentRailroad);
        player.Cash = 20_000;
        opponent.Cash = 0;
        engine.CurrentTurn.RailroadsRiddenThisTurn.Clear();
        engine.CurrentTurn.RailroadsRiddenThisTurn.Add(opponentRailroad.Index);
        engine.CurrentTurn.RailroadsRequiringFullOwnerRateThisTurn.Add(opponentRailroad.Index);

        engine.DeclinePurchase();

        Assert.Equal(TurnPhase.EndTurn, engine.CurrentTurn.Phase);
        Assert.Equal(8_000, player.Cash);
        Assert.Equal(12_000, opponent.Cash);
    }

    [Fact]
    public void SuggestRoute_TotalCost_UsesConfiguredPublicPrivateAndUnfriendlyFees()
    {
        var settings = GameSettings.Default with
        {
            PublicFee = 1_500,
            PrivateFee = 500,
            UnfriendlyFee1 = 6_000
        };
        var (engine, _) = GameEngineFixture.CreateTestEngine(settings);
        var player = engine.CurrentTurn.ActivePlayer;
        var opponent = engine.Players[1];
        var ownedRailroad = engine.Railroads.First(railroad => railroad.Index == 0);
        var opponentRailroad = engine.Railroads.First(railroad => railroad.Index == 1);

        ownedRailroad.Owner = player;
        player.OwnedRailroads.Add(ownedRailroad);
        opponentRailroad.Owner = opponent;
        opponent.OwnedRailroads.Add(opponentRailroad);

        player.CurrentCity = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "New York", StringComparison.Ordinal));
        player.CurrentNodeId = "0:0";
        player.Destination = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Atlanta", StringComparison.Ordinal));
        player.TripOriginCity = player.CurrentCity;

        var route = engine.SuggestRoute();

        Assert.Equal(6_500, route.TotalCost);
    }
}
