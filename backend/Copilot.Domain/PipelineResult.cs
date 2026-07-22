namespace Copilot.Domain;

/// <summary>
/// Outcome of the drafting pipeline. <see cref="InsufficientKnowledge"/> is an expected
/// result (rendered as a first-class UI state), not an error — hence a typed result
/// rather than an exception.
/// </summary>
public abstract record PipelineResult
{
    private PipelineResult()
    {
    }

    public sealed record Success(Draft Draft) : PipelineResult;

    public sealed record InsufficientKnowledge(string Message) : PipelineResult;
}
