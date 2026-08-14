namespace Copilot.Api.Uploads;

/// <summary>One uploaded policy file sitting in staging, not yet published.</summary>
public sealed record PolicyDraft
{
    public required string BlobName { get; init; }

    public required string FileName { get; init; }

    public required string Market { get; init; }

    public required string Topic { get; init; }

    /// <summary>Free-text attribution until real identity lands. Trusted, and logged as such.</summary>
    public required string UploadedBy { get; init; }

    public required DateTimeOffset UploadedAt { get; init; }

    public required long SizeBytes { get; init; }

    /// <summary>Set by the publish workflow once this upload has reached the live index.</summary>
    public string? PublishId { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }
}
