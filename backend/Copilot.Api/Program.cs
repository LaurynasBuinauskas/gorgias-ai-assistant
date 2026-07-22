using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Copilot.Api.Contracts;
using Copilot.Domain;
using Copilot.Gorgias;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGorgias();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
        RateLimitPartition.GetFixedWindowLimiter("global", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 100,
            Window = TimeSpan.FromMinutes(1),
        }));
});

var app = builder.Build();

var bearerToken = app.Configuration["Api:BearerToken"];
if (string.IsNullOrWhiteSpace(bearerToken))
{
    throw new InvalidOperationException(
        "Api:BearerToken is not configured. Set it in appsettings.Development.json (dev) or Key Vault (prod).");
}

app.UseRateLimiter();

// MVP auth: one shared bearer token for the pilot team. OIDC/PKCE replaces this in P2.
app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/v1"))
    {
        await next(context);
        return;
    }

    var authorization = context.Request.Headers.Authorization.ToString();
    if (!IsValidBearer(authorization, bearerToken))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    await next(context);
});

app.MapGet("/", () => "Copilot API");

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

app.MapGet("/v1/config", () => new ConfigResponseV1
{
    KillSwitch = false,
    MinShellVersion = "0.1.0",
    AnchorProbes = [],
});

app.MapPost("/v1/telemetry/anchor", (AnchorTelemetryRequestV1 telemetry, ILogger<Program> logger) =>
{
    logger.LogInformation("Anchor mode {Mode} reported for account {Account}", telemetry.Mode, telemetry.Account);
    return Results.Accepted();
});

app.Run();

// The real pipeline arrives in Stage 3; until then the draft echoes ticket content to
// prove the server-side fetch works end to end.
static Draft CreateCannedDraft(TicketContext ticket)
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

static bool IsValidBearer(string authorizationHeader, string expectedToken)
{
    const string prefix = "Bearer ";
    if (!authorizationHeader.StartsWith(prefix, StringComparison.Ordinal))
    {
        return false;
    }

    var provided = Encoding.UTF8.GetBytes(authorizationHeader[prefix.Length..]);
    var expected = Encoding.UTF8.GetBytes(expectedToken);
    return CryptographicOperations.FixedTimeEquals(provided, expected);
}
