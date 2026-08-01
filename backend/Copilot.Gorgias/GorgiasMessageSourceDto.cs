namespace Copilot.Gorgias;

/// <summary>
/// Where a message came from and went to. <see cref="To"/> carries the support inbox the
/// customer wrote to, which is one of the three signals that identify a ticket's market.
/// </summary>
public sealed record GorgiasMessageSourceDto(
    string? Type,
    GorgiasAddressDto? From = null,
    IReadOnlyList<GorgiasAddressDto>? To = null);

public sealed record GorgiasAddressDto(string? Name, string? Address);
