using Boxcars.Data;
using Boxcars.Engine.Domain;
using Boxcars.Engine.Persistence;
using Boxcars.Engine.Tests.Fixtures;
using Boxcars.Services;

namespace Boxcars.Engine.Tests.Unit;

public class GameEngineSettingsThresholdTests
{
    [Fact]
    public void ArrivalAtHome_BelowConfiguredWinningCash_DoesNotEndGame()
    {
        var settings = GameSettings.Default with { WinningCash = 325_000 };
        var (engine, _) = GameEngineFixture.CreateTestEngine(settings);
        var player = engine.CurrentTurn.ActivePlayer;
        var homeCity = player.HomeCity;
        var startCity = engine.MapDefinition.Cities.First(city => !string.Equals(city.Name, homeCity.Name, StringComparison.Ordinal));

        player.CurrentCity = startCity;
        player.CurrentNodeId = ResolveNodeId(startCity.Name);
        player.Destination = homeCity;
        player.TripOriginCity = startCity;
        player.ActiveRoute = engine.SuggestRoute();
        player.RouteProgressIndex = 0;
        var payout = ResolveTripPayoff(engine, startCity.Name, homeCity.Name);
        player.Cash = settings.WinningCash - payout - 1;

        engine.CurrentTurn.Phase = TurnPhase.Move;
        engine.CurrentTurn.MovementAllowance = player.ActiveRoute.NodeIds.Count - 1;
        engine.CurrentTurn.MovementRemaining = player.ActiveRoute.NodeIds.Count - 1;

        engine.MoveAlongRoute(player.ActiveRoute.NodeIds.Count - 1);

        Assert.Equal(GameStatus.InProgress, engine.GameStatus);
        Assert.Null(engine.Winner);
    }

    [Fact]
    public void ArrivalAtHome_AtConfiguredWinningCash_EndsGame()
    {
        var settings = GameSettings.Default with { WinningCash = 325_000 };
        var (engine, _) = GameEngineFixture.CreateTestEngine(settings);
        var player = engine.CurrentTurn.ActivePlayer;
        var homeCity = player.HomeCity;
        var startCity = engine.MapDefinition.Cities.First(city => !string.Equals(city.Name, homeCity.Name, StringComparison.Ordinal));

        player.CurrentCity = startCity;
        player.CurrentNodeId = ResolveNodeId(startCity.Name);
        player.Destination = homeCity;
        player.TripOriginCity = startCity;
        player.ActiveRoute = engine.SuggestRoute();
        player.RouteProgressIndex = 0;
        var payout = ResolveTripPayoff(engine, startCity.Name, homeCity.Name);
        player.Cash = settings.WinningCash - payout;

        engine.CurrentTurn.Phase = TurnPhase.Move;
        engine.CurrentTurn.MovementAllowance = player.ActiveRoute.NodeIds.Count - 1;
        engine.CurrentTurn.MovementRemaining = player.ActiveRoute.NodeIds.Count - 1;

        engine.MoveAlongRoute(player.ActiveRoute.NodeIds.Count - 1);

        Assert.Equal(GameStatus.Completed, engine.GameStatus);
        Assert.Same(player, engine.Winner);
    }

