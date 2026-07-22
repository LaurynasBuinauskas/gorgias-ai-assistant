using System.Threading.RateLimiting;

namespace Copilot.Api.RateLimiting;

public static class RateLimitingExtensions
{
    /// <summary>Coarse global limit sized for a single pilot team; refine per-client if abuse ever shows up.</summary>
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
                RateLimitPartition.GetFixedWindowLimiter("global", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 100,
                    Window = TimeSpan.FromMinutes(1),
                }));
        });

        return services;
    }
}
