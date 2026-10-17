using System.Net;
using Boxcars.Data.Maps;
using Boxcars.Engine.Persistence;
using global::Boxcars.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Boxcars.Engine.Tests.Unit;

public class RouteFailureIssueReporterTests
{
    [Fact]
    public void BuildIssue_DescribesTheFailureAndIncludesThePlannerInputs()
    {
        var (title, body) = RouteFailureIssueReporter.BuildIssue("game-1", CreateState(), CreateFailure(), mapDefinition: null);

        Assert.Equal("Route suggestion failed: thermous at 1:8 (Buffalo) to Kansas City", title);
        Assert.Contains("# Description of issue", body, StringComparison.Ordinal);
        Assert.Contains("# Expected or desired alternative", body, StringComparison.Ordinal);
        Assert.Contains("No valid route available to destination.", body, StringComparison.Ordinal);
        Assert.Contains("\"MovementRemaining\": 4", body, StringComparison.Ordinal);
        Assert.Contains("\"TrackRiddenThisTrip\"", body, StringComparison.Ordinal);
        // Owners are listed by player name.
        Assert.Contains("\"El Cheapo\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportAsync_WithoutAToken_FilesNothing()
    {
        var handler = new CountingHandler();
        var reporter = CreateReporter(handler, token: null);

        await reporter.ReportAsync("game-1", CreateState(), CreateFailure(), mapDefinition: null);

        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task ReportAsync_FilesOneIssuePerGameAndPosition()
    {
        var handler = new CountingHandler();
        var reporter = CreateReporter(handler, token: "test-token");

        await reporter.ReportAsync("game-1", CreateState(), CreateFailure(), mapDefinition: null);
        await reporter.ReportAsync("game-1", CreateState(), CreateFailure(), mapDefinition: null);

        Assert.Equal(1, handler.Requests);
        Assert.Equal("https://api.github.com/repos/tomlm/Boxcars/issues", handler.LastUri);
    }

    private static RouteFailureIssueReporter CreateReporter(CountingHandler handler, string? token)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["GitHub:IssueToken"] = token })
            .Build();
        var gitHubIssueClient = new GitHubIssueClient(new SingleClientFactory(handler), configuration, NullLogger<GitHubIssueClient>.Instance);
        return new RouteFailureIssueReporter(gitHubIssueClient, NullLogger<RouteFailureIssueReporter>.Instance);
    }

    private static GameState CreateState()
    {
        return new GameState
        {
            ActivePlayerIndex = 0,
            Players =
            [
                new PlayerState { Name = "thermous", LocomotiveType = "Superchief", CurrentCityName = "Buffalo", CurrentNodeId = "1:8", DestinationCityName = "Kansas City" },
                new PlayerState { Name = "El Cheapo" }
            ],
            RailroadOwnership = new Dictionary<int, int?> { [1] = 1, [5] = 0 },
            Turn = new TurnState { Phase = "Move", MovementRemaining = 4 }
        };
    }

    private static RouteSuggestionResult CreateFailure()
    {
        return new RouteSuggestionResult
        {
            Status = RouteSuggestionStatus.NoRoute,
            Message = "No valid route available to destination.",
            StartNodeId = "1:8",
            DestinationNodeId = "5:20"
        };
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }

        public string? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            LastUri = request.RequestUri?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("{\"number\": 42, \"html_url\": \"https://github.com/tomlm/Boxcars/issues/42\"}")
            });
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
