using Copilot.Domain;

namespace Copilot.Pipeline;

public interface IDraftingPipeline
{
    Task<PipelineResult> GenerateDraftAsync(TicketContext ticket, CancellationToken cancellationToken);
}
