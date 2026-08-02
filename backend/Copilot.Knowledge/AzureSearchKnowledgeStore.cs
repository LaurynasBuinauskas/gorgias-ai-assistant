using Azure;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Copilot.Knowledge;

/// <summary>
/// Hybrid retrieval over Azure AI Search: BM25 and vector search fused, then reranked
/// semantically. Policy questions are full of exact tokens ("30 days", "AEPD", "REPAIR1")
/// where pure vector search underperforms, alongside paraphrased questions where it excels —
/// which is why both halves run and neither is dropped.
/// </summary>
public sealed class AzureSearchKnowledgeStore : IKnowledgeStore
{
    private readonly SearchClient _client;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddings;
    private readonly KnowledgeOptions _options;
    private readonly ILogger<AzureSearchKnowledgeStore> _logger;

    public AzureSearchKnowledgeStore(
        IOptions<KnowledgeOptions> options,
        IEmbeddingGenerator<string, Embedding<float>> embeddings,
        ILogger<AzureSearchKnowledgeStore> logger)
    {
        _options = options.Value;
        _embeddings = embeddings;
        _logger = logger;

        var endpoint = new Uri(_options.Endpoint);
        _client = string.IsNullOrWhiteSpace(_options.ApiKey)
            ? new SearchClient(endpoint, _options.IndexName, new DefaultAzureCredential())
            : new SearchClient(endpoint, _options.IndexName, new AzureKeyCredential(_options.ApiKey));
    }

    public async Task<IReadOnlyList<KnowledgeChunk>> RetrieveAsync(
        KnowledgeQuery query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.Text))
        {
            return [];
        }

        var embedding = await _embeddings.GenerateVectorAsync(query.Text, cancellationToken: cancellationToken);

        var options = new SearchOptions
        {
            Filter = BuildFilter(query),
            Size = query.TopK,
            VectorSearch = new VectorSearchOptions
            {
                Queries =
                {
                    new VectorizedQuery(embedding)
                    {
                        KNearestNeighborsCount = _options.VectorCandidates,
                        Fields = { "contentVector" },
                    },
                },
            },
            Select = { "id", "title", "content", "market", "topic", "sourcePath" },
        };

        if (_options.UseSemanticRanking)
        {
            options.QueryType = SearchQueryType.Semantic;
            options.SemanticSearch = new SemanticSearchOptions
            {
                SemanticConfigurationName = _options.SemanticConfiguration,
            };
        }

        var response = await _client.SearchAsync<SearchDocument>(query.Text, options, cancellationToken);

        var chunks = new List<KnowledgeChunk>(query.TopK);
        await foreach (var result in response.Value.GetResultsAsync().WithCancellation(cancellationToken))
        {
            chunks.Add(ToChunk(result));
        }

        _logger.LogDebug(
            "Retrieved {Count} {Corpus} chunk(s) for market {Market}",
            chunks.Count, query.Corpus, query.Market);

        return chunks;
    }

    /// <summary>
    /// Market and exposure are predicates evaluated with the query, so a filtered-out chunk
    /// never competes for a slot. Applying them afterwards would silently shrink the result
    /// set and could return nothing while relevant in-market content existed.
    /// </summary>
    private static string BuildFilter(KnowledgeQuery query)
    {
        var market = Escape(query.Market);
        var corpus = query.Corpus.ToString().ToLowerInvariant();
        var exposure = query.Exposure.ToString().ToLowerInvariant();

        return $"corpus eq '{corpus}' and exposure eq '{exposure}' " +
               $"and (market eq '{market}' or market eq 'GLOBAL')";
    }

    private static string Escape(string value) => value.Replace("'", "''");

    private static KnowledgeChunk ToChunk(SearchResult<SearchDocument> result) => new()
    {
        Id = Field(result.Document, "id"),
        Title = Field(result.Document, "title"),
        Content = Field(result.Document, "content"),
        Market = Field(result.Document, "market"),
        Topic = Field(result.Document, "topic"),
        SourcePath = Field(result.Document, "sourcePath"),
        // The reranker score is the meaningful one; fall back when semantic ranking did not
        // apply, so a caller comparing scores never sees a silent zero.
        Score = result.SemanticSearch?.RerankerScore ?? result.Score ?? 0,
    };

    private static string Field(SearchDocument document, string name) =>
        document.TryGetValue(name, out var value) ? value?.ToString() ?? "" : "";
}
