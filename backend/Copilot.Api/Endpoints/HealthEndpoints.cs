using System.Reflection;
using Copilot.Api.Contracts;
using Microsoft.AspNetCore.RateLimiting;

namespace Copilot.Api.Endpoints;

public static class HealthEndpoints
{
    /// <summary>
    /// Reports liveness and, more usefully, which build is serving. Every other endpoint
    /// answers identically before and after a deployment swap, so without this a deploy can
    /// only be confirmed by waiting and hoping — which has already produced false readings.
    /// </summary>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        // Resolved once: the assembly cannot change while the process runs.
        var version = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";

        app.MapGet("/health", () => new HealthResponseV1 { Version = version })
            // A rate-limited health check reports "unhealthy" when the service is merely
            // busy, which is the opposite of useful. The response is a constant, so serving
            // it is effectively free.
            .DisableRateLimiting();

        return app;
    }
}
