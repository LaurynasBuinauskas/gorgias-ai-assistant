using Copilot.Domain;

namespace Copilot.Pipeline;

/// <summary>
/// Resolves every ticket to <c>GLOBAL</c>, pending the client's answer on which signal
/// identifies a storefront (`open-questions.md` B-1).
///
/// This is a deliberate placeholder, not a default worth keeping. Under it the assistant
/// answers from <c>GLOBAL</c> policy for everyone — substantively identical to every market on
/// routine questions like return windows and duties, and **wrong** on the statutory material
/// that exists in only one market, such as the German Widerrufsbelehrung. The relevance gate
/// should decline those rather than answer from <c>GLOBAL</c>.
///
/// Fine for building and measuring. Not fit for go-live: `L-7` gates on `R-6` replacing it.
/// </summary>
public sealed class GlobalFallbackMarketResolver : IMarketResolver
{
    public MarketResolution Resolve(TicketContext ticket) => MarketResolution.Fallback;
}
