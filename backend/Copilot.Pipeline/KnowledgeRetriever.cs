using Copilot.Domain;
using Copilot.Knowledge;
using Microsoft.Extensions.Options;

namespace Copilot.Pipeline;

/// <summary>
/// Gathers the knowledge a draft is allowed to use, per corpus, filtered to the ticket's
/// market. Separated from the pipeline so the retrieval policy — what to fetch and how much —
/// is readable in one place rather than interleaved with generation.
/// </summary>
public sealed class KnowledgeRetriever(
    IKnowledgeStore store,
    IMarketResolver marketResolver,
    RetrievalHealth health,
    IOptions<RetrievalOptions> options)
{
    private readonly RetrievalOptions _options = options.Value;

    public async Task<RetrievedContext> RetrieveAsync(
        TicketContext ticket,
        CancellationToken cancellationToken)
    {
        var market = marketResolver.Resolve(ticket);

        if (!_options.Enabled)
        {
            return RetrievedContext.None(market);
        }

        var query = BuildQuery(ticket);
        if (string.IsNullOrWhiteSpace(query))
        {
            return new RetrievedContext { Market = market };
        }

        // Issued together: they are independent reads and the draft waits on the slowest.
        //
        // Only policy is reranked. The gate scores policy and nothing else, so reranking the
        // other three spent metered queries on rankings nothing reads.
        //
        // Exemplars stay unranked for a second, measured reason: reranking them is *worse*.
        // Over 60 held-out paraphrased questions against the live index, semantic reranking on
        // the customer's question moved recall@3 from 75% to 72% and ranked the right exchange
        // lower on 15 queries against higher on 5 — while costing an extra metered query per
        // draft, halving what the free allowance covers. See `tools/evals/exemplar_rerank.py`.
        var policy = Fetch(query, market, KnowledgeCorpus.Policy, KnowledgeExposure.Customer,
            _options.PolicyTopK, cancellationToken, rerank: true);
        var templates = Fetch(query, market, KnowledgeCorpus.Template, KnowledgeExposure.Customer,
            _options.TemplateTopK, cancellationToken);
        var tickets = Fetch(query, market, KnowledgeCorpus.Ticket, KnowledgeExposure.Customer,
            _options.TicketTopK, cancellationToken);
        var internals = Fetch(query, market, KnowledgeCorpus.Internal, KnowledgeExposure.Internal,
            _options.InternalTopK, cancellationToken);

        await Task.WhenAll(policy, templates, tickets, internals);

        return new RetrievedContext
        {
            Market = market,
            RankingUnavailable = health.SemanticRankingUnavailable,
            Policy = policy.Result,
            Templates = templates.Result,
            Tickets = tickets.Result,
            Internal = internals.Result,
        };
    }

    private Task<IReadOnlyList<KnowledgeChunk>> Fetch(
        string query,
        MarketResolution market,
        KnowledgeCorpus corpus,
        KnowledgeExposure exposure,
        int topK,
        CancellationToken cancellationToken,
        bool rerank = false) =>
        topK <= 0
            ? Task.FromResult<IReadOnlyList<KnowledgeChunk>>([])
            : store.RetrieveAsync(
                new KnowledgeQuery
                {
                    Text = query,
                    Market = market.Market,
                    Corpus = corpus,
                    Exposure = exposure,
                    TopK = topK,
                    Rerank = rerank,
                },
                cancellationToken);

    /// <summary>
    /// The newest customer message plus the subject. Older messages describe what has already
    /// been handled; the reply is answering the latest question, so retrieving on the whole
    /// thread would dilute the query with resolved topics.
    /// </summary>
    private static string BuildQuery(TicketContext ticket)
    {
        var newest = ticket.Messages
            .LastOrDefault(m => m is { FromAgent: false, IsInternalNote: false });

        return string.Join(" ", new[] { ticket.Subject, newest?.Text }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
    }
}
