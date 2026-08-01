using Microsoft.Extensions.DependencyInjection;

namespace Copilot.Pipeline;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDraftingPipeline(this IServiceCollection services)
    {
        services.AddOptions<DraftingOptions>()
            .BindConfiguration(DraftingOptions.SectionName)
            .Validate(o => o.MaxOutputTokens > 0, "Drafting:MaxOutputTokens must be greater than zero.")
            .Validate(
                o => o.MaxTranscriptCharacters > 0,
                "Drafting:MaxTranscriptCharacters must be greater than zero.")
            .Validate(
                o => o.MaxPromptCharacters > o.MaxTranscriptCharacters + o.RetrievalCharacterAllowance,
                "Drafting:MaxPromptCharacters must leave room for the transcript and the retrieval allowance.")
            .ValidateOnStart();

        services.AddSingleton<IDraftingPipeline, DraftingPipeline>();
        return services;
    }
}
