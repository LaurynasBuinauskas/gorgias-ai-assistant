namespace Copilot.Pipeline;

/// <summary>
/// Bounds on what the pipeline sends to and accepts from the model. The API caps the
/// client-supplied half of the prompt; these cap the half the client does not control —
/// the ticket transcript, which arrives from Gorgias and can be arbitrarily long, and the
/// retrieved context R-7 will attach.
/// </summary>
public sealed class DraftingOptions
{
    public const string SectionName = "Drafting";

    /// <summary>A support reply that needs more than this has gone wrong.</summary>
    public int MaxOutputTokens { get; set; } = 1_200;

    /// <summary>
    /// Newest messages win when a transcript is trimmed: the recent exchange is what the
    /// reply must answer, and the opening of a long thread is rarely what is being asked.
    /// </summary>
    public int MaxTranscriptCharacters { get; set; } = 20_000;

    /// <summary>
    /// Headroom reserved for retrieved policy, templates and exemplars. Nothing consumes it
    /// yet; it exists so the ceiling below already accounts for retrieval rather than being
    /// re-derived when R-7 lands.
    /// </summary>
    public int RetrievalCharacterAllowance { get; set; } = 24_000;

    /// <summary>
    /// Hard ceiling on the assembled prompt, retrieval included. Exceeding it means a cap
    /// upstream failed, so it is logged as a defect rather than silently truncated.
    /// </summary>
    public int MaxPromptCharacters { get; set; } = 80_000;
}
