using Copilot.Api.Contracts;

namespace Copilot.Api.Endpoints;

public static class ConfigEndpoints
{
    public static IEndpointRouteBuilder MapConfigEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/config", () => new ConfigResponseV1
        {
            KillSwitch = false,
            MinShellVersion = "0.1.0",
            AnchorProbes = [],
        });

        return app;
    }
}
