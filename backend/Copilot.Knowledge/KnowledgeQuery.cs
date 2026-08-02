namespace Copilot.Knowledge;

/// <summary>Which corpus a retrieval targets. Each has a different unit of meaning.</summary>
public enum KnowledgeCorpus
{
    Policy,
    Template,
    Internal,
    Ticket,
}

/// <summary>
/// Whether a chunk may reach a customer. Internal procedure informs what the agent decides
/// and is never quoted, so this is a hard filter rather than a hint.
/// </summary>
public enum KnowledgeExposure
{
    Customer,
    Internal,
}

/// <summary>
/// A single filtered retrieval. Market is a correctness boundary, not a ranking preference:
/// answering a German customer with US return terms is a wrong answer with legal weight, and
/// a good semantic score makes it more dangerous rather than less. It is therefore applied as
/// a predicate evaluated with the query, never as a post-filter over the results.
/// </summary>
public sealed record KnowledgeQuery
{
    public required string Text { get; init; }

    /// <summary>Market code, e.g. "DE". <c>GLOBAL</c> content is always included alongside it.</summary>
    public required string Market { get; init; }

    public required KnowledgeCorpus Corpus { get; init; }

    public KnowledgeExposure Exposure { get; init; } = KnowledgeExposure.Customer;

    public int TopK { get; init; } = 4;

    /// <summary>
    /// Whether to spend a semantic rerank on this query.
    ///
    /// Metered, so it is requested per query rather than applied to all of them. Only the
    /// policy corpus needs it: the relevance gate scores policy and nothing else, and the
    /// other corpora are selected rather than ranked. Reranking all four quadrupled the spend
    /// for no gain the gate could use.
    /// </summary>
    public bool Rerank { get; init; }
}
