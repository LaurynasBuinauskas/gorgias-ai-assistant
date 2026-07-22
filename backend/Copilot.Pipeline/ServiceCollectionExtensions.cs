using Microsoft.Extensions.DependencyInjection;

namespace Copilot.Pipeline;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDraftingPipeline(this IServiceCollection services)
    {
        services.AddSingleton<IDraftingPipeline, DraftingPipeline>();
        return services;
    }
}
