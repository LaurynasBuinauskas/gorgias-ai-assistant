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

        services.AddOptions<RetrievalOptions>()
            .BindConfiguration(RetrievalOptions.SectionName)
            .Validate(
                o => o.MinimumPolicyScore >= 0,
                "Retrieval:MinimumPolicyScore cannot be negative.")
            .Validate(
                o => o.SemanticRankingEnabled || o.MinimumPolicyScore <= 0.1,
                "Retrieval:MinimumPolicyScore is on the semantic reranker's scale. With "
                + "Knowledge:UseSemanticRanking=false the scores are ~0.03, so any meaningful "
                + "threshold declines every draft. Set Retrieval:MinimumPolicyScore=0 and "
                + "Retrieval:SemanticRankingEnabled=false together.")
            .ValidateOnStart();

        services.AddSingleton<IMarketResolver, StorefrontMarketResolver>();
        services.AddSingleton<KnowledgeRetriever>();
        services.AddSingleton<IDraftingPipeline, DraftingPipeline>();
        return services;
    }
}
