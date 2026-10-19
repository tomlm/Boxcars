using Boxcars.Data;

namespace Boxcars.Engine.Tests.Unit;

public class PlayerNameTokensTests
{
    [Fact]
    public void Resolve_FillsInEachSeatsCurrentName()
    {
        var text = $"{PlayerNameTokens.For(0)} paid $5,000 to {PlayerNameTokens.For(1)}.";
        string[] names = ["B'umpus", "Pooper"];

        Assert.Equal("B'umpus paid $5,000 to Pooper.", PlayerNameTokens.Resolve(text, playerIndex => names[playerIndex]));
    }

    [Fact]
    public void Resolve_SeatWithNoName_IsCalledByNumber()
    {
        Assert.Equal("Player 3 moved.", PlayerNameTokens.Resolve($"{PlayerNameTokens.For(2)} moved.", _ => null));
    }

    [Fact]
    public void Resolve_TextWithoutPlaceholders_IsUnchanged()
    {
        Assert.Equal("The D&RGW was sold to the bank for $4,500", PlayerNameTokens.Resolve("The D&RGW was sold to the bank for $4,500", _ => "x"));
    }
}
