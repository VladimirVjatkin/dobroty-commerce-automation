using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Portfolio.LiveChat.Controllers;
using Portfolio.LiveChat.Models;
using Portfolio.LiveChat.Options;
using Portfolio.LiveChat.Services;

namespace Portfolio.LiveChat.Tests;

public class ChatControllerTests
{
    [Fact]
    public async Task Send_NullRequest_Returns400_AndDoesNotCallService()
    {
        var service = new FakeChatService(new ChatResult(ChatResultStatus.Success, "unused"));
        var controller = new ChatController(service);

        var result = await controller.Send(null, CancellationToken.None);

        AssertBadRequest(result);
        Assert.False(service.WasCalled);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Send_EmptyOrWhitespaceMessage_Returns400_AndDoesNotCallService(string message)
    {
        var service = new FakeChatService(new ChatResult(ChatResultStatus.Success, "unused"));
        var controller = new ChatController(service);

        var result = await controller.Send(new ChatRequest { Message = message }, CancellationToken.None);

        AssertBadRequest(result);
        Assert.False(service.WasCalled);
    }

    [Fact]
    public async Task Send_MessageAtMaxLength_IsAccepted_AndForwardedToService()
    {
        var message = new string('a', ChatOptions.MaxMessageLength);
        var service = new FakeChatService(new ChatResult(ChatResultStatus.Success, "ok"));
        var controller = new ChatController(service);

        var result = await controller.Send(new ChatRequest { Message = message }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.True(service.WasCalled);
        Assert.Equal(message, service.ReceivedMessage);
    }

    [Fact]
    public async Task Send_MessageOverMaxLength_Returns400_AndDoesNotCallService()
    {
        var message = new string('a', ChatOptions.MaxMessageLength + 1);
        var service = new FakeChatService(new ChatResult(ChatResultStatus.Success, "unused"));
        var controller = new ChatController(service);

        var result = await controller.Send(new ChatRequest { Message = message }, CancellationToken.None);

        AssertBadRequest(result);
        Assert.False(service.WasCalled);
    }

    [Fact]
    public async Task Send_TrimsMessageBeforeForwardingToService()
    {
        var service = new FakeChatService(new ChatResult(ChatResultStatus.Success, "ok"));
        var controller = new ChatController(service);

        await controller.Send(new ChatRequest { Message = "  hello world  " }, CancellationToken.None);

        Assert.Equal("hello world", service.ReceivedMessage);
    }

    [Fact]
    public async Task Send_ServiceReturnsSuccess_Returns200WithReply()
    {
        var service = new FakeChatService(new ChatResult(ChatResultStatus.Success, "Here is your answer."));
        var controller = new ChatController(service);

        var result = await controller.Send(new ChatRequest { Message = "hello" }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ChatResponse>(ok.Value);
        Assert.Equal("Here is your answer.", response.Reply);
    }

    [Fact]
    public async Task Send_ServiceReturnsNotConfigured_Returns503()
    {
        var service = new FakeChatService(new ChatResult(ChatResultStatus.NotConfigured));
        var controller = new ChatController(service);

        var result = await controller.Send(new ChatRequest { Message = "hello" }, CancellationToken.None);

        AssertStatus(result, StatusCodes.Status503ServiceUnavailable);
    }

    [Fact]
    public async Task Send_ServiceReturnsTimeout_Returns504()
    {
        var service = new FakeChatService(new ChatResult(ChatResultStatus.Timeout));
        var controller = new ChatController(service);

        var result = await controller.Send(new ChatRequest { Message = "hello" }, CancellationToken.None);

        AssertStatus(result, StatusCodes.Status504GatewayTimeout);
    }

    [Theory]
    [InlineData(ChatResultStatus.Unreachable)]
    [InlineData(ChatResultStatus.UpstreamError)]
    [InlineData(ChatResultStatus.InvalidResponse)]
    public async Task Send_ServiceReturnsTransportOrUpstreamFailure_Returns502(ChatResultStatus status)
    {
        var service = new FakeChatService(new ChatResult(status));
        var controller = new ChatController(service);

        var result = await controller.Send(new ChatRequest { Message = "hello" }, CancellationToken.None);

        AssertStatus(result, StatusCodes.Status502BadGateway);
    }

    [Fact]
    public async Task Send_ServiceReturnsCallerCancelled_Returns499_WithNoBody()
    {
        var service = new FakeChatService(new ChatResult(ChatResultStatus.CallerCancelled));
        var controller = new ChatController(service);

        var result = await controller.Send(new ChatRequest { Message = "hello" }, CancellationToken.None);

        var statusResult = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(499, statusResult.StatusCode);
    }

    [Fact]
    public async Task Send_EveryApplicationFailure_UsesTheSameReplyEnvelopeShape()
    {
        // 499 is the one deliberate exception to this envelope (see the controller's XML doc
        // comment) and is covered separately above.
        foreach (var status in new[]
        {
            ChatResultStatus.NotConfigured,
            ChatResultStatus.Timeout,
            ChatResultStatus.Unreachable,
            ChatResultStatus.UpstreamError,
            ChatResultStatus.InvalidResponse
        })
        {
            var service = new FakeChatService(new ChatResult(status));
            var controller = new ChatController(service);

            var result = await controller.Send(new ChatRequest { Message = "hello" }, CancellationToken.None);

            var objectResult = Assert.IsType<ObjectResult>(result);
            var response = Assert.IsType<ChatResponse>(objectResult.Value);
            Assert.False(string.IsNullOrWhiteSpace(response.Reply));
        }
    }

    private static void AssertBadRequest(IActionResult result) => AssertStatus(result, StatusCodes.Status400BadRequest);

    private static void AssertStatus(IActionResult result, int expectedStatusCode)
    {
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedStatusCode, objectResult.StatusCode);
        Assert.IsType<ChatResponse>(objectResult.Value);
    }

    private sealed class FakeChatService : IChatService
    {
        private readonly ChatResult _result;

        public FakeChatService(ChatResult result) => _result = result;

        public bool WasCalled { get; private set; }

        public string? ReceivedMessage { get; private set; }

        public Task<ChatResult> SendAsync(string message, CancellationToken cancellationToken)
        {
            WasCalled = true;
            ReceivedMessage = message;
            return Task.FromResult(_result);
        }
    }
}
