using System.Text.Json;
using Copilot.Domain;
using Copilot.Knowledge;
using Copilot.Pipeline;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Copilot.Evals;

public sealed record CaseResult(
    EvalCase Case,
    DraftOutcome Outcome,
    IReadOnlyList<AssertionResult> Assertions)
{
    public bool AssertionsHeld => Assertions.All(a => a.Passed);

    /// <summary>A case marked <c>expect_failure</c> passes precisely when its assertions do not.</summary>
    public bool Passed => Case.ExpectFailure ? !AssertionsHeld : AssertionsHeld;
}

/// <summary>
/// Runs cases through the real pipeline against the real index.
///
/// Only two things are substituted: the ticket, which comes from a synthetic fixture instead
/// of Gorgias, and the market, which each case pins. Everything under test — retrieval,
/// filtering, the relevance gate, prompt assembly, the model — is production code. A harness
/// that mocked the pipeline would only prove the harness works.
/// </summary>
public sealed class EvalRunner(
    IChatClient chatClient,
    IKnowledgeStore store,
    RetrievalOptions retrievalOptions,
    DraftingOptions draftingOptions)
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    public async Task<CaseResult> RunAsync(
        EvalCase testCase,
        string fixturesDirectory,
        CancellationToken cancellationToken)
    {
        var ticket = LoadFixture(fixturesDirectory, testCase.Fixture);
        var counting = new CountingChatClient(chatClient);

        var retriever = new KnowledgeRetriever(
            store,
            new FixedMarketResolver(testCase.Market),
            Options.Create(retrievalOptions));

        var pipeline = new DraftingPipeline(
            counting,
            retriever,
            Options.Create(draftingOptions),
            Options.Create(retrievalOptions),
            NullLogger<DraftingPipeline>.Instance);

        var request = new DraftRequest
        {
            Instruction = testCase.Instruction,
        };

        var result = await pipeline.GenerateDraftAsync(ticket, request, cancellationToken);

        var outcome = result switch
        {
            PipelineResult.Success success => new DraftOutcome
            {
                Outcome = "drafted",
                Body = success.Draft.Body,
                Citations = success.Draft.Citations,
                ModelCalls = counting.Calls,
            },
            PipelineResult.InsufficientKnowledge insufficient => new DraftOutcome
            {
                Outcome = "insufficient_data",
                Body = insufficient.Message,
                ModelCalls = counting.Calls,
            },
            _ => throw new InvalidOperationException($"Unhandled result: {result.GetType().Name}"),
        };

        return new CaseResult(testCase, outcome, Assertions.Evaluate(testCase, outcome));
    }

    private static TicketContext LoadFixture(string directory, string name)
    {
        var path = Path.Combine(directory, name);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"fixture not found: {path}");
        }

        return JsonSerializer.Deserialize<TicketContext>(File.ReadAllText(path), s_json)
               ?? throw new InvalidOperationException($"fixture is empty: {path}");
    }

    private sealed class FixedMarketResolver(string market) : IMarketResolver
    {
        public MarketResolution Resolve(TicketContext ticket) =>
            new(market, MarketSignal.Fallback);
    }

    /// <summary>
    /// Counts model calls so a case can assert a refusal happened *before* the model, not
    /// after. Without this, "returned insufficient_data" would pass even if the pipeline had
    /// called the model and discarded its answer — which is the expensive failure.
    /// </summary>
    private sealed class CountingChatClient(IChatClient inner) : IChatClient
    {
        public int Calls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return inner.GetResponseAsync(messages, options, cancellationToken);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return inner.GetStreamingResponseAsync(messages, options, cancellationToken);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            inner.GetService(serviceType, serviceKey);

        public void Dispose() => inner.Dispose();
    }
}
