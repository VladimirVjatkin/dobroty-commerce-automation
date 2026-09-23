namespace Portfolio.LiveChat.Options;

/// <summary>
/// Configuration for the outbound chat/AI endpoint call. Bound from the "Chat" configuration
/// section (see appsettings.Example.json) — never hard-coded and never defaulted to a real URL.
/// </summary>
public class ChatOptions
{
    public const string SectionName = "Chat";

    /// <summary>Maximum accepted message length, in UTF-16 code units, after trimming.</summary>
    public const int MaxMessageLength = 1000;

    /// <summary>Maximum accepted request body size, in bytes.</summary>
    public const int MaxRequestBodyBytes = 16 * 1024;

    /// <summary>
    /// Absolute URL of the external chat/AI endpoint. Intentionally has no default value:
    /// a missing or invalid value must fail safely rather than fall back to a placeholder host.
    /// </summary>
    public string? EndpointUrl { get; set; }

    /// <summary>Upper bound on how long the outbound call is allowed to take.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
