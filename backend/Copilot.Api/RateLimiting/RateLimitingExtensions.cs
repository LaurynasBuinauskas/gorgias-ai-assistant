using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Copilot.Api.RateLimiting;

public static class RateLimitingExtensions
{
    /// <summary>Applied to the drafting endpoints, which call the LLM and cost real money.</summary>
    public const string DraftPolicy = "draft";

    /// <summary>Backstop for the cheap shell endpoints; well above any human's pace.</summary>
    private const int GlobalPermitsPerMinute = 120;

    /// <summary>A draft takes seconds, so no agent sustains more than a handful per minute.</summary>
    private const int DraftPermitsPerMinute = 20;

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = WriteRetryAfterAsync;

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                context => Partition(context, "global", GlobalPermitsPerMinute));

            options.AddPolicy(DraftPolicy, context => Partition(context, DraftPolicy, DraftPermitsPerMinute));
        });

        return services;
    }

    /// <summary>
    /// One bucket per client, so a single caller — authenticated or not — can only exhaust
    /// its own budget rather than the whole team's.
    /// </summary>
    private static RateLimitPartition<string> Partition(HttpContext context, string scope, int permitsPerMinute) =>
        RateLimitPartition.GetFixedWindowLimiter(
            $"{scope}:{ClientKey(context)}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitsPerMinute,
                Window = TimeSpan.FromMinutes(1),
            });

    /// <summary>
    /// The bearer token is shared across the pilot team today, so partitioning by it would
    /// be no better than a single global bucket; the remote IP is the best per-agent signal
    /// available. Swap this for the subject claim once OIDC lands.
    /// </summary>
    private static string ClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static ValueTask WriteRetryAfterAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        return ValueTask.CompletedTask;
    }
}
