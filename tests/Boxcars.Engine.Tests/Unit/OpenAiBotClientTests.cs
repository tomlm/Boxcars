using System.Net;
using System.Text;
using System.Text.Json;
using Boxcars.Data;
using Boxcars.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Boxcars.Engine.Tests.Unit;

public class OpenAiBotClientTests
{
    [Fact]
    public async Task SelectOptionAsync_Success_StringContent_ReturnsSelectedOptionId()
    {
        var client = CreateClient("""
            {
              "choices": [
                {
                  "message": {
                    "content": "{\"selectedOptionId\":\"auction-bid:min\"}"
                  }
                }
              ]
            }
            """,
            HttpStatusCode.OK);

        var result = await client.SelectOptionAsync("system", "user", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("auction-bid:min", result.SelectedOptionId);
    }

    [Fact]
    public async Task SelectOptionAsync_Success_ContentArray_ReturnsSelectedOptionId()
    {
        var client = CreateClient("""
            {
              "choices": [
                {
                  "message": {
                    "content": [
                      {
                        "type": "output_text",
                        "text": "```json\n{\"selectedOptionId\":\"auction-pass\"}\n```"
                      }
                    ]
                  }
                }
              ]
            }
            """,
            HttpStatusCode.OK);

        var result = await client.SelectOptionAsync("system", "user", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("auction-pass", result.SelectedOptionId);
    }

    [Fact]
    public async Task SelectOptionAsync_ErrorResponse_UsesApiMessageInFailureReason()
    {
        var client = CreateClient("""
            {
              "error": {
                "message": "This model's maximum context length was exceeded."
              }
            }
            """,
            HttpStatusCode.BadRequest);

        var result = await client.SelectOptionAsync("system", "user", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("OpenAI request failed with status 400: This model's maximum context length was exceeded.", result.FailureReason);
    }

    [Fact]
    public async Task SelectOptionAsync_ReasoningModel_SendsReasoningEffortAndNoTemperature()
    {
        var factory = new StubHttpClientFactory(SelectedOptionResponse, HttpStatusCode.OK);
        var client = CreateClient(factory, new BotOptions { OpenAIKey = "test-key", OpenAIModel = "gpt-5.4-mini", OpenAIReasoningEffort = "low" });

        await client.SelectOptionAsync("system", "user", CancellationToken.None);

        using var request = JsonDocument.Parse(factory.LastRequestBody!);
        Assert.Equal("gpt-5.4-mini", request.RootElement.GetProperty("model").GetString());
        Assert.Equal("low", request.RootElement.GetProperty("reasoning_effort").GetString());
        Assert.False(request.RootElement.TryGetProperty("temperature", out _));
        Assert.Equal("json_object", request.RootElement.GetProperty("response_format").GetProperty("type").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("none")]
    public async Task SelectOptionAsync_ReasoningOff_SendsTemperature(string reasoningEffort)
    {
        var factory = new StubHttpClientFactory(SelectedOptionResponse, HttpStatusCode.OK);
        var client = CreateClient(factory, new BotOptions { OpenAIKey = "test-key", OpenAIModel = "gpt-4o-mini", OpenAIReasoningEffort = reasoningEffort });

        await client.SelectOptionAsync("system", "user", CancellationToken.None);

        using var request = JsonDocument.Parse(factory.LastRequestBody!);
        Assert.Equal(0.1, request.RootElement.GetProperty("temperature").GetDouble());
        Assert.Equal(!string.IsNullOrEmpty(reasoningEffort), request.RootElement.TryGetProperty("reasoning_effort", out _));
    }

    [Fact]
    public async Task CompleteChatAsync_FreeText_SendsNoResponseFormat()
    {
        var factory = new StubHttpClientFactory(SelectedOptionResponse, HttpStatusCode.OK);
        var client = CreateClient(factory, new BotOptions { OpenAIKey = "test-key", OpenAIModel = "gpt-5.4-mini" });

        await client.CompleteChatAsync("system", [new OpenAiChatMessage("user", "What should I buy?")], CancellationToken.None);

        using var request = JsonDocument.Parse(factory.LastRequestBody!);
        Assert.False(request.RootElement.TryGetProperty("response_format", out _));
    }

    private const string SelectedOptionResponse = """
        {
          "choices": [
            {
              "message": {
                "content": "{\"selectedOptionId\":\"decline-purchase\"}"
              }
            }
          ]
        }
        """;

    private static OpenAiBotClient CreateClient(string responseBody, HttpStatusCode statusCode)
    {
        return CreateClient(
            new StubHttpClientFactory(responseBody, statusCode),
            new BotOptions
            {
                OpenAIKey = "test-key",
                OpenAIModel = "gpt-4o-mini",
                DecisionTimeoutSeconds = 15
            });
    }

    private static OpenAiBotClient CreateClient(StubHttpClientFactory factory, BotOptions options)
    {
        return new OpenAiBotClient(factory, Options.Create(options), NullLogger<OpenAiBotClient>.Instance);
    }

    private sealed class StubHttpClientFactory(string responseBody, HttpStatusCode statusCode) : IHttpClientFactory
    {
        public string? LastRequestBody { get; private set; }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new StubHttpMessageHandler(responseBody, statusCode, body => LastRequestBody = body), disposeHandler: true);
        }
    }

    private sealed class StubHttpMessageHandler(string responseBody, HttpStatusCode statusCode, Action<string> captureRequestBody) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            captureRequestBody(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}