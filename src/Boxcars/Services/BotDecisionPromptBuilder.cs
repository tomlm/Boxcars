using System.Text;
using Boxcars.Data;

namespace Boxcars.Services;

public sealed class BotDecisionPromptBuilder
{
    public string BuildSystemPrompt(BotDecisionContext context)
    {
        return string.Join(' ',
            "You are selecting exactly one legal action for a disconnected Rail Baron player.",
            "The player you are controlling is identified as TargetPlayer in the provided game-state JSON.",
            "Return JSON only with the shape {\"selectedOptionId\":\"...\"}.",
            "Never invent rules, options, or state.",
            "In Purchase, account for fees already incurred this turn before spending cash.",
            "A buy that forces an immediate sale can be correct, but only when the resulting network position is meaningfully better than declining or choosing a cheaper option.",
            $"Current phase: {context.Phase}.");
    }

    public string BuildUserPrompt(BotDecisionContext context)
    {
        var builder = new StringBuilder();
        builder.Append("Bot name: ");
        builder.Append(context.BotName);
        builder.AppendLine();
        builder.Append("Turn number: ");
        builder.Append(context.TurnNumber);
        builder.AppendLine();
        builder.Append("Phase: ");
        builder.Append(context.Phase);
        builder.AppendLine();
        builder.Append("TargetPlayer: ");
        builder.Append(context.TargetPlayerName);
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("Strategy guidance:");
        builder.AppendLine(string.IsNullOrWhiteSpace(context.StrategyText) ? "(none)" : context.StrategyText);
        builder.AppendLine();
        builder.AppendLine("Authoritative game state:");
        builder.AppendLine(context.GameStatePayload);
        builder.AppendLine();
        builder.AppendLine("Legal options:");

        foreach (var option in context.LegalOptions)
        {
            builder.Append("- ");
            builder.Append(option.OptionId);
            builder.Append(": [");
            builder.Append(option.OptionType);
            builder.Append("] ");
            builder.Append(option.DisplayText);
            builder.AppendLine();
        }

        builder.AppendLine();
        builder.AppendLine("Choose one option id from the legal options and return JSON only.");
        return builder.ToString();
    }

    public BotDecisionResolution ResolveWithoutOpenAi(BotDecisionContext context, string fallbackReason)
    {
        if (context.LegalOptions.Count == 0)
        {
            throw new InvalidOperationException("Bot decision resolution requires at least one legal option.");
        }

        if (context.LegalOptions.Count == 1)
        {
            return new BotDecisionResolution
            {
                GameId = context.GameId,
                PlayerUserId = context.PlayerUserId,
                Phase = context.Phase,
                SelectedOptionId = context.LegalOptions[0].OptionId,
                Source = "OnlyLegalChoice",
                FallbackReason = fallbackReason
            };
        }

        var selectedOption = SelectFallbackOption(context);
        return new BotDecisionResolution
        {
            GameId = context.GameId,
            PlayerUserId = context.PlayerUserId,
            Phase = context.Phase,
            SelectedOptionId = selectedOption.OptionId,
            Source = "Fallback",
            FallbackReason = fallbackReason
        };
    }

    public BotLegalOption? FindOption(BotDecisionContext context, string? optionId)
    {
        if (string.IsNullOrWhiteSpace(optionId))
        {
            return null;
        }

        var normalizedOptionId = optionId.Trim();

        var exactMatch = context.LegalOptions.FirstOrDefault(option =>
            string.Equals(option.OptionId, normalizedOptionId, StringComparison.Ordinal));
        if (exactMatch is not null)
        {
            return exactMatch;
        }

        if (string.Equals(context.Phase, "Auction", StringComparison.OrdinalIgnoreCase))
        {
            return context.LegalOptions.FirstOrDefault(option =>
                string.Equals(option.OptionType, "Bid", StringComparison.OrdinalIgnoreCase)
                && IsAuctionBidOptionMatch(option, normalizedOptionId));
        }

        return null;
    }

    private static bool IsAuctionBidOptionMatch(BotLegalOption option, string returnedOptionId)
    {
        if (string.IsNullOrWhiteSpace(option.OptionId)
            || !returnedOptionId.StartsWith(option.OptionId, StringComparison.Ordinal))
        {
            return false;
        }

        if (returnedOptionId.Length == option.OptionId.Length)
        {
            return true;
        }

        if (returnedOptionId[option.OptionId.Length] != ':')
        {
            return false;
        }

        var returnedPayload = returnedOptionId[(option.OptionId.Length + 1)..].Trim();
        return string.IsNullOrWhiteSpace(returnedPayload)
            || string.Equals(returnedPayload, option.Payload, StringComparison.Ordinal)
            || int.TryParse(returnedPayload, out _);
    }

    private static BotLegalOption SelectFallbackOption(BotDecisionContext context)
    {
        if (string.Equals(context.Phase, "Auction", StringComparison.OrdinalIgnoreCase))
        {
            var bidOption = context.LegalOptions.FirstOrDefault(option =>
                string.Equals(option.OptionType, "Bid", StringComparison.OrdinalIgnoreCase));
            if (bidOption is not null)
            {
                return bidOption;
            }

            var auctionPassOption = context.LegalOptions.FirstOrDefault(option =>
                string.Equals(option.OptionType, "AuctionPass", StringComparison.OrdinalIgnoreCase)
                || string.Equals(option.OptionType, "Pass", StringComparison.OrdinalIgnoreCase));
            if (auctionPassOption is not null)
            {
                return auctionPassOption;
            }
        }

        var noActionTypes = new[] { "NoPurchase", "Pass", "AuctionPass" };
        var noAction = context.LegalOptions.FirstOrDefault(option =>
            noActionTypes.Contains(option.OptionType, StringComparer.OrdinalIgnoreCase));
        if (noAction is not null)
        {
            return noAction;
        }

        return context.LegalOptions
            .OrderBy(option => option.OptionType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(option => option.DisplayText, StringComparer.OrdinalIgnoreCase)
            .ThenBy(option => option.OptionId, StringComparer.Ordinal)
            .First();
    }
}