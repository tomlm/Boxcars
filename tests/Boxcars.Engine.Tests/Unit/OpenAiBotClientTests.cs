using System.Net;
using System.Text;
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

    private static OpenAiBotClient CreateClient(string responseBody, HttpStatusCode statusCode)
    {
        return new OpenAiBotClient(
            new StubHttpClientFactory(responseBody, statusCode),
            Options.Create(new BotOptions
            {
                OpenAIKey = "test-key",
                OpenAIModel = "gpt-4o-mini",
                DecisionTimeoutSeconds = 15
          }),
          NullLogger<OpenAiBotClient>.Instance);
    }

    private sealed class StubHttpClientFactory(string responseBody, HttpStatusCode statusCode) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new StubHttpMessageHandler(responseBody, statusCode), disposeHandler: true);
        }
    }

    private sealed class StubHttpMessageHandler(string responseBody, HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
        }
    }
}