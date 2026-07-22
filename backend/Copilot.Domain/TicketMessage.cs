namespace Copilot.Domain;

public sealed record TicketMessage
{
    public required long Id { get; init; }

    public required bool FromAgent { get; init; }

    /// <summary>Internal notes are agent-only side channel (Asana links etc.) —
    /// the pipeline must exclude them from customer-facing draft context or mark them explicitly.</summary>
    public required bool IsInternalNote { get; init; }

    /// <summary>Clean message text: Gorgias's stripped_text (no quoted history) when available.</summary>
    public required string Text { get; init; }

    public string? SenderName { get; init; }

    public DateTimeOffset? SentAt { get; init; }
}
