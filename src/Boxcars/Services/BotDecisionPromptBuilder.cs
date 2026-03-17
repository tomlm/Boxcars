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

        return context.LegalOptions.FirstOrDefault(option =>
            string.Equals(option.OptionId, optionId.Trim(), StringComparison.Ordinal));
    }

    private static BotLegalOption SelectFallbackOption(BotDecisionContext context)
    {
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