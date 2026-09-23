using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Portfolio.LiveChat.Options;

namespace Portfolio.LiveChat.Services;

/// <summary>
/// Concrete, single-implementation chat transport. Registered as a typed HttpClient so the
/// framework owns the HttpClient lifetime (see Program.cs) — this class is not itself
/// responsible for socket/connection management.
///
/// Logging policy: every log entry below records outcome, HTTP status, duration and response
/// size only. The message text, the endpoint URL, and the upstream response body are never
/// logged, in any branch — see ChatServiceTests for a test that enforces this directly.
/// </summary>
public class ChatService : IChatService
{
    private readonly HttpClient _httpClient;
    private readonly ChatOptions _options;
    private readonly ILogger<ChatService> _logger;

    public ChatService(HttpClient httpClient, IOptions<ChatOptions> options, ILogger<ChatService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ChatResult> SendAsync(string message, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        if (string.IsNullOrWhiteSpace(_options.EndpointUrl)
            || !Uri.TryCreate(_options.EndpointUrl, UriKind.Absolute, out var endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            _logger.LogError(
                "Chat outcome {Outcome} after {DurationMs} ms",
                ChatResultStatus.NotConfigured, stopwatch.ElapsedMilliseconds);
            return new ChatResult(ChatResultStatus.NotConfigured);
        }

        // Linking the caller's token to a timeout-only token lets the catch blocks below tell
        // "the caller gave up" apart from "we gave up waiting" — see the two OperationCanceledException
        // handlers, which differ only in whether the ORIGINAL token was the one that fired.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.RequestTimeout);

        try
        {
            var payload = new { message };
            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(endpoint, content, timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Chat outcome {Outcome} with HTTP {StatusCode} after {DurationMs} ms",
                    ChatResultStatus.UpstreamError, (int)response.StatusCode, stopwatch.ElapsedMilliseconds);
                return new ChatResult(ChatResultStatus.UpstreamError);
            }

            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);

            try
            {
                using var json = JsonDocument.Parse(body);
                if (json.RootElement.ValueKind != JsonValueKind.Object
                    || !json.RootElement.TryGetProperty("reply", out var replyProperty)
                    || replyProperty.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(replyProperty.GetString()))
                {
                    _logger.LogWarning(
                        "Chat outcome {Outcome} with reason {Reason}, response length {ResponseLength} after {DurationMs} ms",
                        ChatResultStatus.InvalidResponse, "missing_reply", body.Length, stopwatch.ElapsedMilliseconds);
                    return new ChatResult(ChatResultStatus.InvalidResponse);
                }

                var reply = replyProperty.GetString();
                _logger.LogInformation(
                    "Chat outcome {Outcome} with HTTP {StatusCode}, response length {ResponseLength} after {DurationMs} ms",
                    ChatResultStatus.Success, (int)response.StatusCode, body.Length, stopwatch.ElapsedMilliseconds);
                return new ChatResult(ChatResultStatus.Success, reply);
            }
            catch (JsonException)
            {
                _logger.LogWarning(
                    "Chat outcome {Outcome} with reason {Reason}, response length {ResponseLength} after {DurationMs} ms",
                    ChatResultStatus.InvalidResponse, "invalid_json", body.Length, stopwatch.ElapsedMilliseconds);
                return new ChatResult(ChatResultStatus.InvalidResponse);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(
                "Chat outcome {Outcome} after {DurationMs} ms",
                ChatResultStatus.CallerCancelled, stopwatch.ElapsedMilliseconds);
            return new ChatResult(ChatResultStatus.CallerCancelled);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Chat outcome {Outcome} after {DurationMs} ms",
                ChatResultStatus.Timeout, stopwatch.ElapsedMilliseconds);
            return new ChatResult(ChatResultStatus.Timeout);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(
                "Chat outcome {Outcome} with error {ErrorType} after {DurationMs} ms",
                ChatResultStatus.Unreachable, exception.GetType().Name, stopwatch.ElapsedMilliseconds);
            return new ChatResult(ChatResultStatus.Unreachable);
        }
    }
}
