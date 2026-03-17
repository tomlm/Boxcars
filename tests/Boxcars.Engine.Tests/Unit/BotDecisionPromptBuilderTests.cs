using Boxcars.Data;
using Boxcars.Services;

namespace Boxcars.Engine.Tests.Unit;

public class BotDecisionPromptBuilderTests
{
    [Fact]
    public void BuildSystemPrompt_DescribesTargetPlayerContract()
    {
        var builder = new BotDecisionPromptBuilder();

        var prompt = builder.BuildSystemPrompt(new BotDecisionContext
        {
            Phase = "Purchase"
        });

        Assert.Contains("TargetPlayer", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildUserPrompt_IncludesTargetPlayerAndPayload()
    {
        var builder = new BotDecisionPromptBuilder();
        var context = new BotDecisionContext
        {
            GameId = "game-1",
            PlayerUserId = "user-1",
            TargetPlayerName = "Alice",
            Phase = "Auction",
            TurnNumber = 12,
            BotName = "El Cheapo",
            StrategyText = "Preserve cash unless the railroad materially improves coverage.",
            GameStatePayload = "{\"TargetPlayer\":{\"PlayerName\":\"Alice\"},\"RailroadMarket\":{\"SoldRailroads\":[]}}",
            LegalOptions =
            [
                new BotLegalOption
                {
                    OptionId = "auction-pass",
                    OptionType = "Pass",
                    DisplayText = "Pass"
                }
            ]
        };

        var prompt = builder.BuildUserPrompt(context);

        Assert.Contains("TargetPlayer: Alice", prompt, StringComparison.Ordinal);
        Assert.Contains("\"TargetPlayer\"", prompt, StringComparison.Ordinal);
        Assert.Contains("auction-pass", prompt, StringComparison.Ordinal);
    }
}