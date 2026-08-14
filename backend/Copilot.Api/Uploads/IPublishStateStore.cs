using System.Text.Json;

namespace Copilot.Api.Uploads;

/// <summary>What the workflow wrote about a publish, as the API reads it back.</summary>
public sealed record PublishStatus
{
    public required string PublishId { get; init; }

    public required string Step { get; init; }

    public required string State { get; init; }

    /// <summary>Free-shape payload from the workflow (validation findings on a block).</summary>
    public JsonElement? Detail { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>One completed publish, from its ledger blob.</summary>
public sealed record PublishLedger
{
    public required string PublishId { get; init; }

    public required string Mode { get; init; }

    public required string PublishedBy { get; init; }

    public required DateTimeOffset PublishedAt { get; init; }

    public required IReadOnlyList<string> Blobs { get; init; }

    public required string SnapshotIndex { get; init; }
}

/// <summary>
/// The publish state that lives in the `knowledge-versions` container: per-publish status
/// and ledger blobs written by the workflow, plus the queued marker the API writes when it
/// triggers one.
/// </summary>
public interface IPublishStateStore
{
    Task<PublishStatus?> ReadStatusAsync(string publishId, CancellationToken cancellationToken);

    Task<JsonElement?> ReadValidationReportAsync(string publishId, CancellationToken cancellationToken);

    Task WriteQueuedStatusAsync(string publishId, CancellationToken cancellationToken);

    /// <summary>Ledgers of completed publishes, newest first.</summary>
    Task<IReadOnlyList<PublishLedger>> ListLedgersAsync(CancellationToken cancellationToken);

    /// <summary>The publish the API most recently triggered, for the one-at-a-time guard.</summary>
    Task<string?> ReadInflightAsync(CancellationToken cancellationToken);

    Task WriteInflightAsync(string publishId, CancellationToken cancellationToken);
}
