using Copilot.Domain;

namespace Copilot.Api.Contracts;

/// <summary>v1 draft payload. Append-only: never change shipped fields in place.</summary>
public sealed record DraftResponseV1
{
    public int V { get; init; } = 1;

    public required string DraftId { get; init; }

    public required long TicketId { get; init; }

    public required string Status { get; init; }

    public required string Body { get; init; }

    public string? Language { get; init; }

    public static DraftResponseV1 From(Draft draft) => new()
    {
        DraftId = draft.DraftId,
        TicketId = draft.TicketId,
        Status = "drafted",
        Body = draft.Body,
        Language = draft.Language,
    };
}
