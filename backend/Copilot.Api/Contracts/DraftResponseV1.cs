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

    /// <summary>Added in this version; absent from earlier clients, which ignore it.</summary>
    public IReadOnlyList<DraftCitationV1> Citations { get; init; } = [];

    public static DraftResponseV1 From(Draft draft) => new()
    {
        DraftId = draft.DraftId,
        TicketId = draft.TicketId,
        Status = "drafted",
        Body = draft.Body,
        Language = draft.Language,
        Citations = [.. draft.Citations.Select(DraftCitationV1.From)],
    };
}

/// <summary>A source the draft relied on. Chunk ids are internal and deliberately not exposed.</summary>
public sealed record DraftCitationV1
{
    public required string Label { get; init; }

    public required string SourcePath { get; init; }

    public required string Market { get; init; }

    public static DraftCitationV1 From(DraftCitation citation) => new()
    {
        Label = citation.Label,
        SourcePath = citation.SourcePath,
        Market = citation.Market,
    };
}
