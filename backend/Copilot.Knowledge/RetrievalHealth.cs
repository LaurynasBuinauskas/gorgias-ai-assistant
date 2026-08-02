namespace Copilot.Knowledge;

/// <summary>
/// Records that retrieval is running degraded, so it can be reported rather than discovered.
///
/// The incident this exists for: the semantic reranking quota ran out, Search began returning
/// 402, and the first anyone knew was production returning 500s. Running degraded is
/// acceptable; running degraded invisibly is not.
/// </summary>
public sealed class RetrievalHealth
{
    private long _semanticQuotaExhaustedAtTicks;
    private int _degradedRetrievals;

    /// <summary>True once a semantic query has been refused for quota reasons.</summary>
    public bool SemanticRankingUnavailable => Volatile.Read(ref _semanticQuotaExhaustedAtTicks) > 0;

    public DateTimeOffset? SemanticQuotaExhaustedAt
    {
        get
        {
            var ticks = Volatile.Read(ref _semanticQuotaExhaustedAtTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    /// <summary>How many retrievals have fallen back to unranked results since startup.</summary>
    public int DegradedRetrievals => Volatile.Read(ref _degradedRetrievals);

    public void RecordSemanticQuotaExhausted()
    {
        Interlocked.CompareExchange(
            ref _semanticQuotaExhaustedAtTicks, DateTimeOffset.UtcNow.UtcTicks, 0);
        Interlocked.Increment(ref _degradedRetrievals);
    }
}
