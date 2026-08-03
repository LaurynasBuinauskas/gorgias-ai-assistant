using Copilot.Domain;
using Copilot.Knowledge;
using Copilot.Pipeline;
using Microsoft.Extensions.Options;

namespace Copilot.Tests;

/// <summary>
/// Exemplars must never be able to take drafting down with them.
///
/// They are style references. Policy is grounding. A draft written from policy alone is what
/// this system did until exemplars were enabled and is entirely serviceable, so a failure to
/// retrieve them should cost the exemplars and nothing else.
///
/// The case that made this concrete: pointing `TicketIndexName` back at `tickets-v1` — which
/// the runbook once described as a one-setting rollback — makes Search reject every exemplar
/// query, because that index has no `questionVector`. Before this, the precautionary step
/// would have 500'd every draft.
/// </summary>
public sealed class ExemplarResilienceTests
{
    [Fact]
    public async Task AFailingExemplarQueryStillProducesPolicyGroundedContext()
    {
        var health = new RetrievalHealth();
        var retriever = Build(new ExemplarFailingStore(), health);

        var context = await retriever.RetrieveAsync(Ticket(), CancellationToken.None);

        Assert.NotEmpty(context.Policy);
        Assert.Empty(context.Tickets);
    }

    [Fact]
    public async Task TheFailureIsRecordedRatherThanSwallowed()
    {
        var health = new RetrievalHealth();
        var retriever = Build(new ExemplarFailingStore(), health);

        await retriever.RetrieveAsync(Ticket(), CancellationToken.None);

        Assert.True(health.ExemplarsUnavailable);
        Assert.Equal(1, health.ExemplarFailures);
        Assert.Contains("questionVector", health.ExemplarFailureReason);
        Assert.NotNull(health.ExemplarsFailedAt);
    }

    [Fact]
    public async Task RecoveryClearsTheDegradedState()
    {
        var health = new RetrievalHealth();
        await Build(new ExemplarFailingStore(), health).RetrieveAsync(Ticket(), CancellationToken.None);
        Assert.True(health.ExemplarsUnavailable);

        await Build(new WorkingStore(), health).RetrieveAsync(Ticket(), CancellationToken.None);

        Assert.False(health.ExemplarsUnavailable);
        // The count is a record of what happened and is deliberately not reset.
        Assert.Equal(1, health.ExemplarFailures);
    }

    [Fact]
    public async Task APolicyFailureStillFailsTheDraft()
    {
        // The other half of the rule. Drafting without grounding is the failure this whole
        // design exists to prevent, so that exception must keep propagating.
        var retriever = Build(new PolicyFailingStore(), new RetrievalHealth());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => retriever.RetrieveAsync(Ticket(), CancellationToken.None));
    }

    private static KnowledgeRetriever Build(IKnowledgeStore store, RetrievalHealth health) =>
        new(store,
            new FixedMarket(),
            health,
            Options.Create(new RetrievalOptions { TicketTopK = 3 }));

    private static TicketContext Ticket() => new()
    {
        Id = 1,
        Status = "open",
        Subject = "Returning an item",
        Messages =
        [
            new TicketMessage
            {
                Id = 1,
                FromAgent = false,
                IsInternalNote = false,
                SenderName = "Sam",
                Text = "How long do I have to return a bag?",
            },
        ],
    };

    private sealed class FixedMarket : IMarketResolver
    {
        public MarketResolution Resolve(TicketContext ticket) =>
            new("GLOBAL", MarketSignal.Fallback);
    }

    private static KnowledgeChunk Chunk(KnowledgeCorpus corpus) => new()
    {
        Id = $"{corpus}-1",
        Title = "chunk",
        Content = "Returns are accepted within 30 days.",
        Market = "GLOBAL",
        Topic = "returns",
        SourcePath = "knowledge/policy/GLOBAL/returns.md",
        Score = 2.5,
    };

    private sealed class ExemplarFailingStore : IKnowledgeStore
    {
        public Task<IReadOnlyList<KnowledgeChunk>> RetrieveAsync(
            KnowledgeQuery query, CancellationToken cancellationToken) =>
            query.Corpus == KnowledgeCorpus.Ticket
                ? throw new InvalidOperationException(
                    "Unknown field 'questionVector' in vector field list.")
                : Task.FromResult<IReadOnlyList<KnowledgeChunk>>([Chunk(query.Corpus)]);
    }

    private sealed class PolicyFailingStore : IKnowledgeStore
    {
        public Task<IReadOnlyList<KnowledgeChunk>> RetrieveAsync(
            KnowledgeQuery query, CancellationToken cancellationToken) =>
            query.Corpus == KnowledgeCorpus.Policy
                ? throw new InvalidOperationException("policy index unavailable")
                : Task.FromResult<IReadOnlyList<KnowledgeChunk>>([]);
    }

    private sealed class WorkingStore : IKnowledgeStore
    {
        public Task<IReadOnlyList<KnowledgeChunk>> RetrieveAsync(
            KnowledgeQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<KnowledgeChunk>>([Chunk(query.Corpus)]);
    }
}
