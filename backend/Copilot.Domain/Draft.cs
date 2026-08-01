namespace Copilot.Domain;

public sealed record Draft
{
    public required string DraftId { get; init; }

    public required long TicketId { get; init; }

    public required string Body { get; init; }

    /// <summary>Language the draft is written in (pinned to the newest customer message).</summary>
    public string? Language { get; init; }

    /// <summary>
    /// Sources the draft cited, resolved to real chunks. Kept out of <see cref="Body"/> so the
    /// agent copies a clean reply — citations are for review, not for the customer.
    /// </summary>
    public IReadOnlyList<DraftCitation> Citations { get; init; } = [];
}
