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
    private readonly byte[] _expectedToken = Encoding.UTF8.GetBytes(options.Value.BearerToken);

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/v1"))
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
