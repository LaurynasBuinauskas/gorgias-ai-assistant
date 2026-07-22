using Copilot.Api.Contracts;
using Copilot.Domain;
using Copilot.Gorgias;
using Copilot.Pipeline;

namespace Copilot.Api.Endpoints;

public static class DraftEndpoints
{
    public static IEndpointRouteBuilder MapDraftEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/tickets/{ticketId:long}/drafts", async (
            long ticketId,
            IGorgiasTicketClient gorgias,
            IDraftingPipeline pipeline,
            CancellationToken cancellationToken) =>
        {
            var ticket = await gorgias.GetTicketAsync(ticketId, cancellationToken);
            if (ticket is null)
            {
                return Results.NotFound();
            }

            var result = await pipeline.GenerateDraftAsync(ticket, cancellationToken);
            return result switch
            {
                PipelineResult.Success success => Results.Ok(DraftResponseV1.From(success.Draft)),
                PipelineResult.InsufficientKnowledge insufficient =>
                    Results.Ok(new InsufficientDataResponseV1 { Message = insufficient.Message }),
                _ => throw new InvalidOperationException($"Unhandled pipeline result: {result.GetType().Name}"),
            };
        });

        return app;
    }
}
