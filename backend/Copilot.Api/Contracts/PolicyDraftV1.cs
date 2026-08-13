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
    };
}
