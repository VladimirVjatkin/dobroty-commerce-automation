namespace Portfolio.LiveChat.Models;

/// <summary>
/// Inbound request body for POST /api/chat/send. Deliberately minimal: this sample does not
/// model customer identity, session/client identifiers, or any other production-specific field.
/// </summary>
public sealed class ChatRequest
{
    public string? Message { get; set; }
}
