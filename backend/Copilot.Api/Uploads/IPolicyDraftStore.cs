namespace Copilot.Api.Uploads;

/// <summary>Where staged policy uploads live between upload and publish.</summary>
public interface IPolicyDraftStore
{
    Task<PolicyDraft> SaveAsync(PolicyDraft draft, Stream content, CancellationToken cancellationToken);

    Task<IReadOnlyList<PolicyDraft>> ListAsync(CancellationToken cancellationToken);

    /// <summary>The staged file's text, or null when the blob does not exist.</summary>
    Task<string?> ReadTextAsync(string blobName, CancellationToken cancellationToken);
}
