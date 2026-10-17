using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Boxcars.Services;

/// <summary>
/// Files issues in the Boxcars GitHub repository. Needs <c>GitHub:IssueToken</c> (a token with Issues: write on the
/// repository); <c>GitHub:IssueRepository</c> overrides the repository (default tomlm/Boxcars).
/// </summary>
public sealed class GitHubIssueClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<GitHubIssueClient> logger)
{
    private const string DefaultRepository = "tomlm/Boxcars";

    /// <summary>GitHub rejects issue bodies longer than this.</summary>
    public const int MaximumBodyLength = 65_536;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["GitHub:IssueToken"]);

    /// <summary>Creates the issue and returns its web address, or null when it couldn't be filed.</summary>
    public async Task<CreatedIssue?> CreateIssueAsync(string title, string body, CancellationToken cancellationToken = default)
    {
        var token = configuration["GitHub:IssueToken"];
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var repository = configuration["GitHub:IssueRepository"] is { Length: > 0 } configured ? configured : DefaultRepository;

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

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
#pragma warning disable CA1848, CA1873 // Use the LoggerMessage delegates
                logger.LogWarning("Filing GitHub issue '{Title}' failed: {StatusCode}", title, response.StatusCode);
#pragma warning restore CA1848, CA1873
                return null;
            }

            using var created = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var number = created.RootElement.TryGetProperty("number", out var numberElement) ? numberElement.GetInt32() : 0;
            var url = created.RootElement.TryGetProperty("html_url", out var urlElement) ? urlElement.GetString() ?? string.Empty : string.Empty;
            return new CreatedIssue(number, url);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
#pragma warning disable CA1848, CA1873 // Use the LoggerMessage delegates
            logger.LogWarning(exception, "Filing GitHub issue '{Title}' failed.", title);
#pragma warning restore CA1848, CA1873
            return null;
        }
    }
}

public sealed record CreatedIssue(int Number, string Url);
