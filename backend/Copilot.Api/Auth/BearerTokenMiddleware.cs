using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Copilot.Api.Auth;

/// <summary>
/// MVP auth: one shared bearer token for the pilot team, required on every /v1 route.
/// OIDC/PKCE replaces this in P2.
/// </summary>
public sealed class BearerTokenMiddleware(RequestDelegate next, IOptions<ApiOptions> options)
{
    /// <summary>
    /// Shell-facing endpoints, reachable without a token: the extension holds no
    /// credentials by design. Neither carries ticket data or PII — config is feature
    /// flags plus anchor selectors (including the kill switch), telemetry is dock mode.
    /// </summary>
    private static readonly string[] s_publicPaths = ["/v1/config", "/v1/telemetry/anchor"];

    private readonly byte[] _expectedToken = Encoding.UTF8.GetBytes(options.Value.BearerToken);

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/v1") || IsPublic(context.Request.Path))
        {
            await next(context);
            return;
        }

        if (!IsAuthorized(context.Request.Headers.Authorization.ToString()))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context);
    }

    private static bool IsPublic(PathString path) =>
        s_publicPaths.Any(publicPath => path.StartsWithSegments(publicPath));

    private bool IsAuthorized(string authorizationHeader)
    {
        const string prefix = "Bearer ";
        if (!authorizationHeader.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var provided = Encoding.UTF8.GetBytes(authorizationHeader[prefix.Length..]);
        return CryptographicOperations.FixedTimeEquals(provided, _expectedToken);
    }
}
