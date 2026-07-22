using Copilot.Api.Contracts;
using Copilot.Domain;
using Copilot.Gorgias;

namespace Copilot.Api.Endpoints;

public static class DraftEndpoints
{
    public static IEndpointRouteBuilder MapDraftEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/tickets/{ticketId:long}/drafts", async (
            long ticketId,
            IGorgiasTicketClient gorgias,
            CancellationToken cancellationToken) =>
        {
            var ticket = await gorgias.GetTicketAsync(ticketId, cancellationToken);
            if (ticket is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(DraftResponseV1.From(CreateCannedDraft(ticket)));
        });

        return app;
    }

    // The real pipeline arrives in Stage 3; until then the draft echoes ticket content
    // to prove the server-side fetch works end to end.
    private static Draft CreateCannedDraft(TicketContext ticket)
    {
        var latestCustomerMessage = ticket.Messages.LastOrDefault(m => m is { FromAgent: false, IsInternalNote: false });
        var excerpt = latestCustomerMessage?.Text is { Length: > 0 } text
            ? text[..Math.Min(text.Length, 200)]
            : "(no customer message found)";

        return new Draft
        {
            DraftId = Guid.NewGuid().ToString("N"),
            TicketId = ticket.Id,
            Language = ticket.Language,
            Body = $"""
                [Canned draft — RAG pipeline arrives in Stage 3]
                Ticket: {ticket.Subject}
                Customer: {ticket.Customer?.Name ?? "unknown"}
                Latest customer message: {excerpt}
                """,
        };
    }
}
