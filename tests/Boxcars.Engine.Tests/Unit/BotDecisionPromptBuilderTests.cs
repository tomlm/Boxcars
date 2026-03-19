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
        Assert.Contains("fees already incurred this turn", prompt, StringComparison.Ordinal);
        Assert.Contains("forces an immediate sale", prompt, StringComparison.Ordinal);
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

    [Fact]
    public void FindOption_AuctionBidWithReturnedAmountSuffix_ResolvesBidOption()
    {
        var builder = new BotDecisionPromptBuilder();
        var context = new BotDecisionContext
        {
            Phase = "Auction",
            LegalOptions =
            [
                new BotLegalOption
                {
                    OptionId = "auction-bid:min",
                    OptionType = "Bid",
                    DisplayText = "Bid 4250",
                    Payload = "4250"
                },
                new BotLegalOption
                {
                    OptionId = "auction-pass",
                    OptionType = "AuctionPass",
                    DisplayText = "Pass"
                }
            ]
        };

        var option = builder.FindOption(context, "auction-bid:min:4250");

        Assert.NotNull(option);
        Assert.Equal("auction-bid:min", option!.OptionId);
    }
}