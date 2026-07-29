using Copilot.Api.Contracts;

namespace Copilot.Api.Endpoints;

public static class TelemetryEndpoints
{
    public static IEndpointRouteBuilder MapTelemetryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/telemetry/anchor", (AnchorTelemetryRequestV1 telemetry, ILogger<Program> logger) =>
        {
            if (!telemetry.TryValidate(out var mode, out var account))
            {
                return Results.BadRequest(new { message = "Expected mode 'docked' or 'floating' and a non-empty account." });
            }

            logger.LogInformation("Anchor mode {Mode} reported for account {Account}", mode, account);
            return Results.Accepted();
        });

        return app;
    }
}
