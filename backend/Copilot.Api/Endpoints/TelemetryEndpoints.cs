using Copilot.Api.Contracts;

namespace Copilot.Api.Endpoints;

public static class TelemetryEndpoints
{
    public static IEndpointRouteBuilder MapTelemetryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/telemetry/anchor", (AnchorTelemetryRequestV1 telemetry, ILogger<Program> logger) =>
        {
            logger.LogInformation(
                "Anchor mode {Mode} reported for account {Account}",
                telemetry.Mode,
                telemetry.Account);
            return Results.Accepted();
        });

        return app;
    }
}
