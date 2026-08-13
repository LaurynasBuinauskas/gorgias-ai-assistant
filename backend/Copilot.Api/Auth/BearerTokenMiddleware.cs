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

    // Empty when unconfigured, which can never equal a provided token — admin fails closed.
    private readonly byte[] _expectedAdminToken = Encoding.UTF8.GetBytes(options.Value.AdminToken);

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/v1") || IsPublic(context.Request.Path))
        {
            await next(context);
            return;
        }

        // Admin routes take only the admin token: the agents' drafting token must not be
        // able to change what the whole team's drafts are grounded in.
        var expected = context.Request.Path.StartsWithSegments("/v1/admin")
            ? _expectedAdminToken
            : _expectedToken;

        if (!IsAuthorized(context.Request.Headers.Authorization.ToString(), expected))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context);
    }

    private static bool IsPublic(PathString path) =>
        s_publicPaths.Any(publicPath => path.StartsWithSegments(publicPath));

    private static bool IsAuthorized(string authorizationHeader, byte[] expectedToken)
    {
        const string prefix = "Bearer ";
        if (!authorizationHeader.StartsWith(prefix, StringComparison.Ordinal) || expectedToken.Length == 0)
        {
            return false;
        }

        var provided = Encoding.UTF8.GetBytes(authorizationHeader[prefix.Length..]);
        return CryptographicOperations.FixedTimeEquals(provided, expectedToken);
    }
}
