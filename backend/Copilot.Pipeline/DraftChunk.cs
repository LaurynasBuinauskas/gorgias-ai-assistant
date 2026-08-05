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

    /// <summary>
    /// What retrieval found, before anything is drafted: the market that was resolved, how,
    /// and how many chunks each corpus returned. Counts only — chunk text stays out of the
    /// stream for the same reason it stays out of the logs. Not emitted when retrieval is
    /// bypassed, because then nothing was searched and saying otherwise would be a lie.
    /// </summary>
    public sealed record Searched(
        string Market,
        string Signal,
        int Policy,
        int Templates,
        int Tickets,
        int Internal) : DraftChunk;

    /// <summary>The relevance gate's verdict. Not emitted when retrieval is bypassed.</summary>
    public sealed record Coverage(CoverageDecision Decision) : DraftChunk;

    /// <summary>The model call is about to start; everything before it is already known.</summary>
    public sealed record Drafting : DraftChunk;

    public sealed record Delta(string Text) : DraftChunk;

    /// <summary>Emitted once, after the reply text — never interleaved with it.</summary>
    public sealed record Sources(IReadOnlyList<DraftCitation> Citations) : DraftChunk;

    public sealed record Insufficient(string Message) : DraftChunk;
}
