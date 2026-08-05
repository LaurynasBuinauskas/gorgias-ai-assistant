namespace Copilot.Pipeline;

/// <summary>
/// What the relevance gate decided for a draft. <see cref="Skipped"/> is its own value
/// because a gate that could not score is not a gate that passed — the draft rests on the
/// prompt rule alone, and the panel should say so rather than claim coverage was checked.
/// </summary>
public enum CoverageDecision
{
    Passed,

    Declined,

    /// <summary>Semantic ranking was unavailable, so no score existed to judge.</summary>
    Skipped,
}
