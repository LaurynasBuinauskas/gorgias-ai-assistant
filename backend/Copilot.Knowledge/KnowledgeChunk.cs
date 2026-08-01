namespace Copilot.Knowledge;

/// <summary>
/// One retrieved chunk, with everything needed to cite it and to prove afterwards which
/// context produced a given draft.
/// </summary>
public sealed record KnowledgeChunk
{
    public required string Id { get; init; }

    /// <summary>Breadcrumb, e.g. "DE &gt; Warranty &gt; Garantieausschlusse".</summary>
    public required string Title { get; init; }

    public required string Content { get; init; }

    public required string Market { get; init; }

    public required string Topic { get; init; }

    /// <summary>Repository path of the file this came from — the citation target.</summary>
    public required string SourcePath { get; init; }

    /// <summary>Reranker score where semantic ranking applied, otherwise the fused score.</summary>
    public required double Score { get; init; }
}
