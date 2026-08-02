using Microsoft.Extensions.DependencyInjection;

namespace Copilot.Knowledge;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKnowledgeStore(this IServiceCollection services)
    {
        services.AddOptions<KnowledgeOptions>()
            .BindConfiguration(KnowledgeOptions.SectionName)
            .Validate(
                o => Uri.TryCreate(o.Endpoint, UriKind.Absolute, out _),
                "Knowledge:Endpoint must be the absolute URL of the Azure AI Search service.")
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.IndexName),
                "Knowledge:IndexName is not configured.")
            .ValidateOnStart();

        services.AddSingleton<RetrievalHealth>();
        services.AddSingleton<IKnowledgeStore, AzureSearchKnowledgeStore>();
        return services;
    }
}
