namespace Copilot.Api.Uploads;

/// <summary>Starts the publish workflow. Abstracted so the coordinator is testable offline.</summary>
public interface IPublishTrigger
{
    /// <summary>True if publishing is configured at all (a missing token means it is not).</summary>
    bool IsConfigured { get; }

    Task TriggerAsync(
        string publishId,
        IReadOnlyList<string> blobs,
        string publishedBy,
        string mode,
        CancellationToken cancellationToken);
}
