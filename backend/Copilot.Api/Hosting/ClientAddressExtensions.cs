using Microsoft.AspNetCore.HttpOverrides;

namespace Copilot.Api.Hosting;

public static class ClientAddressExtensions
{
    /// <summary>
    /// App Service terminates the connection at its front end, so without this every request
    /// appears to come from the load balancer and per-client rate limiting collapses back
    /// into a single bucket. The platform appends the caller's address to `X-Forwarded-For`,
    /// so with a forward limit of one the rightmost entry is the trustworthy one and a
    /// client-supplied header cannot win.
    /// </summary>
    public static IServiceCollection AddClientAddressForwarding(this IServiceCollection services) =>
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            // The App Service front end is the only hop and its address is not fixed.
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });
}
