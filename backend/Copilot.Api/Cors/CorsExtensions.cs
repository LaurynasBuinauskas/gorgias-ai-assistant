namespace Copilot.Api.Cors;

public static class CorsExtensions
{
    public const string PolicyName = "panel";

    /// <summary>
    /// The panel calls the API from another origin (different port in dev, different host
    /// in prod). Dev allows any loopback origin so the Aspire-assigned panel port just works;
    /// prod allows exactly the configured panel origin(s). Bearer auth means no credentials.
    /// </summary>
    public static IServiceCollection AddPanelCors(
        this IServiceCollection services,
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        var allowedOrigins = configuration.GetSection("Api:AllowedOrigins").Get<string[]>() ?? [];

        services.AddCors(options => options.AddPolicy(PolicyName, policy =>
        {
            if (environment.IsDevelopment())
            {
                policy.SetIsOriginAllowed(origin => Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback);
            }
            else
            {
                policy.WithOrigins(allowedOrigins);
            }

            policy.AllowAnyHeader().AllowAnyMethod();
        }));

        return services;
    }
}
