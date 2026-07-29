using System.Reflection;
using Copilot.Api.Contracts;

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
        var version = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";

        app.MapGet("/health", () => new HealthResponseV1 { Version = version })            
            .DisableRateLimiting();

        return app;
    }
}
