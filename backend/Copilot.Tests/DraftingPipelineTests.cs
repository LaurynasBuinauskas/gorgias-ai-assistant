using Copilot.Domain;
using Copilot.Pipeline;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Copilot.Tests;

public sealed class DraftingPipelineTests
{
    [Fact]
    public async Task ReturnsDraftFromModelReply()
    {
        var chatClient = new FakeChatClient("Hallo Jane, gerne senden wir Ihnen die Rechnung zu.");
        var pipeline = CreatePipeline(chatClient);

        var result = await pipeline.GenerateDraftAsync(Ticket(CustomerMessage("Bitte um Rechnung")), CancellationToken.None);

        var success = Assert.IsType<PipelineResult.Success>(result);
        Assert.Equal("Hallo Jane, gerne senden wir Ihnen die Rechnung zu.", success.Draft.Body);
        Assert.Equal(42, success.Draft.TicketId);
        Assert.Equal("de", success.Draft.Language);
    }

    [Fact]
    public async Task ReturnsInsufficientWhenNoCustomerMessage()
    {
        var chatClient = new FakeChatClient("should never be called");
        var pipeline = CreatePipeline(chatClient);

        var result = await pipeline.GenerateDraftAsync(Ticket(AgentMessage("We shipped it")), CancellationToken.None);

        Assert.IsType<PipelineResult.InsufficientKnowledge>(result);
        Assert.Null(chatClient.LastMessages);
    }

    [Fact]
    public async Task ExcludesInternalNotesFromPrompt()
    {
        var chatClient = new FakeChatClient("reply");
        var pipeline = CreatePipeline(chatClient);
        var ticket = Ticket(
            CustomerMessage("Where is my order?"),
            InternalNote("secret asana link"));

        await pipeline.GenerateDraftAsync(ticket, CancellationToken.None);

        var transcript = Assert.Single(chatClient.LastMessages!, m => m.Role == ChatRole.User).Text;
        Assert.Contains("Where is my order?", transcript);
        Assert.DoesNotContain("secret asana link", transcript);
    }

    [Fact]
    public async Task ReturnsInsufficientWhenModelReplyIsEmpty()
    {
        var pipeline = CreatePipeline(new FakeChatClient("   "));

        var result = await pipeline.GenerateDraftAsync(Ticket(CustomerMessage("Hello")), CancellationToken.None);

        Assert.IsType<PipelineResult.InsufficientKnowledge>(result);
    }

    private static DraftingPipeline CreatePipeline(IChatClient chatClient) =>
        new(chatClient, NullLogger<DraftingPipeline>.Instance);

    private static TicketContext Ticket(params TicketMessage[] messages) => new()
    {
        Id = 42,
        Subject = "Order question",
        Status = "open",
        Language = "de",
        Customer = new TicketCustomer("Jane Doe", "jane@example.com"),
        Messages = messages,
    };

    private static TicketMessage CustomerMessage(string text) => new()
    {
        Id = 1,
        FromAgent = false,
        IsInternalNote = false,
        Text = text,
        SenderName = "Jane Doe",
        SentAt = DateTimeOffset.UtcNow,
    };

    private static TicketMessage AgentMessage(string text) => new()
    {
        Id = 2,
        FromAgent = true,
        IsInternalNote = false,
        Text = text,
        SenderName = "Agent",
        SentAt = DateTimeOffset.UtcNow,
    };

    private static TicketMessage InternalNote(string text) => new()
    {
        Id = 3,
        FromAgent = true,
        IsInternalNote = true,
        Text = text,
        SenderName = "Agent",
        SentAt = DateTimeOffset.UtcNow,
    };

    private sealed class FakeChatClient(string reply) : IChatClient
    {
        public List<ChatMessage>? LastMessages { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastMessages = messages.ToList();
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
