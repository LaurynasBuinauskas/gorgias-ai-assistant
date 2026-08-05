using System.Runtime.CompilerServices;
using Copilot.Domain;
using Copilot.Knowledge;
using Copilot.Pipeline;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Copilot.Tests;

/// <summary>
/// The relevance gate converts "confidently wrong" into "honestly silent". The property that
/// matters is not just the returned type — it is that no model call happens, so an uncovered
/// question costs nothing and cannot produce an invented answer.
/// </summary>
public sealed class RelevanceGateTests
{
    private const double Threshold = 2.2;

    [Fact]
    public async Task DeclinesWithoutCallingTheModelWhenPolicyDoesNotCoverTheQuestion()
    {
        var chatClient = new RecordingChatClient();
        var store = new FakeKnowledgeStore().ReturnsPolicyScoring(Threshold - 0.5);

        var result = await CreatePipeline(chatClient, store).GenerateDraftAsync(
            Ticket(), DraftRequest.Initial, CancellationToken.None);

        Assert.IsType<PipelineResult.InsufficientKnowledge>(result);
        Assert.Equal(0, chatClient.CallCount);
    }

    [Fact]
    public async Task DeclinesWhenNothingIsRetrievedAtAll()
    {
        var chatClient = new RecordingChatClient();

        var result = await CreatePipeline(chatClient, new FakeKnowledgeStore()).GenerateDraftAsync(
            Ticket(), DraftRequest.Initial, CancellationToken.None);

        Assert.IsType<PipelineResult.InsufficientKnowledge>(result);
        Assert.Equal(0, chatClient.CallCount);
    }

    [Fact]
    public async Task AnswersWhenPolicyCoversTheQuestion()
    {
        var chatClient = new RecordingChatClient();
        var store = new FakeKnowledgeStore().ReturnsPolicyScoring(Threshold + 0.5, market: "DE");

        var result = await CreatePipeline(chatClient, store).GenerateDraftAsync(
            Ticket(), DraftRequest.Initial, CancellationToken.None);

        Assert.IsType<PipelineResult.Success>(result);
        Assert.Equal(1, chatClient.CallCount);
        Assert.Contains("DE", chatClient.LastPrompt);
    }

    [Fact]
    public async Task ScoreExactlyAtTheThresholdIsCovered()
    {
        var chatClient = new RecordingChatClient();
        var store = new FakeKnowledgeStore().ReturnsPolicyScoring(Threshold);

        var result = await CreatePipeline(chatClient, store).GenerateDraftAsync(
            Ticket(), DraftRequest.Initial, CancellationToken.None);

        Assert.IsType<PipelineResult.Success>(result);
    }

    [Fact]
    public async Task ThresholdIsConfigurationSoTuningNeedsNoDeploy()
    {
        var store = new FakeKnowledgeStore().ReturnsPolicyScoring(2.0);

        var strict = await CreatePipeline(new RecordingChatClient(), store,
            new RetrievalOptions { MinimumPolicyScore = 2.5 })
            .GenerateDraftAsync(Ticket(), DraftRequest.Initial, CancellationToken.None);
        var lenient = await CreatePipeline(new RecordingChatClient(), store,
            new RetrievalOptions { MinimumPolicyScore = 1.5 })
            .GenerateDraftAsync(Ticket(), DraftRequest.Initial, CancellationToken.None);

        Assert.IsType<PipelineResult.InsufficientKnowledge>(strict);
        Assert.IsType<PipelineResult.Success>(lenient);
    }

    [Fact]
    public async Task BypassingRetrievalSkipsTheGateEntirely()
    {
        // Rollback lever 2: reverting to ungrounded drafting is a deliberate mode, so the gate
        // must not turn it into a system that declines everything.
        var chatClient = new RecordingChatClient();

        var result = await CreatePipeline(chatClient, new FakeKnowledgeStore(),
            new RetrievalOptions { Enabled = false })
            .GenerateDraftAsync(Ticket(), DraftRequest.Initial, CancellationToken.None);

        Assert.IsType<PipelineResult.Success>(result);
        Assert.Equal(1, chatClient.CallCount);
    }

    [Fact]
    public async Task InternalGuidanceIsMarkedDoNotQuoteAndNeverPlacedInAQuotableBlock()
    {
        var chatClient = new RecordingChatClient();
        var store = new FakeKnowledgeStore()
            .ReturnsPolicyScoring(Threshold + 0.5)
            .Returns(KnowledgeCorpus.Internal, new KnowledgeChunk
            {
                Id = "internal-1",
                Title = "GLOBAL > Repair policy",
                Content = "Track the case in the CS: RETURNS/REPAIRS Asana project.",
                Market = "GLOBAL",
                Topic = "repair-policy",
                SourcePath = "knowledge/internal/repair-policy.md",
                Score = 3.0,
            });

        await CreatePipeline(chatClient, store).GenerateDraftAsync(
            Ticket(), DraftRequest.Initial, CancellationToken.None);

        var prompt = chatClient.LastPrompt;
        var internalStart = prompt.IndexOf("<INTERNAL_GUIDANCE", StringComparison.Ordinal);
        Assert.True(internalStart >= 0, "internal guidance was not included at all");
        Assert.Contains("do-not-quote", prompt);
        // The Asana reference must sit inside the do-not-quote block, never before it.
        Assert.True(prompt.IndexOf("Asana", StringComparison.Ordinal) > internalStart);
    }

