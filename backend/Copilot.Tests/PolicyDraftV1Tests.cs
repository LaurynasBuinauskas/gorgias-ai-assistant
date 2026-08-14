using Copilot.Api.Contracts;
using Copilot.Api.Uploads;

namespace Copilot.Tests;

/// <summary>
/// The state the admin page sorts by: an upload is "staged" until a publish stamps it, and
/// the stamp is what flips it — not time, not position in the list.
/// </summary>
public sealed class PolicyDraftV1Tests
{
    private static PolicyDraft Draft(string? publishId = null) => new()
    {
        BlobName = "DE/returns/one.md",
        FileName = "one.md",
        Market = "DE",
        Topic = "returns",
        UploadedBy = "Rasa",
        UploadedAt = DateTimeOffset.UtcNow,
        SizeBytes = 100,
        PublishId = publishId,
        PublishedAt = publishId is null ? null : DateTimeOffset.UtcNow,
    };

    [Fact]
    public void AnUnstampedUploadIsStaged()
    {
        var v1 = PolicyDraftV1.From(Draft());

        Assert.Equal("staged", v1.State);
        Assert.Null(v1.PublishId);
    }

    [Fact]
    public void AStampedUploadIsPublished()
    {
        var v1 = PolicyDraftV1.From(Draft(publishId: "abc123"));

        Assert.Equal("published", v1.State);
        Assert.Equal("abc123", v1.PublishId);
        Assert.NotNull(v1.PublishedAt);
    }
}
