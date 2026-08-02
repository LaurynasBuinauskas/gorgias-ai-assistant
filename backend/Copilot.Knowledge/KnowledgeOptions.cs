namespace Copilot.Knowledge;

/// <summary>Azure AI Search connection settings.</summary>
public sealed class KnowledgeOptions
{
    public const string SectionName = "Knowledge";

    public string Endpoint { get; set; } = "";

    /// <summary>
    /// The concrete index, not an alias. Aliases exist only on preview api-versions — they
    /// cannot even be resolved at query time on a stable one — so pinning the index name here
    /// keeps the request path on a supported contract and makes rollback an app-setting
    /// change rather than a deploy. See `open-questions.md` D-4.
    /// </summary>
    public string IndexName { get; set; } = "knowledge-v1";

    /// <summary>
    /// Ticket exemplars live in their own index, not alongside policy.
    ///
    /// The original design put all four corpora in one index so cross-corpus scores would be
    /// comparable — but retrieval never compares them: each corpus is fetched by its own
    /// filtered query into its own bucket. With that reason gone, separating buys things that
    /// matter:
    ///
    /// * **Personal data is isolated.** Tickets are the only customer-derived corpus, and
    ///   redaction is the sole control protecting it. If redaction ever leaks, this index is
    ///   dropped without touching policy.
    /// * **Erasure is provable.** "Delete the ticket index" is complete by construction; a
    ///   filtered delete inside a shared index has to be trusted.
    /// * **Lifecycles differ.** Policy reindexes on change, tickets monthly, and at ~15,000
    ///   chunks against 400 the tickets would otherwise dominate every policy rebuild.
    ///
    /// Same service, so no extra cost. The semantic quota is per service and is not isolated.
    /// </summary>
    public string TicketIndexName { get; set; } = "tickets-v1";

    /// <summary>
    /// Local development only. Production authenticates as the App Service managed identity,
    /// which holds Search Index Data Reader and therefore cannot write. Leave empty to use
    /// that path.
    /// </summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Semantic configuration defined by the index schema.</summary>
    public string SemanticConfiguration { get; set; } = "policy-semantic";

    /// <summary>
    /// Semantic reranking. On by default because it materially improves ranking, but it is
    /// **metered**: the free tier allows 1,000 queries a month and each draft spends four, one
    /// per corpus. Exhausting the quota makes Search return 402 and every draft fail, so this
    /// exists as a switch that restores service in one app setting while billing is sorted.
    ///
    /// Retrieval still works without it — BM25 and vector results are fused, just not reranked.
    /// </summary>
    public bool UseSemanticRanking { get; set; } = true;

    /// <summary>Candidates pulled from the vector index before fusion and reranking.</summary>
    public int VectorCandidates { get; set; } = 20;
}