    [Fact]
    public async Task StreamDeclinesWithoutCallingTheModel()
    {
        var chatClient = new RecordingChatClient();
        var store = new FakeKnowledgeStore().ReturnsPolicyScoring(Threshold - 0.5);

        var chunks = new List<DraftChunk>();
        await foreach (var chunk in CreatePipeline(chatClient, store).StreamDraftAsync(
            Ticket(), DraftRequest.Initial, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        // The stream opens with Started so the draft id is known before anything else. The
        // decline now narrates itself — what was searched, then the gate's verdict — so the
        // agent sees why before the refusal message, and no model call happens.
        Assert.Collection(chunks,
            chunk => Assert.IsType<DraftChunk.Started>(chunk),
            chunk => Assert.IsType<DraftChunk.Searched>(chunk),
            chunk => Assert.Equal(
                CoverageDecision.Declined, Assert.IsType<DraftChunk.Coverage>(chunk).Decision),
            chunk => Assert.IsType<DraftChunk.Insufficient>(chunk));
        Assert.Equal(0, chatClient.CallCount);
    }

    [Fact]
    public async Task StreamNarratesSearchAndCoverageBeforeDrafting()
    {
        var chatClient = new RecordingChatClient();
        var store = new FakeKnowledgeStore().ReturnsPolicyScoring(Threshold + 0.5, market: "DE");

        var chunks = new List<DraftChunk>();
        await foreach (var chunk in CreatePipeline(chatClient, store).StreamDraftAsync(
            Ticket(), DraftRequest.Initial, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        var searched = Assert.Single(chunks.OfType<DraftChunk.Searched>());
        Assert.Equal("GLOBAL", searched.Market);
        Assert.Equal("Fallback", searched.Signal);
        Assert.Equal(1, searched.Policy);
        Assert.Equal(0, searched.Tickets);

        var coverage = Assert.Single(chunks.OfType<DraftChunk.Coverage>());
        Assert.Equal(CoverageDecision.Passed, coverage.Decision);

        // The narration is strictly ordered: nothing about drafting before the gate has
        // spoken, and no text before Drafting announces the model call.
        var kinds = chunks.Select(c => c.GetType()).ToList();
        Assert.True(kinds.IndexOf(typeof(DraftChunk.Searched))
                    < kinds.IndexOf(typeof(DraftChunk.Coverage)));
        Assert.True(kinds.IndexOf(typeof(DraftChunk.Coverage))
                    < kinds.IndexOf(typeof(DraftChunk.Drafting)));
        Assert.True(kinds.IndexOf(typeof(DraftChunk.Drafting))
                    < kinds.IndexOf(typeof(DraftChunk.Delta)));
    }

    [Fact]
    public async Task RetrievalQueriesUseTheNewestCustomerMessageAndSubject()
    {
        var store = new FakeKnowledgeStore().ReturnsPolicyScoring(Threshold + 0.5);
        var ticket = Ticket(
            Customer("My first question about delivery"),
            Agent("We are checking"),
            Customer("Actually, how do I return it?"));

        await CreatePipeline(new RecordingChatClient(), store).GenerateDraftAsync(
            ticket, DraftRequest.Initial, CancellationToken.None);

        var query = store.Queries[0].Text;
        Assert.Contains("Actually, how do I return it?", query);
        Assert.Contains("Order question", query);
        // Older, already-handled messages would dilute the query with resolved topics.
        Assert.DoesNotContain("My first question about delivery", query);
    }

    private static DraftingPipeline CreatePipeline(
        RecordingChatClient chatClient,
        IKnowledgeStore store,
        RetrievalOptions? retrieval = null)
    {
        var options = retrieval ?? new RetrievalOptions { MinimumPolicyScore = Threshold };
        return new DraftingPipeline(
            chatClient,
            new KnowledgeRetriever(store, new GlobalFallbackMarketResolver(), new RetrievalHealth(),
                Options.Create(options)),
            Options.Create(new DraftingOptions()),
            Options.Create(options),
            NullLogger<DraftingPipeline>.Instance);
    }

    private static TicketContext Ticket(params TicketMessage[] messages) => new()
    {
        Id = 42,
        Subject = "Order question",
        Status = "open",
        Customer = new TicketCustomer("Jane Doe", "jane@example.com"),
        Messages = messages.Length > 0 ? messages : [Customer("How do I return an item?")],
    };

    private static TicketMessage Customer(string text) => new()
    {
        Id = 1, FromAgent = false, IsInternalNote = false, Text = text, SenderName = "Jane Doe",
    };

    private static TicketMessage Agent(string text) => new()
    {
        Id = 2, FromAgent = true, IsInternalNote = false, Text = text, SenderName = "Agent",
    };

    private sealed class RecordingChatClient : IChatClient
    {
        public int CallCount { get; private set; }

        public string LastPrompt { get; private set; } = "";

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Record(messages);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "drafted reply")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Record(messages);
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "drafted reply");
        }

        private void Record(IEnumerable<ChatMessage> messages)
        {
            CallCount++;
            LastPrompt = string.Join("\n", messages.Select(m => m.Text));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
