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
    private long _exemplarsFailedAtTicks;
    private int _exemplarFailures;
    private string _exemplarFailure = "";

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

    /// <summary>
    /// Clears the degraded state after a semantic query succeeds again.
    ///
    /// Without this the flag is permanent for the process lifetime: quota resets on the first
    /// of the month, or billing is enabled, and the gate stays down anyway until someone
    /// happens to restart the app — with nothing to indicate why. Recovery should not require
    /// a human noticing.
    ///
    /// <see cref="DegradedRetrievals"/> is deliberately not reset. It is a record of what
    /// happened, and zeroing it would erase the evidence that anything did.
    /// </summary>
    /// <returns>True if this call ended a degraded period.</returns>
    public bool RecordSemanticRankingSucceeded() =>
        Interlocked.Exchange(ref _semanticQuotaExhaustedAtTicks, 0) != 0;

    /// <summary>True once retrieving ticket exemplars has failed and not since recovered.</summary>
    public bool ExemplarsUnavailable => Volatile.Read(ref _exemplarsFailedAtTicks) > 0;

    public DateTimeOffset? ExemplarsFailedAt
    {
        get
        {
            var ticks = Volatile.Read(ref _exemplarsFailedAtTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    public int ExemplarFailures => Volatile.Read(ref _exemplarFailures);

    /// <summary>The most recent reason, so /health says what broke and not merely that it did.</summary>
    public string ExemplarFailureReason => Volatile.Read(ref _exemplarFailure);

    public void RecordExemplarRetrievalFailed(string reason)
    {
        Interlocked.CompareExchange(ref _exemplarsFailedAtTicks, DateTimeOffset.UtcNow.UtcTicks, 0);
        Interlocked.Increment(ref _exemplarFailures);
        Volatile.Write(ref _exemplarFailure, reason);
    }

    /// <returns>True if this call ended a degraded period.</returns>
    public bool RecordExemplarRetrievalSucceeded() =>
        Interlocked.Exchange(ref _exemplarsFailedAtTicks, 0) != 0;
}
