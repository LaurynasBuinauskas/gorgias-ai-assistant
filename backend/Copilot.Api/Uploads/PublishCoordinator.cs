using Microsoft.Extensions.Logging;

namespace Copilot.Api.Uploads;

/// <summary>The outcome of asking for a publish: started, or refused with a reason.</summary>
public sealed record PublishDecision(string? PublishId, string? Refusal)
{
    public static PublishDecision Started(string publishId) => new(publishId, null);

    public static PublishDecision Refused(string reason) => new(null, reason);
}

/// <summary>
/// Decides whether a publish may start, then hands it to the workflow. One publish at a
/// time: two concurrent runs would race on the live index, so a new one is refused while
/// the last is neither published nor failed. Attribution is a form field until real
/// identity lands — logged as given, trusted as such.
/// </summary>
public sealed class PublishCoordinator(
    IPolicyDraftStore drafts,
    IPublishStateStore state,
    IPublishTrigger trigger,
    ILogger<PublishCoordinator> logger)
{
    private static readonly string[] s_terminalStates = ["succeeded", "failed"];

    public async Task<PublishDecision> StartPublishAsync(
        IReadOnlyList<string> blobs,
        string publishedBy,
        CancellationToken cancellationToken)
    {
        if (blobs.Count == 0)
        {
            return PublishDecision.Refused("Select at least one staged upload to publish.");
        }

        var staged = (await drafts.ListAsync(cancellationToken)).Select(d => d.BlobName).ToHashSet();
        var missing = blobs.Where(b => !staged.Contains(b)).ToList();
        return missing.Count > 0
            ? PublishDecision.Refused($"Not in staging: {string.Join(", ", missing)}")
            : await StartAsync(blobs, publishedBy, "publish", cancellationToken);
    }

    /// <summary>
    /// Runs conversion and content validation on staged uploads without touching any index —
    /// the pre-publish check. It shares the one-at-a-time lock deliberately: "the system is
    /// busy checking or publishing" is one idea for the person at the page, not two.
    /// </summary>
    public async Task<PublishDecision> StartValidateAsync(
        IReadOnlyList<string> blobs,
        string requestedBy,
        CancellationToken cancellationToken)
    {
        if (blobs.Count == 0)
        {
            return PublishDecision.Refused("Select a staged upload to check.");
        }

        var staged = (await drafts.ListAsync(cancellationToken)).Select(d => d.BlobName).ToHashSet();
        var missing = blobs.Where(b => !staged.Contains(b)).ToList();
        return missing.Count > 0
            ? PublishDecision.Refused($"Not in staging: {string.Join(", ", missing)}")
            : await StartAsync(blobs, requestedBy, "validate", cancellationToken);
    }

    /// <summary>
    /// Restores the state before the newest ledger entry: the previous publish's blob set,
    /// or the pure git tree when there is nothing earlier. Still fully gated — a rollback
    /// is a publish of older content, not a bypass.
    /// </summary>
    public async Task<PublishDecision> StartRollbackAsync(
        string publishedBy,
        CancellationToken cancellationToken)
    {
        var ledgers = await state.ListLedgersAsync(cancellationToken);
        if (ledgers.Count == 0)
        {
            return PublishDecision.Refused("Nothing has been published yet, so there is nothing to roll back.");
        }

        var previous = ledgers.Skip(1).FirstOrDefault();
        return await StartAsync(previous?.Blobs ?? [], publishedBy, "rollback", cancellationToken);
    }

    private async Task<PublishDecision> StartAsync(
        IReadOnlyList<string> blobs,
        string publishedBy,
        string mode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(publishedBy) || publishedBy.Trim().Length > 100)
        {
            return PublishDecision.Refused("Say who is publishing (publishedBy).");
        }

        if (!trigger.IsConfigured)
        {
            return PublishDecision.Refused(
                "Publishing is not configured yet: the workflow token is missing. "
                + "Uploads still work and nothing is lost.");
        }

        var inflightId = await state.ReadInflightAsync(cancellationToken);
        if (inflightId is not null)
        {
            var inflight = await state.ReadStatusAsync(inflightId, cancellationToken);
            if (inflight is not null && !s_terminalStates.Contains(inflight.State)
                && !IsStale(inflight))
            {
                return PublishDecision.Refused(
                    $"Publish {inflightId} is still running ({inflight.Step}). One at a time.");
            }
        }

        var publishId = Guid.NewGuid().ToString("N")[..12];
        await state.WriteQueuedStatusAsync(publishId, cancellationToken);
        try
        {
            await trigger.TriggerAsync(publishId, blobs, publishedBy.Trim(), mode, cancellationToken);
        }
        catch (Exception error)
        {
            // The first live run wedged the guard on exactly this: a failed dispatch after
            // the lock was taken left a "queued" publish nothing would ever finish. The
            // failure is recorded so the history explains itself, the lock is never taken,
            // and the caller gets the reason instead of a bare 500.
            await state.WriteTriggerFailedStatusAsync(publishId, cancellationToken);
            logger.LogError(error, "Policy {Mode} {PublishId} could not be dispatched", mode, publishId);
            return PublishDecision.Refused(
                "The publish could not be handed to the workflow — likely the GitHub token's "
                + "permissions. Nothing was changed; try again once it is fixed.");
        }

        // Only after a successful dispatch: an undispatched publish must never hold the lock.
        await state.WriteInflightAsync(publishId, cancellationToken);

        logger.LogInformation(
            "Policy {Mode} {PublishId} started by {PublishedBy}: {BlobCount} blob(s)",
            mode, publishId, publishedBy.Trim(), blobs.Count);
        return PublishDecision.Started(publishId);
    }

    /// <summary>
    /// A dispatched workflow replaces "queued" within about a minute of starting. A queued
    /// status this old means the run never started (or the runner died before its first
    /// write) and will never finish — holding the lock for it would wedge publishing.
    /// </summary>
    private static bool IsStale(PublishStatus status) =>
        status.Step == "queued" && DateTimeOffset.UtcNow - status.UpdatedAt > TimeSpan.FromMinutes(10);
}
