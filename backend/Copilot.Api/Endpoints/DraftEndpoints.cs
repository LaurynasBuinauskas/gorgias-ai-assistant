using System.Text.Json;
using Copilot.Api.Contracts;
using Copilot.Api.RateLimiting;
using Copilot.Domain;
using Copilot.Gorgias;
using Copilot.Pipeline;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

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
            IOptions<DraftLimitsOptions> limits,
            CancellationToken cancellationToken) =>
        {
            if (!TryReadRequest(request, limits.Value, out var draftRequest, out var error))
            {
                return Results.BadRequest(new { message = error });
            }

            var ticket = await gorgias.GetTicketAsync(ticketId, cancellationToken);
            if (ticket is null)
            {
                return Results.NotFound();
            }

            var result = await pipeline.GenerateDraftAsync(ticket, draftRequest, cancellationToken);

            return result switch
            {
                PipelineResult.Success success => Results.Ok(DraftResponseV1.From(success.Draft)),
                PipelineResult.InsufficientKnowledge insufficient =>
                    Results.Ok(new InsufficientDataResponseV1 { Message = insufficient.Message }),
                _ => throw new InvalidOperationException($"Unhandled pipeline result: {result.GetType().Name}"),
            };
        }).RequireRateLimiting(RateLimitingExtensions.DraftPolicy);

        app.MapPost("/v1/tickets/{ticketId:long}/drafts/stream", async (
            long ticketId,
            DraftRequestV1? request,
            IGorgiasTicketClient gorgias,
            IDraftingPipeline pipeline,
            IOptions<DraftLimitsOptions> limits,
            HttpContext http,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            if (!TryReadRequest(request, limits.Value, out var draftRequest, out var error))
            {
                // Rejected before the stream starts, so a normal status code still reaches the panel.
                http.Response.StatusCode = StatusCodes.Status400BadRequest;
                await http.Response.WriteAsJsonAsync(new { message = error }, cancellationToken);
                return;
            }

            var ticket = await gorgias.GetTicketAsync(ticketId, cancellationToken);
            if (ticket is null)
            {
                http.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            StartEventStream(http);

            var draftId = Guid.NewGuid().ToString("N");
            try
            {
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

                await foreach (var chunk in pipeline.StreamDraftAsync(
                    ticket,
                    draftRequest,
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
            catch (Exception ex) when (http.RequestAborted.IsCancellationRequested)
            {
                // A write that failed because the client vanished is a disconnect, not a fault.
                logger.LogDebug(ex, "Draft stream for ticket {TicketId} ended early: client gone", ticketId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Streaming draft failed for ticket {TicketId}", ticketId);
                await TryReportFailureAsync(http, logger, ticketId);
            }
        }).RequireRateLimiting(RateLimitingExtensions.DraftPolicy);

        return app;
    }

    /// <summary>
    /// An absent body is the initial draft request; a present one must satisfy the caps
    /// before any of it reaches the model.
    /// </summary>
    private static bool TryReadRequest(
        DraftRequestV1? request,
        DraftLimitsOptions limits,
        out DraftRequest draftRequest,
        out string error)
    {
        if (request is null)
        {
            draftRequest = DraftRequest.Initial;
            error = "";
            return true;
        }

        return request.TryValidate(limits, out draftRequest, out error);
    }

    /// <summary>
    /// Tells the panel the draft failed. Best-effort: the headers are already sent, so this
    /// is the only way to report it, and the client may disconnect while we try.
    /// </summary>
    private static async Task TryReportFailureAsync(HttpContext http, ILogger logger, long ticketId)
    {
        try
        {
            await WriteEventAsync(
                http.Response,
                "error",
                new { message = "The assistant could not finish this draft. Try again." },
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not deliver the error event for ticket {TicketId}", ticketId);
        }
    }

    private static void StartEventStream(HttpContext http)
    {
        http.Response.Headers.ContentType = "text/event-stream";
        http.Response.Headers.CacheControl = "no-cache";        
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
