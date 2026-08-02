using Copilot.Knowledge;

namespace Copilot.Pipeline;

/// <summary>
/// Everything retrieval found for one draft, kept in separate buckets rather than one merged
/// list. <see cref="Internal"/> is deliberately its own field: it must inform what the agent
/// decides and never appear in what the customer reads, and a single list would make that
/// distinction a matter of remembering to filter.
/// </summary>
public sealed record RetrievedContext
{
    public required MarketResolution Market { get; init; }

    public IReadOnlyList<KnowledgeChunk> Policy { get; init; } = [];

    public IReadOnlyList<KnowledgeChunk> Templates { get; init; } = [];

    public IReadOnlyList<KnowledgeChunk> Tickets { get; init; } = [];

    /// <summary>Never quotable. Held apart from every customer-facing bucket.</summary>
    public IReadOnlyList<KnowledgeChunk> Internal { get; init; } = [];

    /// <summary>Retrieval was switched off; the draft falls back to ticket content alone.</summary>
    public bool Bypassed { get; init; }

    /// <summary>
    /// Results came back unranked because semantic reranking was unavailable. The scores are
    /// then fusion scores on a different scale that do not separate covered from uncovered
    /// questions, so the relevance gate has nothing meaningful to threshold.
    /// </summary>
    public bool RankingUnavailable { get; init; }

    public double BestPolicyScore => Policy.Count == 0 ? 0 : Policy.Max(chunk => chunk.Score);

    public static RetrievedContext None(MarketResolution market) =>
        new() { Market = market, Bypassed = true };
}
