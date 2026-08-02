using Copilot.Knowledge;

namespace Copilot.Tests;

/// <summary>
/// The 402 fallback itself cannot be exercised on demand — triggering it means exhausting a
/// monthly quota — so the observable part is tested here: that degradation is recorded, and
/// therefore reportable, rather than silent.
/// </summary>
public sealed class RetrievalHealthTests
{
    [Fact]
    public void StartsHealthy()
    {
        var health = new RetrievalHealth();

        Assert.False(health.SemanticRankingUnavailable);
        Assert.Null(health.SemanticQuotaExhaustedAt);
        Assert.Equal(0, health.DegradedRetrievals);
    }

    [Fact]
    public void RecordsTheFirstExhaustionAndCountsEveryDegradedRetrieval()
    {
        var health = new RetrievalHealth();

        health.RecordSemanticQuotaExhausted();
        var first = health.SemanticQuotaExhaustedAt;
        health.RecordSemanticQuotaExhausted();
        health.RecordSemanticQuotaExhausted();

        Assert.True(health.SemanticRankingUnavailable);
        Assert.Equal(3, health.DegradedRetrievals);
        // The timestamp answers "since when", so it must not drift forward with each hit.
        Assert.Equal(first, health.SemanticQuotaExhaustedAt);
    }

    [Fact]
    public void RecoversWhenSemanticRankingWorksAgain()
    {
        // Quota resets monthly and billing can be enabled mid-incident. If the flag were
        // permanent the gate would stay down until someone restarted the app, with nothing
        // saying why.
        var health = new RetrievalHealth();
        health.RecordSemanticQuotaExhausted();

        Assert.True(health.RecordSemanticRankingSucceeded());
        Assert.False(health.SemanticRankingUnavailable);
        Assert.Null(health.SemanticQuotaExhaustedAt);

        // The count survives recovery: it is the record that something happened.
        Assert.Equal(1, health.DegradedRetrievals);
        // Only the call that ends a degraded period reports true.
        Assert.False(health.RecordSemanticRankingSucceeded());
    }

    [Fact]
    public void OnlyPolicyIsRerankedSoOneDraftSpendsOneMeteredQuery()
    {
        // The whole point of the fix: four corpora are retrieved, one is reranked. If this
        // ever flips back, the free allowance drops from ~1,000 drafts a month to ~250.
        var policy = new KnowledgeQuery
        {
            Text = "q", Market = "DE", Corpus = KnowledgeCorpus.Policy, Rerank = true,
        };
        var others = new[] { KnowledgeCorpus.Template, KnowledgeCorpus.Internal, KnowledgeCorpus.Ticket }
            .Select(corpus => new KnowledgeQuery { Text = "q", Market = "DE", Corpus = corpus });

        Assert.True(policy.Rerank);
        Assert.All(others, query => Assert.False(query.Rerank));
    }
}
