namespace Copilot.Pipeline;

/// <summary>One item in a streamed draft: either text as it arrives, or a typed refusal.</summary>
public abstract record DraftChunk
{
    private DraftChunk()
    {
    }

    public sealed record Delta(string Text) : DraftChunk;

    public sealed record Insufficient(string Message) : DraftChunk;
}
