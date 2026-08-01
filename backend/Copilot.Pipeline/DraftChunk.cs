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

    public sealed record Delta(string Text) : DraftChunk;

    /// <summary>Emitted once, after the reply text — never interleaved with it.</summary>
    public sealed record Sources(IReadOnlyList<DraftCitation> Citations) : DraftChunk;

    public sealed record Insufficient(string Message) : DraftChunk;
}
