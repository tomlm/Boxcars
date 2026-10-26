using Boxcars.Data;
using Boxcars.Engine.Domain;
using Boxcars.Engine.Persistence;
using Boxcars.Engine.Tests.Fixtures;
using Boxcars.Services;
using Boxcars.Services.Maps;

namespace Boxcars.Engine.Tests.Unit;

public class RoverPreviewStateMapperTests
{
    [Fact]
    public void BuildTurnViewState_MoveThroughADeclaredOpponent_PreviewsTheRoverCash()
    {
        // Bob has declared on a dot the move passes through; Carl has declared beyond where it ends; Dee hasn't declared.
        var state = new GameState
        {
            ActivePlayerIndex = 0,
            Players =
            [
                CreatePlayer("Alice", "0:0", declared: false),
                CreatePlayer("Bob", "0:2", declared: true),
                CreatePlayer("Carl", "0:9", declared: true),
                CreatePlayer("Dee", "0:3", declared: false)
            ],
            RailroadOwnership = new Dictionary<int, int?>(),
            Turn = new TurnState { Phase = TurnPhase.Move.ToString(), MovementAllowance = 3, MovementRemaining = 3 }
        };
        var preview = new TurnMovementPreview { NodeIds = ["0:0", "0:1", "0:2", "0:3"] };
        var mapper = new GameBoardStateMapper(
            new NetworkCoverageService(),
            new MapAnalysisService(new MapRouteService()),
            new PurchaseRecommendationService());

        var viewState = mapper.BuildTurnViewState("game-1", null, state, null, GameEngineFixture.CreateTestMap(), preview);

        Assert.Equal(GameSettings.Default.RoverCash, viewState.PreviewRoverCash);
    }

    private static PlayerState CreatePlayer(string name, string nodeId, bool declared) => new()
    {
        Name = name,
        Cash = 260_000,
        CurrentNodeId = nodeId,
        HasDeclared = declared,
        LocomotiveType = LocomotiveType.Freight.ToString(),
        IsActive = true
    };
}
