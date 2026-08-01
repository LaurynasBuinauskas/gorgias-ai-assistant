using Copilot.Knowledge;

namespace Copilot.Tests;

/// <summary>
/// Returns whatever the test asks for, per corpus, and records the queries it was given.
/// Lets the relevance gate be tested at exact score boundaries, which is not something the
/// live index can be made to do on demand.
/// </summary>
public sealed class FakeKnowledgeStore : IKnowledgeStore
{
    private readonly Dictionary<KnowledgeCorpus, IReadOnlyList<KnowledgeChunk>> _byCorpus = [];

    public List<KnowledgeQuery> Queries { get; } = [];

    public FakeKnowledgeStore Returns(KnowledgeCorpus corpus, params KnowledgeChunk[] chunks)
    {
        _byCorpus[corpus] = chunks;
        return this;
    }

    /// <summary>Policy chunks at a chosen score — the input the gate actually decides on.</summary>
    public FakeKnowledgeStore ReturnsPolicyScoring(double score, string market = "GLOBAL") =>
        Returns(KnowledgeCorpus.Policy, Chunk("policy-1", score, market));

    public static KnowledgeChunk Chunk(string id, double score, string market = "GLOBAL") => new()
    {
        Id = id,
        Title = $"{market} > Shipping and Returns",
        Content = "Returns are accepted within 30 days of delivery.",
        Market = market,
        Topic = "shipping-and-returns",
        SourcePath = $"knowledge/policy/{market}/shipping-and-returns.md",
        Score = score,
    };

    public Task<IReadOnlyList<KnowledgeChunk>> RetrieveAsync(
        KnowledgeQuery query,
        CancellationToken cancellationToken)
    {
        Queries.Add(query);
        return Task.FromResult(
            _byCorpus.TryGetValue(query.Corpus, out var chunks) ? chunks : []);
    }
}
