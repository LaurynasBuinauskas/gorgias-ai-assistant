using Copilot.Api.Contracts;
using Copilot.Api.Uploads;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace Copilot.Api.Endpoints;

/// <summary>
/// The client-facing policy upload surface. Gated by the admin token (never the agents'
/// drafting token), and every action logs who did what — the audit trail is the control
/// while identity is still a shared secret.
/// </summary>
public static class AdminPolicyEndpoints
{
    public static IEndpointRouteBuilder MapAdminPolicyEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/admin/policy/files", async (
            HttpRequest request,
            IPolicyDraftStore store,
            IOptions<PolicyUploadOptions> options,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            if (!request.HasFormContentType)
            {
                return Results.BadRequest(new { message = "Send multipart/form-data with a 'file'." });
            }

            var form = await request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file");
            if (file is null)
            {
                return Results.BadRequest(new { message = "The 'file' part is missing." });
            }

            var market = (form["market"].ToString()).Trim().ToUpperInvariant();
            var topic = form["topic"].ToString().Trim().ToLowerInvariant();
            var uploadedBy = form["uploadedBy"].ToString().Trim();

            var refusal = PolicyUploadValidator.Validate(
                file.FileName, file.Length, market, topic, uploadedBy, options.Value);
            if (refusal is not null)
            {
                return Results.BadRequest(new { message = refusal });
            }

            var draft = new PolicyDraft
            {
                BlobName = $"{market}/{topic}/{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Path.GetFileName(file.FileName)}",
                FileName = Path.GetFileName(file.FileName),
                Market = market,
                Topic = topic,
                UploadedBy = uploadedBy,
                UploadedAt = DateTimeOffset.UtcNow,
                SizeBytes = file.Length,
            };

            await using var content = file.OpenReadStream();
            await store.SaveAsync(draft, content, cancellationToken);

            logger.LogInformation(
                "Policy upload: {BlobName} ({SizeBytes} bytes) for {Market}/{Topic} by {UploadedBy}",
                draft.BlobName, draft.SizeBytes, market, topic, uploadedBy);

            return Results.Ok(PolicyDraftV1.From(draft));
        });

        app.MapGet("/v1/admin/policy/files", async (
            IPolicyDraftStore store,
            CancellationToken cancellationToken) =>
        {
            var drafts = await store.ListAsync(cancellationToken);
            return Results.Ok(new { v = 1, drafts = drafts.Select(PolicyDraftV1.From) });
        });

        app.MapGet("/v1/admin/policy/current", async (
            PolicyCatalog catalog,
            IOptions<PolicyUploadOptions> options,
            CancellationToken cancellationToken) =>
        {
            var documents = await catalog.ListCurrentAsync(cancellationToken);
            return Results.Ok(new
            {
                v = 1,
                markets = options.Value.Markets,
                documents = documents.Select(d => new
                {
                    sourcePath = d.SourcePath,
                    market = d.Market,
                    topic = d.Topic,
                    chunks = d.Chunks,
                }),
            });
        });

        // Query-string rather than a route segment because blob names contain slashes.
        app.MapGet("/v1/admin/policy/file-content", async (
            string blob,
            IPolicyDraftStore store,
            CancellationToken cancellationToken) =>
        {
            if (blob.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new
                {
                    message = "Word files are shown after a check runs the conversion — "
                              + "use Check this upload.",
                });
            }

            var content = await store.ReadTextAsync(blob, cancellationToken);
            return content is null
                ? Results.NotFound()
                : Results.Ok(new { v = 1, blob, content });
        });

        app.MapPost("/v1/admin/policy/validate", async (
            PublishRequestV1? request,
            PublishCoordinator coordinator,
            CancellationToken cancellationToken) =>
        {
            if (request is null)
            {
                return Results.BadRequest(new { message = "Send { blobs, publishedBy }." });
            }

            var decision = await coordinator.StartValidateAsync(
                request.Blobs, request.PublishedBy, cancellationToken);
            return decision.PublishId is null
                ? Results.Json(new { message = decision.Refusal }, statusCode: StatusCodes.Status409Conflict)
                : Results.Ok(new { v = 1, publishId = decision.PublishId });
        });

        app.MapPost("/v1/admin/policy/publish", async (
            PublishRequestV1? request,
            PublishCoordinator coordinator,
            CancellationToken cancellationToken) =>
        {
            if (request is null)
            {
                return Results.BadRequest(new { message = "Send { blobs, publishedBy }." });
            }

            var decision = await coordinator.StartPublishAsync(
                request.Blobs, request.PublishedBy, cancellationToken);
            return decision.PublishId is null
                ? Results.Json(new { message = decision.Refusal }, statusCode: StatusCodes.Status409Conflict)
                : Results.Ok(new { v = 1, publishId = decision.PublishId });
        });

        app.MapPost("/v1/admin/policy/rollback", async (
            RollbackRequestV1? request,
            PublishCoordinator coordinator,
            CancellationToken cancellationToken) =>
        {
            var decision = await coordinator.StartRollbackAsync(
                request?.PublishedBy ?? "", cancellationToken);
            return decision.PublishId is null
                ? Results.Json(new { message = decision.Refusal }, statusCode: StatusCodes.Status409Conflict)
                : Results.Ok(new { v = 1, publishId = decision.PublishId });
        });

        app.MapGet("/v1/admin/policy/publishes/{publishId}", async (
            string publishId,
            IPublishStateStore state,
            CancellationToken cancellationToken) =>
        {
            var status = await state.ReadStatusAsync(publishId, cancellationToken);
            if (status is null)
            {
                return Results.NotFound();
            }

            // The findings ride along whenever the workflow produced a report — a validation
            // block explains why nothing happened, a completed check shows what it found —
            // so the page never needs a second round trip.
            var validation = status.Step is "blocked-by-validation" or "validate-complete"
                ? await state.ReadValidationReportAsync(publishId, cancellationToken)
                : null;
            return Results.Ok(new { v = 1, status, validation });
        });

        app.MapGet("/v1/admin/policy/publishes", async (
            IPublishStateStore state,
            CancellationToken cancellationToken) =>
        {
            var ledgers = await state.ListLedgersAsync(cancellationToken);
            return Results.Ok(new { v = 1, publishes = ledgers });
        });

        return app;
    }

    /// <summary>
    /// Kestrel's global body cap is sized for small JSON, which is right for every other
    /// route. Uploads get their own ceiling, set before the body is read.
    /// </summary>
    public static IApplicationBuilder UseUploadBodySizeLimit(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/v1/admin/policy/files"))
            {
                var options = context.RequestServices
                    .GetRequiredService<IOptions<PolicyUploadOptions>>().Value;
                var sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (sizeFeature is { IsReadOnly: false })
                {
                    // Headroom for multipart boundaries and the metadata fields.
                    sizeFeature.MaxRequestBodySize = options.MaxFileBytes + 64 * 1024;
                }
            }

            await next(context);
        });
}
