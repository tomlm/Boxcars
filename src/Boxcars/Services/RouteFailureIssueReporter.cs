using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Boxcars.Data.Maps;
using Boxcars.Engine.Data.Maps;
using Boxcars.Engine.Persistence;

namespace Boxcars.Services;

/// <summary>
/// Files a GitHub issue when the route planner finds no route for a player, with the inputs needed to replay it.
/// Does nothing but log unless <c>GitHub:IssueToken</c> is configured. One issue per game and position, and at most
/// a few per hour, so a planner bug can't flood the repository.
/// </summary>
public sealed class RouteFailureIssueReporter(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<RouteFailureIssueReporter> logger)
{
    private const string DefaultRepository = "tomlm/Boxcars";
    private const int MaximumIssuesPerHour = 5;

    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    private readonly ConcurrentDictionary<string, byte> _reportedFailures = new(StringComparer.Ordinal);
    private readonly Queue<DateTimeOffset> _recentIssueTimes = new();
    private readonly Lock _rateLock = new();

    /// <summary>
    /// Reports the failure. The issue text is built before the first await, so the caller can fire and forget even
    /// though <paramref name="state"/> keeps changing.
    /// </summary>
    public async Task ReportAsync(string gameId, GameState state, RouteSuggestionResult suggestion, MapDefinition? mapDefinition)
    {
        var token = configuration["GitHub:IssueToken"];
        if (string.IsNullOrWhiteSpace(token))
        {
#pragma warning disable CA1848, CA1873 // Use the LoggerMessage delegates
            logger.LogInformation("Route failure in game {GameId} not filed as an issue: GitHub:IssueToken is not configured.", gameId);
#pragma warning restore CA1848, CA1873
            return;
        }

        var failureKey = $"{gameId}|{suggestion.StartNodeId}|{suggestion.DestinationNodeId}";
        if (!_reportedFailures.TryAdd(failureKey, 0) || !TryReserveIssueSlot())
        {
            return;
        }

        var repository = configuration["GitHub:IssueRepository"] is { Length: > 0 } configured ? configured : DefaultRepository;
        var (title, body) = BuildIssue(gameId, state, suggestion, mapDefinition);

        try
        {
            var client = httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.github.com/repos/{repository}/issues")
            {
                Content = JsonContent.Create(new { title, body })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.UserAgent.ParseAdd("Boxcars");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

            using var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
#pragma warning disable CA1848, CA1873 // Use the LoggerMessage delegates
                logger.LogWarning("Filing route failure issue for game {GameId} failed: {StatusCode}", gameId, response.StatusCode);
#pragma warning restore CA1848, CA1873
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
#pragma warning disable CA1848, CA1873 // Use the LoggerMessage delegates
            logger.LogWarning(exception, "Filing route failure issue for game {GameId} failed.", gameId);
#pragma warning restore CA1848, CA1873
        }
    }

    private bool TryReserveIssueSlot()
    {
        lock (_rateLock)
        {
            var now = DateTimeOffset.UtcNow;
            while (_recentIssueTimes.Count > 0 && now - _recentIssueTimes.Peek() > TimeSpan.FromHours(1))
            {
                _recentIssueTimes.Dequeue();
            }

            if (_recentIssueTimes.Count >= MaximumIssuesPerHour)
            {
                return false;
            }

            _recentIssueTimes.Enqueue(now);
            return true;
        }
    }

    /// <summary>The issue title and markdown body: what failed, and the planner inputs needed to replay it.</summary>
    public static (string Title, string Body) BuildIssue(string gameId, GameState state, RouteSuggestionResult suggestion, MapDefinition? mapDefinition)
    {
        var player = state.Players[state.ActivePlayerIndex];
        string RailroadName(int railroadIndex) =>
            mapDefinition?.Railroads.FirstOrDefault(railroad => railroad.Index == railroadIndex)?.ShortName
            ?? railroadIndex.ToString(CultureInfo.InvariantCulture);

        var plannerInputs = new
        {
            GameId = gameId,
            ReportedUtc = DateTimeOffset.UtcNow,
            Player = player.Name,
            PlayerIndex = state.ActivePlayerIndex,
            player.LocomotiveType,
            player.CurrentCityName,
            player.CurrentNodeId,
            player.DestinationCityName,
            suggestion.StartNodeId,
            suggestion.DestinationNodeId,
            state.Turn.Phase,
            state.Turn.MovementRemaining,
            state.Turn.DiceResult,
            state.Turn.BonusRollAvailable,
            RailroadsRiddenThisTurn = state.Turn.RailroadsRiddenThisTurn.Select(RailroadName).ToList(),
            TrackRiddenThisTrip = player.UsedSegments,
            state.AllRailroadsSold,
            RailroadsByOwner = state.RailroadOwnership
                .Where(entry => entry.Value is int ownerIndex && ownerIndex >= 0 && ownerIndex < state.Players.Count)
                .GroupBy(entry => state.Players[entry.Value!.Value].Name)
                .ToDictionary(group => group.Key, group => group.Select(entry => RailroadName(entry.Key)).OrderBy(name => name, StringComparer.Ordinal).ToList()),
            PlannerStatus = suggestion.Status.ToString(),
            PlannerMessage = suggestion.Message
        };

        var title = $"Route suggestion failed: {player.Name} at {player.CurrentNodeId} ({player.CurrentCityName}) to {player.DestinationCityName}";
        var body = new StringBuilder()
            .AppendLine("# Description of issue")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"The route planner found no route for **{player.Name}** from `{suggestion.StartNodeId}` (last city {player.CurrentCityName}) to **{player.DestinationCityName}** (`{suggestion.DestinationNodeId}`), so no route was suggested.")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"Planner: `{suggestion.Status}` {suggestion.Message}")
            .AppendLine()
            .AppendLine("# Expected or desired alternative")
            .AppendLine()
            .AppendLine("A fee-aware route suggestion.")
            .AppendLine()
            .AppendLine("# Planner inputs")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"Game `{gameId}`; the saved game events around this time hold the full state.")
            .AppendLine()
            .AppendLine("```json")
            .AppendLine(JsonSerializer.Serialize(plannerInputs, IndentedJson))
            .AppendLine("```")
            .AppendLine()
            .AppendLine("_Filed automatically by Boxcars._")
            .ToString();

        return (title, body);
    }
}
