namespace Portfolio.LiveChat.Services;

/// <summary>Outcome of a chat transport attempt, used to drive the controller's status mapping.</summary>
public enum ChatResultStatus
{
    Success,
    NotConfigured,
    Timeout,
    Unreachable,
    UpstreamError,
    InvalidResponse,
    CallerCancelled
}

/// <summary>A small, typed result — no exceptions are used for expected failure paths.</summary>
public sealed record ChatResult(ChatResultStatus Status, string? Reply = null);

/// <summary>
/// Sends a validated, trimmed chat message to the configured external endpoint and returns a
/// typed outcome. Implementations must not throw for expected failure modes (missing
/// configuration, transport failure, timeout, malformed upstream response); they should return
/// the matching <see cref="ChatResultStatus"/> instead.
/// </summary>
public interface IChatService
{
    Task<ChatResult> SendAsync(string message, CancellationToken cancellationToken);
}
