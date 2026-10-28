using Boxcars.Data;
using Boxcars.Engine.Persistence;

namespace Boxcars.Engine.Tests.Unit;

public class PlayerPaymentModelTests
{
    [Fact]
    public void Build_FeesPaid_ShowOnThePayeesCardScaledToTheLargestPayment()
    {
        var state = new GameState
        {
            Players =
            [
                new PlayerState { Name = "A", FeesPaidToPlayers = [new FeePaidToPlayerState { PlayerIndex = 1, Amount = 10_000 }] },
                new PlayerState { Name = "B", FeesPaidToPlayers = [new FeePaidToPlayerState { PlayerIndex = 0, Amount = 5_000 }] },
                new PlayerState { Name = "C", FeesPaidToPlayers = [new FeePaidToPlayerState { PlayerIndex = 1, Amount = 2_500 }] }
            ]
        };

        var payments = PlayerPaymentModel.Build(state, ["Ann", "Bob", "Cat"], ["red", "blue", "green"]);

        Assert.Equal([new PlayerPaymentModel(1, "Bob", "blue", 5_000, 50)], payments[0]);
        Assert.Equal(
            [new PlayerPaymentModel(0, "Ann", "red", 10_000, 100), new PlayerPaymentModel(2, "Cat", "green", 2_500, 25)],
            payments[1]);
        Assert.Empty(payments[2]);
    }

    [Fact]
    public void Build_NoFeesPaid_LeavesEveryCardWithoutBars()
    {
        var state = new GameState { Players = [new PlayerState(), new PlayerState()] };

        var payments = PlayerPaymentModel.Build(state, ["Ann", "Bob"], ["red", "blue"]);

        Assert.All(payments, Assert.Empty);
    }
}
