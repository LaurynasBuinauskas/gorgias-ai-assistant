using Copilot.Domain;

namespace Copilot.Pipeline;

public interface IDraftingPipeline
{
    Task<PipelineResult> GenerateDraftAsync(
        TicketContext ticket,
        DraftRequest request,
        CancellationToken cancellationToken);

    /// <summary>Same pipeline, surfaced incrementally so the panel can render as it writes.</summary>
    IAsyncEnumerable<DraftChunk> StreamDraftAsync(
        TicketContext ticket,
        DraftRequest request,
        CancellationToken cancellationToken);
}
