using Microsoft.AspNetCore.Mvc;
using Portfolio.LiveChat.Models;
using Portfolio.LiveChat.Options;
using Portfolio.LiveChat.Services;

namespace Portfolio.LiveChat.Controllers;

[ApiController]
[Route("api/chat")]
public class ChatController : ControllerBase
{
    private readonly IChatService _chatService;

    public ChatController(IChatService chatService)
    {
        _chatService = chatService;
    }

    /// <summary>
    /// Accepts a chat message and forwards it to the configured external endpoint.
    /// Every application-generated response — success and failure alike — uses the same
    /// { "reply": "..." } envelope, except for 499 (caller cancellation), which intentionally
    /// carries no body because the caller has already disconnected.
    /// </summary>
    [HttpPost("send")]
    [RequestSizeLimit(ChatOptions.MaxRequestBodyBytes)]
    public async Task<IActionResult> Send([FromBody] ChatRequest? request, CancellationToken cancellationToken)
    {
        var message = request?.Message?.Trim();

        if (string.IsNullOrEmpty(message))
        {
            return StatusCode(StatusCodes.Status400BadRequest, new ChatResponse("Please enter a message."));
        }

        if (message.Length > ChatOptions.MaxMessageLength)
        {
            return StatusCode(
                StatusCodes.Status400BadRequest,
                new ChatResponse($"Your message is too long (maximum {ChatOptions.MaxMessageLength} characters)."));
        }

        var result = await _chatService.SendAsync(message, cancellationToken);

        if (result.Status == ChatResultStatus.CallerCancelled)
        {
            // The caller already disconnected; there is no one to receive a body.
            return StatusCode(499);
        }

        if (result.Status == ChatResultStatus.Success)
        {
            return Ok(new ChatResponse(result.Reply!));
        }

        var statusCode = result.Status switch
        {
            ChatResultStatus.NotConfigured => StatusCodes.Status503ServiceUnavailable,
            ChatResultStatus.Timeout => StatusCodes.Status504GatewayTimeout,
            ChatResultStatus.Unreachable => StatusCodes.Status502BadGateway,
            ChatResultStatus.UpstreamError => StatusCodes.Status502BadGateway,
            ChatResultStatus.InvalidResponse => StatusCodes.Status502BadGateway,
            _ => StatusCodes.Status502BadGateway
        };

        return StatusCode(statusCode, new ChatResponse("The assistant is temporarily unavailable. Please try again in a moment."));
    }
}
