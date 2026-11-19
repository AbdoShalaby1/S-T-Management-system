using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OscCommittee.Web.Services.Common;
using OscCommittee.Web.Services.Grading;
using Xunit;

namespace OscCommittee.Tests.Services.Grading;

public sealed class GeminiClientTests
{
    [Fact]
    public async Task TheRequestMatchesThePlanSchemaAndCarriesTheKey()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, SuccessBody("{\"score\":8}")));
        var client = CreateClient(handler);

        var result = await client.SendAsync("Grade this folder.");

        Assert.True(result.Success);
        Assert.NotNull(handler.Request);
        Assert.Equal("test-key", handler.Request!.Headers.GetValues("x-goog-api-key").Single());
        Assert.Contains(
            "/v1beta/models/gemini-test:generateContent",
            handler.Request.RequestUri!.AbsolutePath,
            StringComparison.Ordinal);

        var body = handler.Body!;
        Assert.Contains("\"role\":\"user\"", body);
        Assert.Contains("Grade this folder.", body);
        Assert.Contains("\"temperature\":0.2", body);
        Assert.Contains("\"responseMimeType\":\"application/json\"", body);
        Assert.Contains("\"responseSchema\"", body);
        Assert.Contains("\"must-fix\"", body);
        Assert.Contains("\"score\"", body);
    }

    [Fact]
    public async Task TextAndTokenUsageAreParsed()
    {
        const string feedback = "{\"score\":9,\"summary\":\"Good\"}";
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, SuccessBody(feedback)));

        var result = await CreateClient(handler).SendAsync("prompt");

        Assert.True(result.Success);
        Assert.Equal(feedback, result.Text);
        Assert.Equal(123, result.InputTokens);
        Assert.Equal(45, result.OutputTokens);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.BadGateway, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public async Task OnlyRateLimitsAndServerErrorsAreRetryable(
        HttpStatusCode status,
        bool retryable)
    {
        var handler = new StubHandler(_ => Json(status, "{}"));

        var result = await CreateClient(handler).SendAsync("prompt");

        Assert.False(result.Success);
        Assert.Equal(retryable, result.Retryable);
        Assert.Equal((int)status, result.StatusCode);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task MissingConfigurationDoesNotCallTheApi()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, SuccessBody("{}")));
        var client = CreateClient(handler, new AiOptions { ApiKey = "", Model = "" });

        var result = await client.SendAsync("prompt");

        Assert.False(result.Success);
        Assert.False(result.Retryable);
        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task ABlockedPromptIsNotRetried()
    {
        var handler = new StubHandler(_ => Json(
            HttpStatusCode.OK,
            """{ "promptFeedback": { "blockReason": "SAFETY" } }"""));

        var result = await CreateClient(handler).SendAsync("prompt");

        Assert.False(result.Success);
        Assert.False(result.Retryable);
        Assert.Contains("declined", result.Error);
    }

    [Fact]
    public async Task AnEmptyAnswerIsRetryable()
    {
        var handler = new StubHandler(_ => Json(
            HttpStatusCode.OK,
            """{ "candidates": [ { "content": { "parts": [] } } ] }"""));

        var result = await CreateClient(handler).SendAsync("prompt");

        Assert.False(result.Success);
        Assert.True(result.Retryable);
    }

    [Fact]
    public async Task ATruncatedAnswerKeepsThePartialText()
    {
        var handler = new StubHandler(_ => Json(
            HttpStatusCode.OK,
            Body("{\"score\":5", "MAX_TOKENS")));

        var result = await CreateClient(handler).SendAsync("prompt");

        Assert.True(result.Success);
        Assert.True(result.Truncated);
        Assert.Null(result.Error);
        Assert.Equal("{\"score\":5", result.Text);
    }

    [Fact]
    public async Task AnEmptyTruncatedAnswerIsRetryable()
    {
        var handler = new StubHandler(_ => Json(
            HttpStatusCode.OK,
            Body(null, "MAX_TOKENS")));

        var result = await CreateClient(handler).SendAsync("prompt");

        Assert.False(result.Success);
        Assert.True(result.Retryable);
        Assert.True(result.Truncated);
        Assert.Contains("cut off", result.Error);
    }

    [Fact]
    public async Task ACompleteAnswerThatHitsTheLimitIsStillUsable()
    {
        var handler = new StubHandler(_ => Json(
            HttpStatusCode.OK,
            Body("{\"score\":5,\"summary\":\"Good\"}", "MAX_TOKENS")));

        var result = await CreateClient(handler).SendAsync("prompt");

        Assert.True(result.Success);
        Assert.True(result.Truncated);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task ABlockedCandidateIsNotRetried()
    {
        var handler = new StubHandler(_ => Json(
            HttpStatusCode.OK,
            Body(null, "SAFETY")));

        var result = await CreateClient(handler).SendAsync("prompt");

        Assert.False(result.Success);
        Assert.False(result.Retryable);
        Assert.Contains("declined", result.Error);
    }

    private static GeminiClient CreateClient(StubHandler handler, AiOptions? options = null)
    {
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://generativelanguage.googleapis.com/")
        };

        return new GeminiClient(
            http,
            Options.Create(options ?? new AiOptions
            {
                ApiKey = "test-key",
                Model = "gemini-test",
                Temperature = 0.2
            }),
            NullLogger<GeminiClient>.Instance);
    }

    private static string SuccessBody(string text) =>
        $$"""
        {
          "candidates": [
            { "content": { "parts": [ { "text": {{JsonSerializer.Serialize(text)}} } ] } }
          ],
          "usageMetadata": { "promptTokenCount": 123, "candidatesTokenCount": 45 }
        }
        """;

    private static string Body(string? text, string? finishReason)
    {
        var parts = text is null
            ? "[]"
            : $"[ {{ \"text\": {JsonSerializer.Serialize(text)} }} ]";
        var finish = finishReason is null
            ? string.Empty
            : $", \"finishReason\": {JsonSerializer.Serialize(finishReason)}";

        return $$"""
        {
          "candidates": [ { "content": { "parts": {{parts}} }{{finish}} } ],
          "usageMetadata": { "promptTokenCount": 123, "candidatesTokenCount": 45 }
        }
        """;
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return responder(request);
        }
    }
}