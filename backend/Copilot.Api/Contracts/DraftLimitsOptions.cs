namespace Copilot.Api.Contracts;

/// <summary>
/// Boundary caps on drafting requests (audit #2). The panel replays the whole conversation
/// on every call, so request size is client-controlled: without these, anyone holding the
/// shared token can post a body of fabricated turns and have it forwarded verbatim to
/// OpenAI. Retrieval multiplies prompt size again, which is why this is blocking for beta
/// rather than deferred.
/// </summary>
public sealed class DraftLimitsOptions
{
    public const string SectionName = "DraftLimits";

    /// <summary>A refinement conversation this long has already gone wrong.</summary>
    public int MaxTurns { get; set; } = 20;

    /// <summary>Roughly a long draft; anything larger is not a reply the agent wrote.</summary>
    public int MaxTurnCharacters { get; set; } = 4_000;

    public int MaxInstructionCharacters { get; set; } = 2_000;

    /// <summary>
    /// Ceiling across every turn plus the instruction. Caps the client-supplied half of the
    /// prompt; <see cref="Copilot.Pipeline.DraftingOptions"/> bounds the rest.
    /// </summary>
    public int MaxTotalCharacters { get; set; } = 24_000;

    /// <summary>
    /// Kestrel defaults to 30 MB. This API only ever accepts small JSON, and rejecting an
    /// oversized body at the transport layer costs nothing.
    /// </summary>
    public long MaxRequestBodyBytes { get; set; } = 128 * 1024;
}
