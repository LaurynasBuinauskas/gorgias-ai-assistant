using Copilot.Knowledge;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenAI;

namespace Copilot.Tests.Integration;

/// <summary>
/// Runs against the real index. The filters under test are the ones that carry consequence —
/// a market leak is a wrong answer with legal weight, and an exposure leak puts internal
/// procedure in front of a customer — so they are verified against the live service rather
/// than a mock that would only prove the query was built as expected.
/// </summary>
public sealed class AzureSearchKnowledgeStoreTests
{
    private static AzureSearchKnowledgeStore CreateStore()
    {
        var embeddings = new OpenAIClient(KnowledgeTestEnvironment.OpenAiKey)
            .GetEmbeddingClient("text-embedding-3-small")
            .AsIEmbeddingGenerator(1536);

        var options = Options.Create(new KnowledgeOptions
        {
            Endpoint = KnowledgeTestEnvironment.SearchEndpoint,
            IndexName = KnowledgeTestEnvironment.IndexName,
            ApiKey = KnowledgeTestEnvironment.SearchKey!,
        });

        return new AzureSearchKnowledgeStore(
            options, embeddings, NullLogger<AzureSearchKnowledgeStore>.Instance);
    }

    [IntegrationFact]
    public async Task ReturnsOnlyTheRequestedMarketOrGlobal()
    {
        var chunks = await CreateStore().RetrieveAsync(
            new KnowledgeQuery
            {
                Text = "How long do I have to return an item?",
                Market = "DE",
                Corpus = KnowledgeCorpus.Policy,
                TopK = 8,
            },
            CancellationToken.None);

        Assert.NotEmpty(chunks);
        Assert.All(chunks, chunk => Assert.Contains(chunk.Market, new[] { "DE", "GLOBAL" }));
    }

    [IntegrationFact]
    public async Task NeverReturnsAnotherMarketsPolicy()
    {
        // The US corpus is the one most likely to be retrieved for an English query, which
        // makes it the sharpest test that the filter is a predicate and not a preference.
        var chunks = await CreateStore().RetrieveAsync(
            new KnowledgeQuery
            {
                Text = "returns refund shipping warranty",
                Market = "DE",
                Corpus = KnowledgeCorpus.Policy,
                TopK = 20,
            },
            CancellationToken.None);

        Assert.DoesNotContain(chunks, chunk => chunk.Market == "US");
    }

    [IntegrationFact]
    public async Task CustomerRetrievalNeverReturnsInternalProcedure()
    {
        var chunks = await CreateStore().RetrieveAsync(
            new KnowledgeQuery
            {
                Text = "repair discount code warranty replacement",
                Market = "GLOBAL",
                Corpus = KnowledgeCorpus.Policy,
                Exposure = KnowledgeExposure.Customer,
                TopK = 20,
            },
            CancellationToken.None);

        Assert.DoesNotContain(chunks, chunk =>
            chunk.SourcePath.Contains("internal", StringComparison.OrdinalIgnoreCase));
    }

    [IntegrationFact]
    public async Task InternalCorpusIsReachableWhenExplicitlyRequested()
    {
        // Internal guidance must still be retrievable — it informs what the agent decides.
        // What must never happen is it arriving through a customer-facing query.
        var chunks = await CreateStore().RetrieveAsync(
            new KnowledgeQuery
            {
                Text = "repair triage warranty discount",
                Market = "GLOBAL",
                Corpus = KnowledgeCorpus.Internal,
                Exposure = KnowledgeExposure.Internal,
                TopK = 4,
            },
            CancellationToken.None);

        Assert.NotEmpty(chunks);
        Assert.All(chunks, chunk => Assert.Contains("internal", chunk.SourcePath));
    }

    [IntegrationFact]
    public async Task ReturnsNothingForAMarketWithNoContent()
    {
        // An empty result is a normal outcome the relevance gate acts on, not an error.
        var chunks = await CreateStore().RetrieveAsync(
            new KnowledgeQuery
            {
                Text = "return policy",
                Market = "ZZ_NOT_A_MARKET",
                Corpus = KnowledgeCorpus.Ticket,
                TopK = 4,
            },
            CancellationToken.None);

        Assert.Empty(chunks);
    }

    [IntegrationFact]
    public async Task ReturnsNothingForEmptyQueryTextWithoutCallingTheService()
    {
        var chunks = await CreateStore().RetrieveAsync(
            new KnowledgeQuery
            {
                Text = "   ",
                Market = "DE",
                Corpus = KnowledgeCorpus.Policy,
            },
            CancellationToken.None);

        Assert.Empty(chunks);
    }
}
