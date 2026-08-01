using Copilot.Domain;

namespace Copilot.Pipeline;

/// <summary>
/// One item in a streamed draft: text as it arrives, the sources behind it, or a typed refusal.
/// </summary>
public abstract record DraftChunk
{
    private DraftChunk()
    {
    }

    /// <summary>
    /// Emitted first, carrying the id every log line for this draft is keyed by. The pipeline
    /// owns it so that feedback quoting a draft id can be traced back to the exact retrieved
    /// context — which is impossible if the transport invents its own id.
    /// </summary>
    public sealed record Started(string DraftId) : DraftChunk;

    public sealed record Delta(string Text) : DraftChunk;

    /// <summary>Emitted once, after the reply text — never interleaved with it.</summary>
    public sealed record Sources(IReadOnlyList<DraftCitation> Citations) : DraftChunk;

    public sealed record Insufficient(string Message) : DraftChunk;
}
