namespace Copilot.Domain;

/// <summary>
/// The evidence a ticket carries about which storefront it concerns.
///
/// Deliberately raw strings rather than a resolved market: resolution is a policy decision
/// with legal weight, and keeping the inputs separate from the verdict means a wrong answer
/// can be traced to the signal that produced it rather than reconstructed from guesswork.
///
/// Measured coverage across 80 real tickets (`docs/gorgias-extraction-findings.md`):
/// order URLs 39 %, chat pages 20 %, support inboxes 71 %, and 92 % resolvable in total.
/// </summary>
public sealed record MarketSignals
{
    public static readonly MarketSignals None = new();

    /// <summary>Shopify order status / referring URLs — the shop that took the order.</summary>
    public IReadOnlyList<string> OrderUrls { get; init; } = [];

    /// <summary>Storefront pages a chat was started from.</summary>
    public IReadOnlyList<string> ChatPages { get; init; } = [];

    /// <summary>Support inbox addresses the customer wrote to.</summary>
    public IReadOnlyList<string> SupportInboxes { get; init; } = [];
}
