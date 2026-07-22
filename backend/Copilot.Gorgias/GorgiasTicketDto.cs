namespace Copilot.Gorgias;

/// <summary>Wire shape of GET /api/tickets/{id} — only the fields we consume.</summary>
public sealed record GorgiasTicketDto(
    long Id,
    string? Subject,
    string? Status,
    string? Channel,
    string? Language,
    GorgiasUserDto? Customer,
    GorgiasUserDto? Requester,
    IReadOnlyList<GorgiasMessageDto>? Messages);
