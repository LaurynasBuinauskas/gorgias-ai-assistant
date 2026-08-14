using Copilot.Api.Uploads;

namespace Copilot.Api.Contracts;

/// <summary>A staged policy upload, as the admin interface sees it.</summary>
public sealed record PolicyDraftV1
{
    public required int V { get; init; }

    public required string BlobName { get; init; }

    public required string FileName { get; init; }

    public required string Market { get; init; }

    public required string Topic { get; init; }

    public required string UploadedBy { get; init; }

    public required DateTimeOffset UploadedAt { get; init; }

    public required long SizeBytes { get; init; }

    /// <summary>"staged" until a publish carries it to the live index, then "published".</summary>
    public required string State { get; init; }

    public string? PublishId { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }

    public static PolicyDraftV1 From(PolicyDraft draft) => new()
    {
        V = 1,
        BlobName = draft.BlobName,
        FileName = draft.FileName,
        Market = draft.Market,
        Topic = draft.Topic,
        UploadedBy = draft.UploadedBy,
        UploadedAt = draft.UploadedAt,
        SizeBytes = draft.SizeBytes,
        State = draft.PublishId is null ? "staged" : "published",
        PublishId = draft.PublishId,
        PublishedAt = draft.PublishedAt,
    };
}
