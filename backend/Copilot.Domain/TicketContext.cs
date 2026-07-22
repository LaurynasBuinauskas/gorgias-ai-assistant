namespace Copilot.Domain;

/// <summary>
/// The prompt-relevant view of a Gorgias ticket: metadata plus the conversation,
/// deliberately excluding the heavy integrations payload the Gorgias API embeds.
/// </summary>
public sealed record TicketContext
{
    public required long Id { get; init; }

    public string? Subject { get; init; }

    public required string Status { get; init; }

    public string? Channel { get; init; }

    /// <summary>Ticket-level language reported by Gorgias (e.g. "de"); a prior, not authoritative —
    /// the pipeline pins output language from the newest customer message.</summary>
    public string? Language { get; init; }

    public TicketCustomer? Customer { get; init; }

    /// <summary>Conversation ordered oldest to newest.</summary>
    public required IReadOnlyList<TicketMessage> Messages { get; init; }
}
