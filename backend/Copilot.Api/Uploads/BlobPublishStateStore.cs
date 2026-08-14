using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;

namespace Copilot.Api.Uploads;

public sealed class BlobPublishStateStore(
    IOptions<PolicyUploadOptions> uploadOptions,
    IOptions<PolicyPublishOptions> publishOptions) : IPublishStateStore
{
    private static readonly JsonSerializerOptions s_json = JsonSerializerOptions.Web;

    private readonly BlobContainerClient _container = new(
        uploadOptions.Value.ConnectionString, publishOptions.Value.VersionsContainer);

    public Task<PublishStatus?> ReadStatusAsync(string publishId, CancellationToken cancellationToken) =>
        ReadAsync<PublishStatus>($"publishes/{publishId}/status.json", cancellationToken);

    public async Task<JsonElement?> ReadValidationReportAsync(
        string publishId, CancellationToken cancellationToken)
    {
        var report = await ReadAsync<JsonElement>(
            $"publishes/{publishId}/validation-report.json", cancellationToken);
        return report.ValueKind == JsonValueKind.Undefined ? null : report;
    }

    public async Task WriteQueuedStatusAsync(string publishId, CancellationToken cancellationToken)
    {
        var status = new PublishStatus
        {
            PublishId = publishId,
            Step = "queued",
            State = "running",
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await UploadAsync($"publishes/{publishId}/status.json", status, cancellationToken);
    }

    public async Task<IReadOnlyList<PublishLedger>> ListLedgersAsync(CancellationToken cancellationToken)
    {
        var ledgers = new List<PublishLedger>();
        await foreach (var item in _container.GetBlobsAsync(
            new Azure.Storage.Blobs.Models.GetBlobsOptions { Prefix = "publishes/" },
            cancellationToken))
        {
            if (!item.Name.EndsWith("/ledger.json", StringComparison.Ordinal))
            {
                continue;
            }

            var ledger = await ReadAsync<PublishLedger>(item.Name, cancellationToken);
            if (ledger is not null)
            {
                ledgers.Add(ledger);
            }
        }

        return [.. ledgers.OrderByDescending(l => l.PublishedAt)];
    }

    public async Task<string?> ReadInflightAsync(CancellationToken cancellationToken)
    {
        var marker = await ReadAsync<InflightMarker>("publishes/inflight.json", cancellationToken);
        return marker?.PublishId;
    }

    public Task WriteInflightAsync(string publishId, CancellationToken cancellationToken) =>
        UploadAsync("publishes/inflight.json", new InflightMarker(publishId), cancellationToken);

    private sealed record InflightMarker(string PublishId);

    private async Task<T?> ReadAsync<T>(string blobName, CancellationToken cancellationToken)
    {
        try
        {
            var download = await _container.GetBlobClient(blobName)
                .DownloadContentAsync(cancellationToken);
            return download.Value.Content.ToObjectFromJson<T>(s_json);
        }
        catch (RequestFailedException error) when (error.Status == 404)
        {
            return default;
        }
    }

    private async Task UploadAsync<T>(string blobName, T value, CancellationToken cancellationToken)
    {
        var data = BinaryData.FromObjectAsJson(value, s_json);
        await _container.GetBlobClient(blobName).UploadAsync(data, overwrite: true, cancellationToken);
    }
}
