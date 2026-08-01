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
    /// Local development only. Production authenticates as the App Service managed identity,
    /// which holds Search Index Data Reader and therefore cannot write. Leave empty to use
    /// that path.
    /// </summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Semantic configuration defined by the index schema.</summary>
    public string SemanticConfiguration { get; set; } = "policy-semantic";

    /// <summary>Candidates pulled from the vector index before fusion and reranking.</summary>
    public int VectorCandidates { get; set; } = 20;
}
