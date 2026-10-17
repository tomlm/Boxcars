using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Boxcars.Engine.Persistence;
using Boxcars.GameEngine;

namespace Boxcars.Services;

/// <summary>
/// Files a bug a player reports from the top bar as a GitHub issue: what happened, what they expected, and the
/// current game state when they are in a game. The repository is public, so user ids (email addresses) are removed
/// from the state first.
/// </summary>
public sealed class BugReportService(GitHubIssueClient gitHubIssueClient, IGameEngine gameEngine)
{
    private const int MaximumTitleLength = 80;
    private const string RemovedValue = "[removed]";

    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    public bool IsConfigured => gitHubIssueClient.IsConfigured;

    public async Task<CreatedIssue?> SubmitAsync(
        string issue,
        string expected,
        string reporterName,
        string pagePath,
        string? gameId,
        CancellationToken cancellationToken = default)
    {
        var state = string.IsNullOrWhiteSpace(gameId)
            ? null
            : await gameEngine.GetCurrentStateAsync(gameId, cancellationToken);
        var (title, body) = BuildIssue(issue, expected, reporterName, pagePath, gameId, state, DateTimeOffset.UtcNow);
        return await gitHubIssueClient.CreateIssueAsync(title, body, cancellationToken);
    }

    public static (string Title, string Body) BuildIssue(
        string issue,
        string expected,
        string reporterName,
        string pagePath,
        string? gameId,
        GameState? state,
        DateTimeOffset reportedUtc)
    {
        var firstLine = issue.Trim().Split('\n', 2)[0].Trim();
        var title = firstLine.Length > MaximumTitleLength ? string.Concat(firstLine.AsSpan(0, MaximumTitleLength - 1), "…") : firstLine;

        var header = new StringBuilder()
            .AppendLine("# Issue")
            .AppendLine()
            .AppendLine(issue.Trim())
            .AppendLine()
            .AppendLine("# Expected")
            .AppendLine()
            .AppendLine(string.IsNullOrWhiteSpace(expected) ? "_Not given._" : expected.Trim())
            .AppendLine()
            .AppendLine("# Game State")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"Reported by {reporterName} on `/{pagePath}` at {reportedUtc:yyyy-MM-dd HH:mm:ss} UTC.")
            .AppendLine();

        var footer = Environment.NewLine + "_Filed from the Boxcars bug button._" + Environment.NewLine;
        if (state is null)
        {
            header.AppendLine(string.IsNullOrWhiteSpace(gameId) ? "Not in a game." : $"Game `{gameId}`: state unavailable.");
            return (title, header.Append(footer).ToString());
        }

        var stateJson = SerializeWithoutUserIds(state, IndentedJson);
        var stateBlock = string.Concat(
            "<details><summary>Game ", gameId, " state</summary>", Environment.NewLine, Environment.NewLine,
            "```json", Environment.NewLine, stateJson, Environment.NewLine, "```", Environment.NewLine, Environment.NewLine,
            "</details>", Environment.NewLine);

        if (header.Length + stateBlock.Length + footer.Length > GitHubIssueClient.MaximumBodyLength)
        {
            // Too big for an issue: the game id and time are enough to pull the state from the saved game events.
            header.AppendLine(CultureInfo.InvariantCulture, $"Game `{gameId}`: the state is too large to attach; it is in the saved game events at the time above.");
            return (title, header.Append(footer).ToString());
        }

        return (title, header.Append(stateBlock).Append(footer).ToString());
    }

    /// <summary>The state as JSON with every user id or email address replaced, so it can go in a public issue.</summary>
    public static string SerializeWithoutUserIds(GameState state, JsonSerializerOptions? options = null)
    {
        var node = JsonSerializer.SerializeToNode(state);
        RemoveUserIds(node);
        return node?.ToJsonString(options) ?? "null";
    }

    private static void RemoveUserIds(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                foreach (var property in jsonObject.ToList())
                {
                    if (property.Key.Contains("UserId", StringComparison.OrdinalIgnoreCase)
                        || property.Key.Contains("Email", StringComparison.OrdinalIgnoreCase))
                    {
                        jsonObject[property.Key] = RemovedValue;
                    }
                    else
                    {
                        RemoveUserIds(property.Value);
                    }
                }

                break;
            case JsonArray jsonArray:
                foreach (var item in jsonArray)
                {
                    RemoveUserIds(item);
                }

                break;
        }
    }
}
