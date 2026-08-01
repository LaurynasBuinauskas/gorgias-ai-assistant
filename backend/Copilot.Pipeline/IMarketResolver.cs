using Copilot.Domain;

namespace Copilot.Pipeline;

/// <summary>Which signal decided a ticket's market. Logged so a wrong answer is traceable.</summary>
public enum MarketSignal
{
    /// <summary>No signal identified the market; <c>GLOBAL</c> was used.</summary>
    Fallback,

    /// <summary>The Shopify shop domain on the order the ticket concerns.</summary>
    ShopDomain,

    /// <summary>The storefront support inbox the customer wrote to.</summary>
    RecipientAddress,

    /// <summary>Country on the customer's Shopify billing or default address.</summary>
    CustomerCountry,
}

/// <param name="Market">Market code, or <c>GLOBAL</c>.</param>
/// <param name="Signal">What decided it.</param>
public readonly record struct MarketResolution(string Market, MarketSignal Signal)
{
    public const string Global = "GLOBAL";

    public static MarketResolution Fallback => new(Global, MarketSignal.Fallback);
}

/// <summary>
/// Decides which market's policy applies to a ticket. Market is a correctness boundary:
/// answering a German customer with US return terms is a wrong answer with legal weight, and
/// it reads perfectly fluently — so resolution must be deterministic, pure, and must say which
/// signal decided rather than producing a bare answer nobody can audit.
///
/// This interface exists so the rest of the pipeline could be built and evaluated before the
/// client confirmed which signal to trust (`R-6`). Swapping the implementation changes nothing
/// above it.
/// </summary>
public interface IMarketResolver
{
    MarketResolution Resolve(TicketContext ticket);
}
