namespace Copilot.Api.Uploads;

/// <summary>Where staged policy uploads live between upload and publish.</summary>
public interface IPolicyDraftStore
{
    Task<PolicyDraft> SaveAsync(PolicyDraft draft, Stream content, CancellationToken cancellationToken);

    Task<IReadOnlyList<PolicyDraft>> ListAsync(CancellationToken cancellationToken);
}
