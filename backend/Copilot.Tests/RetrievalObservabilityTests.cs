using System.Runtime.CompilerServices;
using Copilot.Domain;
using Copilot.Knowledge;
using Copilot.Pipeline;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Copilot.Tests;

/// <summary>
/// Logs must be able to explain a draft after the fact without becoming a second copy of the
/// data. Both halves are asserted: the retrieved context is reconstructable from the log
/// alone, and no customer or policy text appears in it.
/// </summary>
public sealed class RetrievalObservabilityTests
{
    private const string CustomerText = "Hi, my name is Sammy Nguyen and my order is #US#14532.";
    private const string PolicyText = "Returns are accepted within 30 days of delivery.";

    [Fact]
    public async Task TheRetrievedContextCanBeReconstructedFromLogsAlone()
    {
        var (logs, result) = await RunAsync();

        var draftId = Assert.IsType<PipelineResult.Success>(result).Draft.DraftId;
        var lines = logs.Lines.Where(line => line.Contains(draftId, StringComparison.Ordinal)).ToList();

        Assert.NotEmpty(lines);
        // Which market, and why it was chosen.
        Assert.Contains(lines, l => l.Contains("market DE") && l.Contains("Fallback"));
        // Which chunks, with their scores, per corpus.
        Assert.Contains(lines, l => l.Contains("policy-1:2.900") && l.Contains("internal-1:3.000"));
        // The gate's decision and the numbers behind it.
        Assert.Contains(lines, l => l.Contains("gate passed") && l.Contains("2.900"));
        // How large the prompt was.
        Assert.Contains(lines, l => l.Contains("prompt") && l.Contains("tokens"));
    }

    [Fact]
    public async Task EveryLogLineForADraftIsKeyedByTheSameDraftId()
    {
        var (logs, result) = await RunAsync();

        var draftId = Assert.IsType<PipelineResult.Success>(result).Draft.DraftId;
        var pipelineLines = logs.Lines.Where(l => l.StartsWith("Draft ", StringComparison.Ordinal));

        Assert.All(pipelineLines, line => Assert.Contains(draftId, line, StringComparison.Ordinal));
    }

    [Fact]
    public async Task NoCustomerOrPolicyTextEverReachesTheLog()
    {
        var (logs, _) = await RunAsync();
        var everything = string.Join("\n", logs.Lines);

        Assert.DoesNotContain("Sammy Nguyen", everything, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#US#14532", everything, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(PolicyText, everything, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("drafted reply", everything, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheStreamedDraftIdMatchesTheOneInTheLogs()
    {
        // The two paths once minted independent ids, so feedback quoting a streamed draft id
        // matched nothing in the logs. This is what stops that returning.
        var logs = new CapturingLoggerProvider();
        var pipeline = CreatePipeline(logs);

        var streamed = "";
        await foreach (var chunk in pipeline.StreamDraftAsync(
            Ticket(), DraftRequest.Initial, CancellationToken.None))
        {
            if (chunk is DraftChunk.Started started)
            {
                streamed = started.DraftId;
            }
        }

        Assert.NotEqual("", streamed);
        Assert.Contains(logs.Lines, line => line.Contains(streamed, StringComparison.Ordinal));
    }

    private static async Task<(CapturingLoggerProvider Logs, PipelineResult Result)> RunAsync()
    {
        var logs = new CapturingLoggerProvider();
        var result = await CreatePipeline(logs).GenerateDraftAsync(
            Ticket(), DraftRequest.Initial, CancellationToken.None);
        return (logs, result);
    }

    private static DraftingPipeline CreatePipeline(CapturingLoggerProvider logs)
    {
        var store = new FakeKnowledgeStore()
            .Returns(KnowledgeCorpus.Policy, Chunk("policy-1", "DE", 2.9, PolicyText))
            .Returns(KnowledgeCorpus.Internal, Chunk("internal-1", "GLOBAL", 3.0, "Asana project."));

        var options = new RetrievalOptions { MinimumPolicyScore = 1.6 };
        return new DraftingPipeline(
            new StubChatClient(),
            new KnowledgeRetriever(store, new FixedMarketResolver("DE"), new RetrievalHealth(),
                Options.Create(options)),
            Options.Create(new DraftingOptions()),
            Options.Create(options),
            new LoggerFactory([logs]).CreateLogger<DraftingPipeline>());
    }

    private static KnowledgeChunk Chunk(string id, string market, double score, string content) => new()
    {
        Id = id,
        Title = $"{market} > Section",
        Content = content,
        Market = market,
        Topic = "shipping-and-returns",
        SourcePath = $"knowledge/policy/{market}/shipping-and-returns.md",
        Score = score,
    };

    private static TicketContext Ticket() => new()
    {
        Id = 42,
        Subject = "Return Item",
        Status = "open",
        Customer = new TicketCustomer("Sammy Nguyen", "sammy@example.com"),
        Messages =
        [
            new TicketMessage
            {
                Id = 1, FromAgent = false, IsInternalNote = false,
                Text = CustomerText, SenderName = "Sammy Nguyen",
            },
        ],
    };

    private sealed class FixedMarketResolver(string market) : IMarketResolver
    {
        public MarketResolution Resolve(TicketContext ticket) =>
            new(market, MarketSignal.Fallback);
    }

    private sealed class StubChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "drafted reply")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "drafted reply");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<string> Lines { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Capturing(Lines);

        public void Dispose()
        {
        }

        private sealed class Capturing(List<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                lines.Add(formatter(state, exception));
        }
    }
}
