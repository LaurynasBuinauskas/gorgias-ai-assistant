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
    DateTimeOffset? SentDatetime);
