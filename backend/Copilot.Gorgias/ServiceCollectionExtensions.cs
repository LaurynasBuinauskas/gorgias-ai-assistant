using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Copilot.Gorgias;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGorgias(this IServiceCollection services)
    {
        services.AddOptions<GorgiasOptions>()
            .BindConfiguration(GorgiasOptions.SectionName)
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.Subdomain)
                    && !string.IsNullOrWhiteSpace(o.Email)
                    && !string.IsNullOrWhiteSpace(o.ApiKey),
                "Gorgias credentials are missing. Set Gorgias:Subdomain, Gorgias:Email and Gorgias:ApiKey "
                    + "via user-secrets (dev) or Key Vault (prod).")
            .ValidateOnStart();

        services.AddSingleton<IGorgiasCredentialProvider, ApiKeyCredentialProvider>();
        services.AddTransient<GorgiasAuthenticationHandler>();

        services.AddHttpClient<IGorgiasTicketClient, GorgiasTicketClient>((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<GorgiasOptions>>().Value;
                client.BaseAddress = new Uri($"https://{options.Subdomain}.gorgias.com/");
            })
            .AddHttpMessageHandler<GorgiasAuthenticationHandler>()
            // The ticket endpoint executes integration lookups server-side and has been
            // observed taking ~14 s, so the default 10 s attempt timeout is too tight.
            .AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(90);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
            });

        return services;
    }
}
