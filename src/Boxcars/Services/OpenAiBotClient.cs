using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Boxcars.Data;
using Microsoft.Extensions.Options;

namespace Boxcars.Services;

public sealed class OpenAiBotClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BotOptions _botOptions;

    public OpenAiBotClient(IHttpClientFactory httpClientFactory, IOptions<BotOptions> botOptions)
    {
        _httpClientFactory = httpClientFactory;
        _botOptions = botOptions.Value;
    }

    public async Task<OpenAiBotDecisionResult> SelectOptionAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_botOptions.OpenAIKey))
        {
            return OpenAiBotDecisionResult.Failed("Missing OpenAI API key.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_botOptions.DecisionTimeoutSeconds));

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _botOptions.OpenAIKey);

        var payload = new
        {
            model = _botOptions.OpenAIModel,
            temperature = 0.1,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        };

        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

        try
        {
            var client = _httpClientFactory.CreateClient();
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
            var responseContent = await response.Content.ReadAsStringAsync(timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                return OpenAiBotDecisionResult.Failed($"OpenAI request failed with status {(int)response.StatusCode}.", responseContent);
            }

            var optionId = ParseSelectedOptionId(responseContent);
            return string.IsNullOrWhiteSpace(optionId)
                ? OpenAiBotDecisionResult.Failed("OpenAI response did not contain a selectedOptionId.", responseContent)
                : OpenAiBotDecisionResult.Success(optionId, responseContent);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return OpenAiBotDecisionResult.Timeout();
        }
        catch (HttpRequestException ex)
        {
            return OpenAiBotDecisionResult.Failed(ex.Message);
        }
    }

    private static string? ParseSelectedOptionId(string responseContent)
    {
        using var root = JsonDocument.Parse(responseContent);

        if (!root.RootElement.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            return null;
        }

        var message = choices[0].GetProperty("message");
        if (!message.TryGetProperty("content", out var contentElement))
        {
            return null;
        }

        var content = contentElement.GetString();
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        using var contentDocument = JsonDocument.Parse(content);
        return contentDocument.RootElement.TryGetProperty("selectedOptionId", out var selectedOptionId)
            ? selectedOptionId.GetString()
            : null;
    }
}

public sealed record OpenAiBotDecisionResult
{
    public bool Succeeded { get; init; }
    public bool TimedOut { get; init; }
    public string? SelectedOptionId { get; init; }
    public string? FailureReason { get; init; }
    public string? RawResponse { get; init; }

    public static OpenAiBotDecisionResult Success(string selectedOptionId, string rawResponse) => new()
    {
        Succeeded = true,
        SelectedOptionId = selectedOptionId,
        RawResponse = rawResponse
    };

    public static OpenAiBotDecisionResult Timeout() => new()
    {
        TimedOut = true,
        FailureReason = "OpenAI decision request timed out."
    };

    public static OpenAiBotDecisionResult Failed(string failureReason, string? rawResponse = null) => new()
    {
        FailureReason = failureReason,
        RawResponse = rawResponse
    };
}