using System.Runtime.CompilerServices;
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

        var result = await pipeline.GenerateDraftAsync(
            Ticket(CustomerMessage("Bitte um Rechnung")),
            DraftRequest.Initial,
            CancellationToken.None);

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

        var result = await pipeline.GenerateDraftAsync(
            Ticket(AgentMessage("We shipped it")),
            DraftRequest.Initial,
            CancellationToken.None);

        Assert.IsType<PipelineResult.InsufficientKnowledge>(result);
        Assert.Null(chatClient.LastMessages);
    }

    [Fact]
    public async Task ExcludesInternalNotesFromPrompt()
    {
        var chatClient = new FakeChatClient("reply");
        var pipeline = CreatePipeline(chatClient);
        var ticket = Ticket(CustomerMessage("Where is my order?"), InternalNote("secret asana link"));

        await pipeline.GenerateDraftAsync(ticket, DraftRequest.Initial, CancellationToken.None);

        var transcript = Assert.Single(chatClient.LastMessages!, m => m.Role == ChatRole.User).Text;
        Assert.Contains("Where is my order?", transcript);
        Assert.DoesNotContain("secret asana link", transcript);
    }

    [Fact]
    public async Task ReturnsInsufficientWhenModelReplyIsEmpty()
    {
        var pipeline = CreatePipeline(new FakeChatClient("   "));

        var result = await pipeline.GenerateDraftAsync(
            Ticket(CustomerMessage("Hello")),
            DraftRequest.Initial,
            CancellationToken.None);

        Assert.IsType<PipelineResult.InsufficientKnowledge>(result);
    }

    [Fact]
    public async Task ReplaysRefinementTurnsAndInstruction()
    {
        var chatClient = new FakeChatClient("Hello Jane, the invoice is on its way.");
        var pipeline = CreatePipeline(chatClient);
        var request = new DraftRequest
        {
            Turns =
            [
                new DraftTurn(DraftTurnRole.Assistant, "Hallo Jane, die Rechnung folgt."),
                new DraftTurn(DraftTurnRole.Agent, "make it warmer"),
            ],
            Instruction = "translate to English",
        };

        await pipeline.GenerateDraftAsync(Ticket(CustomerMessage("Rechnung?")), request, CancellationToken.None);

        var sent = chatClient.LastMessages!;
        Assert.Equal(ChatRole.Assistant, sent[2].Role);
        Assert.Equal("Hallo Jane, die Rechnung folgt.", sent[2].Text);
        Assert.Equal("make it warmer", sent[3].Text);
        // The new instruction is always last so the model acts on it.
        Assert.Equal("translate to English", sent[^1].Text);
    }

    [Fact]
    public async Task StreamsDeltasInOrder()
    {
        var pipeline = CreatePipeline(new FakeChatClient("Hallo ", "Jane, ", "danke."));

        var chunks = new List<DraftChunk>();
        await foreach (var chunk in pipeline.StreamDraftAsync(
            Ticket(CustomerMessage("Hallo")),
            DraftRequest.Initial,
            CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        Assert.Equal(
            "Hallo Jane, danke.",
            string.Concat(chunks.OfType<DraftChunk.Delta>().Select(d => d.Text)));
    }

    [Fact]
    public async Task StreamYieldsInsufficientWithoutCallingTheModel()
    {
        var chatClient = new FakeChatClient("never");
        var pipeline = CreatePipeline(chatClient);

        var chunks = new List<DraftChunk>();
        await foreach (var chunk in pipeline.StreamDraftAsync(
            Ticket(AgentMessage("only agent")),
            DraftRequest.Initial,
            CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        Assert.IsType<DraftChunk.Insufficient>(Assert.Single(chunks));
        Assert.Null(chatClient.LastMessages);
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

    private static TicketMessage CustomerMessage(string text) => Message(1, fromAgent: false, isNote: false, text);

    private static TicketMessage AgentMessage(string text) => Message(2, fromAgent: true, isNote: false, text);

    private static TicketMessage InternalNote(string text) => Message(3, fromAgent: true, isNote: true, text);

    private static TicketMessage Message(long id, bool fromAgent, bool isNote, string text) => new()
    {
        Id = id,
        FromAgent = fromAgent,
        IsInternalNote = isNote,
        Text = text,
        SenderName = fromAgent ? "Agent" : "Jane Doe",
        SentAt = DateTimeOffset.UtcNow,
    };

    private sealed class FakeChatClient(params string[] reply) : IChatClient
    {
        public List<ChatMessage>? LastMessages { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastMessages = messages.ToList();
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Concat(reply))));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastMessages = messages.ToList();
            foreach (var part in reply)
            {
                await Task.Yield();
                yield return new ChatResponseUpdate(ChatRole.Assistant, part);
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
