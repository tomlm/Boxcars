using Boxcars.Data;
using Boxcars.Services;

namespace Boxcars.Engine.Tests.Unit;

public class GamePresenceServiceTests
{
    [Fact]
    public void PruneStaleConnections_ExpiredHeartbeat_MarksUserDisconnected()
    {
        var clock = new AdjustableTimeProvider(DateTimeOffset.UtcNow);
        var service = new GamePresenceService(clock, TimeSpan.FromSeconds(15), Timeout.InfiniteTimeSpan);

        service.AddConnection("game-1", "target", "connection-1");

        clock.Advance(TimeSpan.FromSeconds(16));
        var changedGames = service.PruneStaleConnections();

        Assert.Contains("game-1", changedGames);
        Assert.False(service.IsUserConnected("game-1", "target"));
    }

    [Fact]
    public void RefreshConnection_BeforeTimeout_KeepsUserConnected()
    {
        var clock = new AdjustableTimeProvider(DateTimeOffset.UtcNow);
        var service = new GamePresenceService(clock, TimeSpan.FromSeconds(15), Timeout.InfiniteTimeSpan);

        service.AddConnection("game-1", "target", "connection-1");
        clock.Advance(TimeSpan.FromSeconds(10));

        service.RefreshConnection("game-1", "target", "connection-1");

        clock.Advance(TimeSpan.FromSeconds(10));
        service.PruneStaleConnections();

        Assert.True(service.IsUserConnected("game-1", "target"));
    }

    [Fact]
    public void RemoveConnection_ControllerDisconnect_PreservesDelegatedControl()
    {
        var service = new GamePresenceService();

        service.AddConnection("game-1", "controller", "connection-1");
        service.SetMockConnectionState("game-1", "target", isConnected: false);

        var taken = service.TryTakeDelegatedControl("game-1", "target", "controller");
        var removed = service.RemoveConnection("game-1", "controller", "connection-1");

        Assert.True(taken);
        Assert.True(removed);
        Assert.Equal("controller", service.GetDelegatedControllerUserId("game-1", "target"));
    }

    [Fact]
    public void SetMockConnectionState_ControllerDisconnect_PreservesDelegatedControl()
    {
        var service = new GamePresenceService();

        service.SetMockConnectionState("game-1", "controller", isConnected: true);
        service.SetMockConnectionState("game-1", "target", isConnected: false);

        var taken = service.TryTakeDelegatedControl("game-1", "target", "controller");
        service.SetMockConnectionState("game-1", "controller", isConnected: false);

        Assert.True(taken);
        Assert.Equal("controller", service.GetDelegatedControllerUserId("game-1", "target"));
    }

    [Fact]
    public void SetMockConnectionState_TargetReconnect_ClearsDelegatedControl()
    {
        var service = new GamePresenceService();

        service.SetMockConnectionState("game-1", "controller", isConnected: true);
        service.SetMockConnectionState("game-1", "target", isConnected: false);
        service.TryTakeDelegatedControl("game-1", "target", "controller");

        service.SetMockConnectionState("game-1", "target", isConnected: true);

        Assert.Null(service.GetDelegatedControllerUserId("game-1", "target"));
    }

    [Fact]
    public void ResolveSeatControllerState_OfflineDelegatedSeat_ReturnsHumanDelegated()
    {
        var service = new GamePresenceService();

        service.SetMockConnectionState("game-1", "controller", isConnected: true);
        service.SetMockConnectionState("game-1", "target", isConnected: false);
        service.TryTakeDelegatedControl("game-1", "target", "controller");

        var controllerState = service.ResolveSeatControllerState("game-1", "target", activeBotAssignment: null);

        Assert.Equal(SeatControllerModes.HumanDelegated, controllerState.ControllerMode);
        Assert.Equal("controller", controllerState.DelegatedControllerUserId);
        Assert.False(controllerState.IsConnected);
    }

