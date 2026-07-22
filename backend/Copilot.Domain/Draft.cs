namespace Copilot.Domain;

public sealed record Draft
{
    public required string DraftId { get; init; }

    public required long TicketId { get; init; }

    public required string Body { get; init; }

    /// <summary>Language the draft is written in (pinned to the newest customer message).</summary>
    public string? Language { get; init; }
}
