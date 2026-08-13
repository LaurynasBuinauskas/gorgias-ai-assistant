using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace Copilot.Api.Uploads;

/// <summary>
/// Drafts live in the `knowledge-drafts` blob container, named
/// <c>{market}/{topic}/{utc-timestamp}-{filename}</c> so listing groups naturally and
/// nothing is ever overwritten — every upload is a new blob, and publish decides which one
/// becomes policy. Attribution and metadata ride on the blob itself so the store needs no
/// database.
/// </summary>
public sealed class BlobPolicyDraftStore(IOptions<PolicyUploadOptions> options) : IPolicyDraftStore
{
    private readonly BlobContainerClient _container = new(
        options.Value.ConnectionString, options.Value.DraftsContainer);

    public async Task<PolicyDraft> SaveAsync(
        PolicyDraft draft,
        Stream content,
        CancellationToken cancellationToken)
    {
        var blob = _container.GetBlobClient(draft.BlobName);
        await blob.UploadAsync(
            content,
            new BlobUploadOptions
            {
                Metadata = new Dictionary<string, string>
                {
                    ["market"] = draft.Market,
                    ["topic"] = draft.Topic,
                    ["fileName"] = draft.FileName,
                    // Blob metadata must be ASCII; names often are not. Base64 keeps them.
                    ["uploadedBy"] = Convert.ToBase64String(
                        System.Text.Encoding.UTF8.GetBytes(draft.UploadedBy)),
                },
            },
            cancellationToken);

        return draft;
    }

    public async Task<IReadOnlyList<PolicyDraft>> ListAsync(CancellationToken cancellationToken)
    {
        var drafts = new List<PolicyDraft>();
        await foreach (var item in _container.GetBlobsAsync(
            new GetBlobsOptions { Traits = BlobTraits.Metadata },
            cancellationToken))
        {
            var metadata = item.Metadata ?? new Dictionary<string, string>();
            string Field(string key, string fallback = "") =>
                metadata.TryGetValue(key, out var value) ? value : fallback;

            drafts.Add(new PolicyDraft
            {
                BlobName = item.Name,
                FileName = Field("fileName", Path.GetFileName(item.Name)),
                Market = Field("market"),
                Topic = Field("topic"),
                UploadedBy = DecodeUploader(Field("uploadedBy")),
                UploadedAt = item.Properties.CreatedOn ?? default,
                SizeBytes = item.Properties.ContentLength ?? 0,
            });
        }

        return [.. drafts.OrderByDescending(d => d.UploadedAt)];
    }

    private static string DecodeUploader(string encoded)
    {
        try
        {
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        }
        catch (FormatException)
        {
            return encoded;
        }
    }
}
