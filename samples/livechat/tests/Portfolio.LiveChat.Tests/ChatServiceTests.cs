using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Portfolio.LiveChat.Options;
using Portfolio.LiveChat.Services;

namespace Portfolio.LiveChat.Tests;

public class ChatServiceTests
{
    private const string Endpoint = "https://example.invalid/webhook";

    private static ChatService CreateService(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder,
        TimeSpan? timeout = null,
        RecordingLogger<ChatService>? logger = null,
        string? endpointUrl = Endpoint)
    {
        var handler = new FakeHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler);
        // Fully qualified: this project's own "Options" namespace collides with the
        // Microsoft.Extensions.Options.Options static factory class by name.
        var options = Microsoft.Extensions.Options.Options.Create(new ChatOptions
        {
            EndpointUrl = endpointUrl,
            RequestTimeout = timeout ?? TimeSpan.FromSeconds(30)
        });
        return new ChatService(httpClient, options, logger ?? new RecordingLogger<ChatService>());
    }

    [Fact]
    public async Task SendAsync_WhenEndpointNotConfigured_ReturnsNotConfigured_WithoutCallingHttpClient()
    {
        var called = false;
        var service = CreateService(
            (_, _) =>
            {
                called = true;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            },
            endpointUrl: null);

        var result = await service.SendAsync("hello", CancellationToken.None);

        Assert.Equal(ChatResultStatus.NotConfigured, result.Status);
        Assert.Null(result.Reply);
        Assert.False(called, "No network call should be attempted when the endpoint is not configured.");
    }

    [Fact]
    public async Task SendAsync_ValidMessage_IsPostedAsJsonToConfiguredEndpoint()
    {
        // Trimming itself is the controller's responsibility (see ChatControllerTests); this
        // verifies the service sends exactly what it was given, to the configured endpoint,
        // as the expected JSON shape.
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;

        var service = CreateService(async (request, ct) =>
        {
            capturedRequest = request;
            capturedBody = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"reply\":\"ok\"}")
            };
        });

        var result = await service.SendAsync("hello", CancellationToken.None);

        Assert.Equal(ChatResultStatus.Success, result.Status);
        Assert.Equal(Endpoint, capturedRequest!.RequestUri!.ToString());
        using var json = JsonDocument.Parse(capturedBody!);
        Assert.Equal("hello", json.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task SendAsync_UpstreamReturns200WithValidReply_ReturnsSuccessWithReply()
    {
        var service = CreateService((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"reply\":\"Here is your answer.\"}")
        }));

        var result = await service.SendAsync("hello", CancellationToken.None);

        Assert.Equal(ChatResultStatus.Success, result.Status);
        Assert.Equal("Here is your answer.", result.Reply);
    }

    [Fact]
    public async Task SendAsync_UpstreamReturnsNonSuccessStatus_ReturnsUpstreamError()
    {
        var service = CreateService((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var result = await service.SendAsync("hello", CancellationToken.None);

        Assert.Equal(ChatResultStatus.UpstreamError, result.Status);
    }

    [Fact]
    public async Task SendAsync_UpstreamReturnsMalformedJson_ReturnsInvalidResponse()
    {
        var service = CreateService((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not json")
        }));

        var result = await service.SendAsync("hello", CancellationToken.None);

        Assert.Equal(ChatResultStatus.InvalidResponse, result.Status);
    }

    [Theory]
    [InlineData("{\"unexpected\":true}")]
    [InlineData("{\"reply\":123}")]
    [InlineData("{\"reply\":\"\"}")]
    [InlineData("{\"reply\":\"   \"}")]
    [InlineData("[1,2,3]")]
    public async Task SendAsync_UpstreamReturnsMissingOrInvalidReply_ReturnsInvalidResponse(string body)
    {
        var service = CreateService((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body)
        }));

        var result = await service.SendAsync("hello", CancellationToken.None);

        Assert.Equal(ChatResultStatus.InvalidResponse, result.Status);
    }

    [Fact]
    public async Task SendAsync_TransportFailure_ReturnsUnreachable()
    {
        var service = CreateService((_, _) => throw new HttpRequestException("connection refused"));

        var result = await service.SendAsync("hello", CancellationToken.None);

        Assert.Equal(ChatResultStatus.Unreachable, result.Status);
    }

    [Fact]
    public async Task SendAsync_UpstreamTimesOut_ReturnsTimeout_NotCallerCancelled()
    {
        var service = CreateService(
            async (_, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return new HttpResponseMessage(); // unreachable: the delay above always throws first
            },
            timeout: TimeSpan.FromMilliseconds(50));

        var result = await service.SendAsync("hello", CancellationToken.None);

        Assert.Equal(ChatResultStatus.Timeout, result.Status);
    }

    [Fact]
    public async Task SendAsync_CallerCancelsBeforeUpstreamResponds_ReturnsCallerCancelled_NotTimeout()
    {
        using var callerCts = new CancellationTokenSource();

        var service = CreateService(
            async (_, ct) =>
            {
                // Cancel the CALLER's token (not the service's own timeout) once the request
                // has actually been sent, to prove the two are distinguished correctly.
                callerCts.Cancel();
                await Task.Delay(Timeout.Infinite, ct);
                return new HttpResponseMessage();
            },
            timeout: TimeSpan.FromSeconds(30));

        var result = await service.SendAsync("hello", callerCts.Token);

        Assert.Equal(ChatResultStatus.CallerCancelled, result.Status);
    }

    [Fact]
    public async Task SendAsync_NeverLogsMessageContentOrEndpointUrl()
    {
        // This is a real, non-brittle content check: it does not assert on log call shape or
        // exact templates (which would break on harmless refactors), only that specific
        // sensitive substrings never appear in any recorded, formatted log message.
        var logger = new RecordingLogger<ChatService>();
        const string sensitiveMessage = "my order number is 123456 and my email is test@example.com";

        var successService = CreateService((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"reply\":\"ok\"}")
        }), logger: logger);
        await successService.SendAsync(sensitiveMessage, CancellationToken.None);

        var errorService = CreateService((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)), logger: logger);
        await errorService.SendAsync(sensitiveMessage, CancellationToken.None);

        var malformedService = CreateService((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not json")
        }), logger: logger);
        await malformedService.SendAsync(sensitiveMessage, CancellationToken.None);

        Assert.NotEmpty(logger.Messages);
        foreach (var entry in logger.Messages)
        {
            Assert.DoesNotContain(sensitiveMessage, entry, StringComparison.Ordinal);
            Assert.DoesNotContain("test@example.com", entry, StringComparison.Ordinal);
            Assert.DoesNotContain(Endpoint, entry, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Records every formatted log message so tests can assert on content, not on log-call
    /// shape. This is a test double, not a mocking-framework object, by design — the interface
    /// is small enough that a mocking library would add a dependency without adding clarity.
    /// </summary>
    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _responder(request, cancellationToken);
    }
}
