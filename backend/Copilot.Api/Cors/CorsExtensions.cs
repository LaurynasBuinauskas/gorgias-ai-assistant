namespace Copilot.Api.Cors;

public static class CorsExtensions
{
    public const string PolicyName = "panel";

    /// <summary>Everything the panel actually sends; anything else is not ours to allow.</summary>
    private static readonly string[] s_methods = ["GET", "POST"];
    private static readonly string[] s_headers = ["Authorization", "Content-Type"];

    /// <summary>
    /// The panel calls the API from another origin (a different port in development, a
    /// different host in production). Development allows any loopback origin so the
    /// Aspire-assigned port just works; production allows exactly the configured origins.
    /// Bearer auth means no cookies, so credentials are never allowed.
    /// </summary>
    public static IServiceCollection AddPanelCors(
        this IServiceCollection services,
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        var allowedOrigins = configuration.GetSection("Api:AllowedOrigins").Get<string[]>() ?? [];

        if (!environment.IsDevelopment())
        {
            Validate(allowedOrigins);
        }

        services.AddCors(options => options.AddPolicy(PolicyName, policy =>
        {
            if (environment.IsDevelopment())
            {
                policy.SetIsOriginAllowed(origin =>
                    Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback);
            }
            else
            {
                policy.WithOrigins(allowedOrigins);
            }

            policy.WithMethods(s_methods).WithHeaders(s_headers);
        }));

        return services;
    }

    /// <summary>
    /// Fails at startup rather than leaving a healthy-looking API that rejects every panel
    /// request with an opaque browser-side CORS error.
    /// </summary>
    private static void Validate(string[] allowedOrigins)
    {
        if (allowedOrigins.Length == 0)
        {
            throw new InvalidOperationException(
                "Api:AllowedOrigins is empty. Set it to the panel's origin — for example "
                + "Api__AllowedOrigins__0=https://<panel-host> — or the browser will block "
                + "every request the panel makes.");
        }

        foreach (var origin in allowedOrigins)
        {
            // A trailing slash is the classic silent failure here: CORS compares origins
            // exactly, so "https://host/" never matches the browser's "https://host".
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                || uri.AbsolutePath != "/"
                || origin.EndsWith('/'))
            {
                throw new InvalidOperationException(
                    $"Api:AllowedOrigins contains '{origin}', which is not a bare origin. "
                    + "Use scheme and host only, with no trailing slash or path — "
                    + "for example https://<panel-host>.");
            }
        }
    }
}
