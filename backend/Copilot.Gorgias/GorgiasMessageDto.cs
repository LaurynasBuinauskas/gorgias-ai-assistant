namespace Copilot.Gorgias;

public sealed record GorgiasMessageDto(
    long Id,
    bool Public,
    bool FromAgent,
    string? StrippedText,
    string? BodyText,
    GorgiasUserDto? Sender,
    GorgiasMessageSourceDto? Source,
    DateTimeOffset? CreatedDatetime,
    DateTimeOffset? SentDatetime,
    GorgiasMessageMetaDto? Meta = null);

/// <summary>
/// <see cref="CurrentPage"/> is the storefront page a chat was started from — the market
/// signal for chat tickets, which carry no order and no inbox address.
/// </summary>
public sealed record GorgiasMessageMetaDto(string? CurrentPage);
