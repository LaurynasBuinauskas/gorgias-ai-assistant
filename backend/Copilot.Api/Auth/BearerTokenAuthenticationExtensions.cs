namespace Copilot.Api.Auth;

public static class BearerTokenAuthenticationExtensions
{
    public static IServiceCollection AddBearerTokenAuthentication(this IServiceCollection services)
    {
        services.AddOptions<ApiOptions>()
            .BindConfiguration(ApiOptions.SectionName)
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.BearerToken),
                "Api:BearerToken is not configured. Set it in appsettings.Development.json (dev) or Key Vault (prod).")
            .ValidateOnStart();

        return services;
    }

    public static IApplicationBuilder UseBearerTokenAuthentication(this IApplicationBuilder app) =>
        app.UseMiddleware<BearerTokenMiddleware>();
}
