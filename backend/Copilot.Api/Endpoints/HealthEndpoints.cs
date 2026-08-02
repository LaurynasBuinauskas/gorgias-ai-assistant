using System.Reflection;
using Copilot.Api.Contracts;
using Copilot.Knowledge;

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

        app.MapGet("/health", (RetrievalHealth retrieval) =>
            {
                var degraded = new List<string>();
                if (retrieval.SemanticRankingUnavailable)
                {
                    degraded.Add(
                        "semantic-ranking-quota-exhausted since "
                        + $"{retrieval.SemanticQuotaExhaustedAt:u} ({retrieval.DegradedRetrievals} "
                        + "retrieval(s) unranked); the relevance gate cannot score");
                }

                return new HealthResponseV1
                {
                    Status = degraded.Count == 0 ? "healthy" : "degraded",
                    Version = version,
                    Degraded = degraded.Count == 0 ? null : degraded,
                };
            })
            .DisableRateLimiting();

        return app;
    }
}