    [Fact]
    public void DeclaredArrivalAtHome_DoesNotAwardHomePayout()
    {
        var settings = GameSettings.Default with { WinningCash = 325_000 };
        var (engine, _) = GameEngineFixture.CreateTestEngine(settings);
        var player = engine.CurrentTurn.ActivePlayer;
        var homeCity = player.HomeCity;
        var startCity = engine.MapDefinition.Cities.First(city => !string.Equals(city.Name, homeCity.Name, StringComparison.Ordinal));
        var alternateDestination = engine.MapDefinition.Cities.First(city =>
            !string.Equals(city.Name, homeCity.Name, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(city.Name, startCity.Name, StringComparison.OrdinalIgnoreCase));

        player.CurrentCity = startCity;
        player.CurrentNodeId = ResolveNodeId(startCity.Name);
        player.HasDeclared = true;
        player.Destination = homeCity;
        player.AlternateDestination = alternateDestination;
        player.TripOriginCity = startCity;
        player.ActiveRoute = engine.SuggestRoute();
        player.RouteProgressIndex = 0;
        player.Cash = settings.WinningCash;
        var cashBeforeArrival = player.Cash;

        engine.CurrentTurn.Phase = TurnPhase.Move;
        engine.CurrentTurn.MovementAllowance = player.ActiveRoute.NodeIds.Count - 1;
        engine.CurrentTurn.MovementRemaining = player.ActiveRoute.NodeIds.Count - 1;

        engine.MoveAlongRoute(player.ActiveRoute.NodeIds.Count - 1);

        Assert.Equal(cashBeforeArrival, player.Cash);
        Assert.Equal(GameStatus.Completed, engine.GameStatus);
        Assert.Same(player, engine.Winner);
    }

    [Fact]
    public void DeclaredArrivalAtHome_DroppingBelowWinningCash_RoutesToAlternateAndPaysOriginalTripOnArrival()
    {
        var settings = GameSettings.Default with
        {
            WinningCash = 325_000,
            PublicFee = 1_000
        };
        var (engine, _) = GameEngineFixture.CreateTestEngine(settings);
        var player = engine.CurrentTurn.ActivePlayer;
        var homeCity = player.HomeCity;
        var startCity = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Miami", StringComparison.OrdinalIgnoreCase));
        var alternateDestination = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Atlanta", StringComparison.OrdinalIgnoreCase));

        player.CurrentCity = startCity;
        player.CurrentNodeId = ResolveNodeId(startCity.Name);
        player.HasDeclared = true;
        player.Destination = homeCity;
        player.AlternateDestination = alternateDestination;
        player.TripOriginCity = startCity;
        player.ActiveRoute = engine.SuggestRoute();
        player.RouteProgressIndex = 0;
        player.Cash = settings.WinningCash;

        engine.CurrentTurn.Phase = TurnPhase.Move;
        engine.CurrentTurn.MovementAllowance = player.ActiveRoute.NodeIds.Count - 1;
        engine.CurrentTurn.MovementRemaining = player.ActiveRoute.NodeIds.Count - 1;
        engine.CurrentTurn.RailroadsRiddenThisTurn.Add(0);

        engine.MoveAlongRoute(player.ActiveRoute.NodeIds.Count - 1);

        Assert.False(player.HasDeclared);
        Assert.Equal(TurnPhase.EndTurn, engine.CurrentTurn.Phase);
        Assert.NotNull(player.Destination);
        Assert.Equal("Atlanta", player.Destination!.Name);
        Assert.Equal(startCity.Name, player.TripOriginCity?.Name);
        Assert.Equal(settings.WinningCash - settings.PublicFee, player.Cash);

        var cashBeforeAlternateArrival = player.Cash;
        var expectedAlternatePayout = ResolveTripPayoff(engine, startCity.Name, alternateDestination.Name);

        player.ActiveRoute = engine.SuggestRoute();
        player.RouteProgressIndex = 0;
        engine.CurrentTurn.Phase = TurnPhase.Move;
        engine.CurrentTurn.MovementAllowance = player.ActiveRoute.NodeIds.Count - 1;
        engine.CurrentTurn.MovementRemaining = player.ActiveRoute.NodeIds.Count - 1;
        engine.CurrentTurn.RailroadsRiddenThisTurn.Clear();

        engine.MoveAlongRoute(player.ActiveRoute.NodeIds.Count - 1);

        Assert.Equal(cashBeforeAlternateArrival + expectedAlternatePayout, player.Cash);
        Assert.Null(player.TripOriginCity);
        Assert.Equal(GameStatus.InProgress, engine.GameStatus);
    }

    [Fact]
    public void Resolve_LegacyGame_UsesDefaultRoverCash()
    {
        var resolver = new GameSettingsResolver();
        var legacyGame = new GameEntity
        {
            PartitionKey = "game-legacy",
            RowKey = "GAME",
            GameId = "game-legacy",
            CreatorId = "creator@example.com",
            MapFileName = "U21MAP.RB3",
            MaxPlayers = 2,
            CurrentPlayerCount = 2,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var resolved = resolver.Resolve(legacyGame);

        Assert.Equal(GameSettings.Default.RoverCash, resolved.Settings.RoverCash);
        Assert.Equal("LegacyDefaulted", resolved.Source);
    }

    [Fact]
    public void Rover_UsesConfiguredCashTransferAndClearsDeclaredState()
    {
        var settings = GameSettings.Default with { RoverCash = 65_000 };
        var (engine, _) = GameEngineFixture.CreateTestEngine(settings);
        var movingPlayer = engine.CurrentTurn.ActivePlayer;
        var declaredPlayer = engine.Players[1];
        var alternateDestination = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Atlanta", StringComparison.Ordinal));
        var destination = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Boston", StringComparison.Ordinal));

        movingPlayer.CurrentCity = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, "Miami", StringComparison.Ordinal));
        movingPlayer.CurrentNodeId = ResolveNodeId(movingPlayer.CurrentCity.Name);
        movingPlayer.Destination = destination;
        movingPlayer.TripOriginCity = movingPlayer.CurrentCity;
        movingPlayer.ActiveRoute = new Route(
            ["1:0", "0:4", "0:3", "0:2", "0:1"],
            [
                new RouteSegment { FromNodeId = "1:0", ToNodeId = "0:4", RailroadIndex = 0 },
                new RouteSegment { FromNodeId = "0:4", ToNodeId = "0:3", RailroadIndex = 0 },
                new RouteSegment { FromNodeId = "0:3", ToNodeId = "0:2", RailroadIndex = 0 },
                new RouteSegment { FromNodeId = "0:2", ToNodeId = "0:1", RailroadIndex = 0 }
            ],
            totalCost: 0);
        movingPlayer.RouteProgressIndex = 0;
        movingPlayer.Cash = 20_000;

        declaredPlayer.HasDeclared = true;
        declaredPlayer.Cash = 300_000;
        declaredPlayer.CurrentNodeId = "0:4";
        declaredPlayer.AlternateDestination = alternateDestination;
        declaredPlayer.Destination = declaredPlayer.HomeCity;

        engine.CurrentTurn.Phase = TurnPhase.Move;
        engine.CurrentTurn.MovementAllowance = 1;
        engine.CurrentTurn.MovementRemaining = 1;

        engine.MoveAlongRoute(1);

        Assert.False(declaredPlayer.HasDeclared);
        Assert.True(movingPlayer.Cash > 20_000);
        Assert.Equal(235_000, declaredPlayer.Cash);
        Assert.NotNull(declaredPlayer.Destination);
        Assert.Equal("Atlanta", declaredPlayer.Destination!.Name);
        Assert.Null(declaredPlayer.AlternateDestination);
    }

    private static string ResolveNodeId(string cityName)
    {
        return cityName switch
        {
            "New York" => "0:0",
            "Boston" => "0:1",
            "Miami" => "1:0",
            "Atlanta" => "1:1",
            _ => throw new InvalidOperationException($"Unsupported city '{cityName}'.")
        };
    }

    private static int ResolveTripPayoff(global::Boxcars.Engine.Domain.GameEngine engine, string originCityName, string destinationCityName)
    {
        var origin = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, originCityName, StringComparison.Ordinal));
        var destination = engine.MapDefinition.Cities.First(city => string.Equals(city.Name, destinationCityName, StringComparison.Ordinal));
        Assert.True(origin.PayoutIndex.HasValue);
        Assert.True(destination.PayoutIndex.HasValue);
        Assert.True(engine.MapDefinition.TryGetPayout(origin.PayoutIndex.Value, destination.PayoutIndex.Value, out var payout));
        return payout;
    }
}
