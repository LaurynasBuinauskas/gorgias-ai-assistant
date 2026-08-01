namespace Copilot.Knowledge;

/// <summary>
/// Retrieval over the knowledge corpora. The seam that keeps the storage choice reversible —
/// nothing above this interface knows the index is Azure AI Search, and no Azure SDK type
/// appears in any signature here.
/// </summary>
public interface IKnowledgeStore
{
    /// <summary>
    /// Returns the best chunks for a query, filtered to the requested market and exposure.
    /// An empty result is a normal outcome, not an error: it is what the relevance gate acts
    /// on when the corpus does not cover a question.
    /// </summary>
    Task<IReadOnlyList<KnowledgeChunk>> RetrieveAsync(
        KnowledgeQuery query,
        CancellationToken cancellationToken);
}
