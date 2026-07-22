using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenAI;

namespace Copilot.Ai;

/// <summary>
/// The only place vendor LLM SDK types may appear; everything else consumes
/// <see cref="IChatClient"/> from Microsoft.Extensions.AI.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAi(this IServiceCollection services)
    {
        services.AddOptions<AiOptions>()
            .BindConfiguration(AiOptions.SectionName)
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.ApiKey),
                "OpenAI API key is missing. Set OpenAi:ApiKey via user-secrets (dev) or Key Vault (prod).")
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.DraftingModel),
                "OpenAi:DraftingModel is not configured (pin a dated model snapshot in appsettings.json).")
            .ValidateOnStart();

        services.AddSingleton<IChatClient>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<AiOptions>>().Value;
            return new OpenAIClient(options.ApiKey)
                .GetChatClient(options.DraftingModel)
                .AsIChatClient();
        });

        return services;
    }
}
