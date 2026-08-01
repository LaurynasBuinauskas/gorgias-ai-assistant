using Copilot.Domain;
using Microsoft.Extensions.Logging;

namespace Copilot.Pipeline;

/// <summary>
/// Resolves a ticket's market from the storefront it concerns.
///
/// Market is a correctness boundary, not a ranking preference: answering a German customer
/// with US return terms is a wrong answer with legal weight, and it reads perfectly fluently.
/// So resolution is deterministic, pure, and reports which signal decided — a market nobody
/// can audit is worse than no market.
///
/// Signal order, first match wins, measured over 80 real tickets:
///
/// 1. **Order storefront** (39 % present) — the shop that took the order. Most precise,
///    because the terms that apply are the ones the customer bought under.
/// 2. **Chat page** (20 %) — the storefront page a chat started from. The only signal chat
///    tickets carry: they have no order and no inbox address.
/// 3. **Support inbox** (71 %) — where the customer wrote. Coarsest, see below.
/// 4. **GLOBAL** — 8 % of tickets resolve to nothing, and falling back is the safe direction.
///
/// Order beats inbox when they disagree (16 % of tickets), on the reasoning that the
/// applicable terms belong to the shop that took the money rather than the mailbox the
/// customer happened to use. That is a business judgement recorded in `open-questions.md`,
/// not a technical one — the deciding signal is logged so it can be revisited on evidence.
/// </summary>
public sealed class StorefrontMarketResolver(ILogger<StorefrontMarketResolver> logger) : IMarketResolver
{
    /// <summary>
    /// Storefront domain to market. Ordered longest-first: <c>eu.timeresistance.com</c> must
    /// win over <c>timeresistance.com</c>, which is a suffix of it.
    /// </summary>
    private static readonly (string Domain, string Market)[] s_storefronts =
    [
        ("global.timeresistance.com", "GLOBAL"),
        ("eu.timeresistance.com", "EU"),
        ("ca.timeresistance.com", "CA"),
        ("au.timeresistance.com", "AU_NZ"),
        ("timeresistance.co.uk", "UK"),
        ("timeresistance.de", "DE"),
        ("timeresistance.fr", "FR"),
        ("timeresistance.es", "ES"),
        ("timeresistance.it", "IT"),
        ("timeresistance.nl", "NL"),
        ("timeresistance.pl", "PL"),
        ("timeresistance.se", "SE"),
        ("timeresistance.sg", "SG"),
        ("timeresistance.com", "US"),
    ];

    /// <summary>
    /// Inbox local parts that name a language queue rather than a storefront. These sit on the
    /// shared <c>.com</c> domain, so reading the domain alone labels every one of them US and
    /// answers a German customer with US terms — the exact failure the market filter exists to
    /// prevent. Observed in the live account, not hypothetical.
    /// </summary>
    private static readonly Dictionary<string, string> s_languageInboxes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["kundenservice"] = "DE",
        ["magazin"] = "DE",
        ["serviceclient"] = "FR",
        ["bonjour"] = "FR",
        ["servicioalcliente"] = "ES",
        ["hola"] = "ES",
        ["servizioclienti"] = "IT",
        ["ciao"] = "IT",
        ["klantenservice"] = "NL",
    };

    public MarketResolution Resolve(TicketContext ticket)
    {
        var signals = ticket.MarketSignals;

        var resolution =
            FromUrls(signals.OrderUrls, MarketSignal.ShopDomain)
            ?? FromUrls(signals.ChatPages, MarketSignal.ShopDomain)
            ?? FromInboxes(signals.SupportInboxes)
            ?? MarketResolution.Fallback;

        logger.LogDebug(
            "Ticket {TicketId} resolved to market {Market} by {Signal}",
            ticket.Id, resolution.Market, resolution.Signal);

        return resolution;
    }

    private static MarketResolution? FromUrls(IReadOnlyList<string> urls, MarketSignal signal)
    {
        foreach (var url in urls)
        {
            if (MarketOf(url) is { } market)
            {
                return new MarketResolution(market, signal);
            }
        }

        return null;
    }

    private static MarketResolution? FromInboxes(IReadOnlyList<string> inboxes)
    {
        foreach (var inbox in inboxes)
        {
            var at = inbox.IndexOf('@');
            if (at <= 0)
            {
                continue;
            }

            // Local part first: a language queue on the shared domain is not a US ticket.
            if (s_languageInboxes.TryGetValue(inbox[..at], out var byLanguage))
            {
                return new MarketResolution(byLanguage, MarketSignal.RecipientAddress);
            }

            if (MarketOf(inbox[(at + 1)..]) is { } byDomain)
            {
                return new MarketResolution(byDomain, MarketSignal.RecipientAddress);
            }
        }

        return null;
    }

    private static string? MarketOf(string value)
    {
        foreach (var (domain, market) in s_storefronts)
        {
            if (value.Contains(domain, StringComparison.OrdinalIgnoreCase))
            {
                return market;
            }
        }

        return null;
    }
}
