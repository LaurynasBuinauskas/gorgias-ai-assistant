namespace Copilot.Gorgias;

public sealed record GorgiasUserDto(
    string? Name,
    string? Email,
    IReadOnlyDictionary<string, GorgiasIntegrationDto>? Integrations = null);

/// <summary>
/// One connected system's view of this customer. Each Shopify integration corresponds to a
/// single storefront in this tenant, so an order's status URL identifies the shop that took
/// it — the most precise market signal available.
/// </summary>
public sealed record GorgiasIntegrationDto(IReadOnlyList<GorgiasOrderDto>? Orders);

public sealed record GorgiasOrderDto(string? OrderStatusUrl, string? ReferringSite);