    [Fact]
    public void ResolveSeatControllerState_DisconnectedHumanWithoutManualController_DefaultsToAiGhost()
    {
        var service = new GamePresenceService();

        service.SetMockConnectionState("game-1", "target", isConnected: false);

        var controllerState = service.ResolveSeatControllerState("game-1", "target", activeBotAssignment: null);

        Assert.Equal(SeatControllerModes.AiGhost, controllerState.ControllerMode);
        Assert.Equal("target", controllerState.BotDefinitionId);
        Assert.False(controllerState.IsConnected);
    }

    [Fact]
    public void ResolveSeatControllerState_GhostAssignment_ReturnsAiGhost()
    {
        var service = new GamePresenceService();

        service.SetMockConnectionState("game-1", "controller", isConnected: true);
        service.SetMockConnectionState("game-1", "target", isConnected: false);
        service.TryTakeDelegatedControl("game-1", "target", "controller");

        var controllerState = service.ResolveSeatControllerState(
            "game-1",
            "target",
            new BotAssignment
            {
                GameId = "game-1",
                PlayerUserId = "target",
                ControllerUserId = "controller",
                ControllerMode = SeatControllerModes.AiGhost,
                BotDefinitionId = "bot-1",
                Status = BotAssignmentStatuses.Active
            });

        Assert.Equal(SeatControllerModes.AiGhost, controllerState.ControllerMode);
        Assert.Equal("controller", controllerState.DelegatedControllerUserId);
        Assert.Equal("bot-1", controllerState.BotDefinitionId);
    }

    [Fact]
    public void ResolveSeatControllerState_DedicatedBotAssignment_ReturnsAiBotSeat()
    {
        var service = new GamePresenceService();

        var controllerState = service.ResolveSeatControllerState(
            "game-1",
            "beatle-bot",
            new BotAssignment
            {
                GameId = "game-1",
                PlayerUserId = "beatle-bot",
                ControllerMode = SeatControllerModes.AiBotSeat,
                BotDefinitionId = "bot-1",
                Status = BotAssignmentStatuses.Active
            });

        Assert.Equal(SeatControllerModes.AiBotSeat, controllerState.ControllerMode);
        Assert.Equal("bot-1", controllerState.BotDefinitionId);
    }

    [Fact]
    public void ReleaseDelegatedControl_DisconnectedSeatFallsBackToAiGhost()
    {
        var service = new GamePresenceService();

        service.SetMockConnectionState("game-1", "controller", isConnected: true);
        service.SetMockConnectionState("game-1", "target", isConnected: false);
        service.TryTakeDelegatedControl("game-1", "target", "controller");
        service.ReleaseDelegatedControl("game-1", "target", "controller");

        var controllerState = service.ResolveSeatControllerState(
            "game-1",
            "target",
            new BotAssignment
            {
                GameId = "game-1",
                PlayerUserId = "target",
                ControllerUserId = "controller",
                ControllerMode = SeatControllerModes.AiGhost,
                BotDefinitionId = "bot-1",
                Status = BotAssignmentStatuses.Active
            });

        Assert.Equal(SeatControllerModes.AiGhost, controllerState.ControllerMode);
        Assert.Null(controllerState.DelegatedControllerUserId);
    }

    [Fact]
    public void Reconnect_GhostAssignmentFallsBackToHumanDirect()
    {
        var service = new GamePresenceService();

        service.SetMockConnectionState("game-1", "controller", isConnected: true);
        service.SetMockConnectionState("game-1", "target", isConnected: false);
        service.TryTakeDelegatedControl("game-1", "target", "controller");
        service.SetMockConnectionState("game-1", "target", isConnected: true);

        var controllerState = service.ResolveSeatControllerState(
            "game-1",
            "target",
            new BotAssignment
            {
                GameId = "game-1",
                PlayerUserId = "target",
                ControllerUserId = "controller",
                ControllerMode = SeatControllerModes.AiGhost,
                BotDefinitionId = "bot-1",
                Status = BotAssignmentStatuses.Active
            });

        Assert.Equal(SeatControllerModes.HumanDirect, controllerState.ControllerMode);
        Assert.True(controllerState.IsConnected);
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }

        public void Advance(TimeSpan amount)
        {
            _utcNow = _utcNow.Add(amount);
        }
    }
}