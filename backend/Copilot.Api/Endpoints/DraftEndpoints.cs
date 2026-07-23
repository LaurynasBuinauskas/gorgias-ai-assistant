using System.Text.Json;
using Copilot.Api.Contracts;
using Copilot.Domain;
using Copilot.Gorgias;
using Copilot.Pipeline;
using Microsoft.AspNetCore.Http.Features;

namespace Copilot.Api.Endpoints;

public static class DraftEndpoints
{
    private static readonly JsonSerializerOptions s_json = JsonSerializerOptions.Web;

    public static IEndpointRouteBuilder MapDraftEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/tickets/{ticketId:long}/drafts", async (
            long ticketId,
            DraftRequestV1? request,
            IGorgiasTicketClient gorgias,
            IDraftingPipeline pipeline,
            CancellationToken cancellationToken) =>
        {
            var ticket = await gorgias.GetTicketAsync(ticketId, cancellationToken);
            if (ticket is null)
            {
                return Results.NotFound();
            }

            var result = await pipeline.GenerateDraftAsync(
                ticket,
                request?.ToDomain() ?? DraftRequest.Initial,
                cancellationToken);

            return result switch
            {
                PipelineResult.Success success => Results.Ok(DraftResponseV1.From(success.Draft)),
                PipelineResult.InsufficientKnowledge insufficient =>
                    Results.Ok(new InsufficientDataResponseV1 { Message = insufficient.Message }),
                _ => throw new InvalidOperationException($"Unhandled pipeline result: {result.GetType().Name}"),
            };
        });

        // Server-sent events. POST (not GET) because the panel replays the conversation in
        // the body, and fetch-based SSE lets us send the bearer header EventSource cannot.
        app.MapPost("/v1/tickets/{ticketId:long}/drafts/stream", async (
            long ticketId,
            DraftRequestV1? request,
            IGorgiasTicketClient gorgias,
            IDraftingPipeline pipeline,
            HttpContext http,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            var ticket = await gorgias.GetTicketAsync(ticketId, cancellationToken);
            if (ticket is null)
            {
                http.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            StartEventStream(http);

            // Emitted as soon as the (potentially slow) Gorgias fetch completes, so the
            // panel can show a ticket header while the model warms up.
            await WriteEventAsync(
                http.Response,
                "ticket",
                new
                {
                    customerName = ticket.Customer?.Name,
                    subject = ticket.Subject,
                    language = ticket.Language,
                    messageCount = ticket.Messages.Count(m => !m.IsInternalNote),
                },
                cancellationToken);

            var draftId = Guid.NewGuid().ToString("N");
            try
            {
                await foreach (var chunk in pipeline.StreamDraftAsync(
                    ticket,
                    request?.ToDomain() ?? DraftRequest.Initial,
                    cancellationToken))
                {
                    switch (chunk)
                    {
                        case DraftChunk.Delta delta:
                            await WriteEventAsync(http.Response, "delta", new { text = delta.Text }, cancellationToken);
                            break;

                        case DraftChunk.Insufficient insufficient:
                            await WriteEventAsync(
                                http.Response,
                                "insufficient",
                                new { message = insufficient.Message },
                                cancellationToken);
                            return;
                    }
                }

                await WriteEventAsync(http.Response, "done", new { draftId, ticketId }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // The agent navigated away or switched tickets; nothing to report.
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Streaming draft failed for ticket {TicketId}", ticketId);
                await WriteEventAsync(
                    http.Response,
                    "error",
                    new { message = "The assistant could not finish this draft. Try again." },
                    CancellationToken.None);
            }
        });

        return app;
    }

    private static void StartEventStream(HttpContext http)
    {
        http.Response.Headers.ContentType = "text/event-stream";
        http.Response.Headers.CacheControl = "no-cache";
        // Stops reverse proxies (App Service front end) from buffering the stream.
        http.Response.Headers["X-Accel-Buffering"] = "no";
        http.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
    }

    private static async Task WriteEventAsync(
        HttpResponse response,
        string eventName,
        object payload,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, s_json);
        await response.WriteAsync($"event: {eventName}\ndata: {json}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}
