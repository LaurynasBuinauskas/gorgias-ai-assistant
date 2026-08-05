using Copilot.Pipeline;

namespace Copilot.Api.Contracts;

/// <summary>
/// The "progress" frames of the v1 draft stream, typed rather than anonymous so the wire keys
/// the panel parses are pinned by tests — a renamed property here would be a silently empty
/// timeline there. Additive to the stream contract: a panel that predates the "progress"
/// event drops it unread.
/// </summary>
public static class DraftStreamProgressV1
{
    /// <summary>The payload for a progress-worthy chunk, or null for chunks that are not.</summary>
    public static object? From(DraftChunk chunk) => chunk switch
    {
        DraftChunk.Searched searched => new SearchedV1(
            "searched",
            searched.Market,
            searched.Signal.ToLowerInvariant(),
            searched.Policy,
            searched.Templates,
            searched.Tickets,
            searched.Internal),
        DraftChunk.Coverage coverage => new CoverageV1(
            "coverage",
            coverage.Decision.ToString().ToLowerInvariant()),
        DraftChunk.Drafting => new DraftingV1("drafting"),
        _ => null,
    };

    public sealed record SearchedV1(
        string Stage,
        string Market,
        string Signal,
        int Policy,
        int Templates,
        int PastTickets,
        int InternalGuides);

    public sealed record CoverageV1(string Stage, string Decision);

    public sealed record DraftingV1(string Stage);
}
