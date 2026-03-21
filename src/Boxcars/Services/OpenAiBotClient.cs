using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Boxcars.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Boxcars.Services;

public sealed class OpenAiBotClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string ChatCompletionsEndpoint = "https://api.openai.com/v1/chat/completions";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BotOptions _botOptions;
    private readonly ILogger<OpenAiBotClient> _logger;

    public OpenAiBotClient(IHttpClientFactory httpClientFactory, IOptions<BotOptions> botOptions, ILogger<OpenAiBotClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _botOptions = botOptions.Value;
        _logger = logger;
    }

    public async Task<OpenAiBotDecisionResult> SelectOptionAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        var requestMessages = new[]
        {
            new OpenAiChatMessage("system", systemPrompt),
            new OpenAiChatMessage("user", userPrompt)
        };

        var response = await SendChatCompletionAsync(requestMessages, requireJsonObject: true, cancellationToken);
        if (!response.Succeeded)
        {
            return response.TimedOut
                ? OpenAiBotDecisionResult.Timeout()
                : OpenAiBotDecisionResult.Failed(response.FailureReason ?? "OpenAI request failed.", response.RawResponse);
        }

        var optionId = ParseSelectedOptionId(response.RawResponse ?? string.Empty);
        if (string.IsNullOrWhiteSpace(optionId))
        {
#pragma warning disable CA1848 // Use the LoggerMessage delegates
            _logger.LogWarning("OpenAI response did not contain a selectedOptionId. Response: {ResponseContent}", response.RawResponse);
#pragma warning restore CA1848 // Use the LoggerMessage delegates
        }

        return string.IsNullOrWhiteSpace(optionId)
            ? OpenAiBotDecisionResult.Failed("OpenAI response did not contain a selectedOptionId.", response.RawResponse)
            : OpenAiBotDecisionResult.Success(optionId, response.RawResponse ?? string.Empty);
    }

    public async Task<OpenAiChatCompletionResult> CompleteChatAsync(
        string systemPrompt,
        IReadOnlyList<OpenAiChatMessage> messages,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
        ArgumentNullException.ThrowIfNull(messages);

        var requestMessages = new List<OpenAiChatMessage>(messages.Count + 1)
        {
            new("system", systemPrompt)
        };
        requestMessages.AddRange(messages);

        var response = await SendChatCompletionAsync(requestMessages, requireJsonObject: false, cancellationToken);
        if (!response.Succeeded)
        {
            return response.TimedOut
                ? OpenAiChatCompletionResult.Timeout()
                : OpenAiChatCompletionResult.Failed(response.FailureReason ?? "OpenAI request failed.", response.RawResponse);
        }

        var assistantText = ExtractAssistantMessage(response.RawResponse ?? string.Empty);
        if (string.IsNullOrWhiteSpace(assistantText))
        {
#pragma warning disable CA1848 // Use the LoggerMessage delegates
            _logger.LogWarning("OpenAI chat response did not contain assistant text. Response: {ResponseContent}", response.RawResponse);
#pragma warning restore CA1848 // Use the LoggerMessage delegates
            return OpenAiChatCompletionResult.Failed("OpenAI response did not contain assistant text.", response.RawResponse);
        }

        return OpenAiChatCompletionResult.Success(assistantText, response.RawResponse ?? string.Empty);
    }

    private async Task<OpenAiRawChatCompletionResult> SendChatCompletionAsync(
        IReadOnlyList<OpenAiChatMessage> messages,
        bool requireJsonObject,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_botOptions.OpenAIKey))
        {
#pragma warning disable CA1848 // Use the LoggerMessage delegates
            _logger.LogWarning("OpenAI bot decision skipped because no API key is configured.");
#pragma warning restore CA1848 // Use the LoggerMessage delegates
            return OpenAiRawChatCompletionResult.Failed("Missing OpenAI API key.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_botOptions.DecisionTimeoutSeconds));

        using var request = new HttpRequestMessage(HttpMethod.Post, ChatCompletionsEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _botOptions.OpenAIKey);

        var payload = new
        {
            model = _botOptions.OpenAIModel,
            temperature = 0.1,
            response_format = requireJsonObject ? new { type = "json_object" } : null,
            messages = messages.Select(message => new { role = message.Role, content = message.Content }).ToArray()
        };

        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

        try
        {
            var client = _httpClientFactory.CreateClient();
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
            var responseContent = await response.Content.ReadAsStringAsync(timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
#pragma warning disable CA1848 // Use the LoggerMessage delegates
                _logger.LogWarning(
                    "OpenAI request failed with status {StatusCode}. Response: {ResponseContent}",
                    (int)response.StatusCode,
                    responseContent);
#pragma warning restore CA1848 // Use the LoggerMessage delegates
                return OpenAiRawChatCompletionResult.Failed(BuildFailureReason(response.StatusCode, responseContent), responseContent);
            }

            return OpenAiRawChatCompletionResult.Success(responseContent);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
#pragma warning disable CA1848 // Use the LoggerMessage delegates
            _logger.LogWarning("OpenAI decision request timed out after {TimeoutSeconds} seconds.", _botOptions.DecisionTimeoutSeconds);
#pragma warning restore CA1848 // Use the LoggerMessage delegates
            return OpenAiRawChatCompletionResult.Timeout();
        }
        catch (HttpRequestException ex)
        {
#pragma warning disable CA1848 // Use the LoggerMessage delegates
            _logger.LogError(ex, "OpenAI decision request failed before a response was returned.");
#pragma warning restore CA1848 // Use the LoggerMessage delegates
            return OpenAiRawChatCompletionResult.Failed(ex.Message);
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

        var content = ExtractMessageContent(contentElement);
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var normalizedContent = NormalizeJsonContent(content);
        using var contentDocument = JsonDocument.Parse(normalizedContent);
        return contentDocument.RootElement.TryGetProperty("selectedOptionId", out var selectedOptionId)
            ? selectedOptionId.GetString()
            : null;
    }

    private static string BuildFailureReason(System.Net.HttpStatusCode statusCode, string responseContent)
    {
        var apiErrorMessage = TryExtractApiErrorMessage(responseContent);
        return string.IsNullOrWhiteSpace(apiErrorMessage)
            ? $"OpenAI request failed with status {(int)statusCode}."
            : $"OpenAI request failed with status {(int)statusCode}: {apiErrorMessage}";
    }

    private static string? TryExtractApiErrorMessage(string responseContent)
    {
        if (string.IsNullOrWhiteSpace(responseContent))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseContent);
            if (!document.RootElement.TryGetProperty("error", out var errorElement))
            {
                return null;
            }

            return errorElement.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractMessageContent(JsonElement contentElement)
    {
        return contentElement.ValueKind switch
        {
            JsonValueKind.String => contentElement.GetString(),
            JsonValueKind.Array => string.Concat(contentElement
                .EnumerateArray()
                .Select(item => item.TryGetProperty("text", out var textElement) ? textElement.GetString() : null)
                .Where(text => !string.IsNullOrWhiteSpace(text))),
            _ => null
        };
    }

    private static string? ExtractAssistantMessage(string responseContent)
    {
        using var root = JsonDocument.Parse(responseContent);

        if (!root.RootElement.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            return null;
        }

        var message = choices[0].GetProperty("message");
        return message.TryGetProperty("content", out var contentElement)
            ? ExtractMessageContent(contentElement)
            : null;
    }

    private static string NormalizeJsonContent(string content)
    {
        var trimmed = content.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var lines = trimmed
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .ToList();

        if (lines.Count >= 2 && lines[0].StartsWith("```", StringComparison.Ordinal) && lines[^1] == "```")
        {
            return string.Join('\n', lines.Skip(1).Take(lines.Count - 2)).Trim();
        }

        return trimmed;
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

public sealed record OpenAiChatMessage(string Role, string Content);

public sealed record OpenAiChatCompletionResult
{
    public bool Succeeded { get; init; }
    public bool TimedOut { get; init; }
    public string? AssistantText { get; init; }
    public string? FailureReason { get; init; }
    public string? RawResponse { get; init; }

    public static OpenAiChatCompletionResult Success(string assistantText, string rawResponse) => new()
    {
        Succeeded = true,
        AssistantText = assistantText,
        RawResponse = rawResponse
    };

    public static OpenAiChatCompletionResult Timeout() => new()
    {
        TimedOut = true,
        FailureReason = "OpenAI chat request timed out."
    };

    public static OpenAiChatCompletionResult Failed(string failureReason, string? rawResponse = null) => new()
    {
        FailureReason = failureReason,
        RawResponse = rawResponse
    };
}

internal sealed record OpenAiRawChatCompletionResult
{
    public bool Succeeded { get; init; }
    public bool TimedOut { get; init; }
    public string? FailureReason { get; init; }
    public string? RawResponse { get; init; }

    public static OpenAiRawChatCompletionResult Success(string rawResponse) => new()
    {
        Succeeded = true,
        RawResponse = rawResponse
    };

    public static OpenAiRawChatCompletionResult Timeout() => new()
    {
        TimedOut = true,
        FailureReason = "OpenAI request timed out."
    };

    public static OpenAiRawChatCompletionResult Failed(string failureReason, string? rawResponse = null) => new()
    {
        FailureReason = failureReason,
        RawResponse = rawResponse
    };
}