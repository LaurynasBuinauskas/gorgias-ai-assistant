using Copilot.Domain;
using Copilot.Pipeline;

namespace Copilot.Tests;

public sealed class MarketResolverTests
{
    [Fact]
    public void FallsBackToGlobalAndSaysSo()
    {
        var resolution = new GlobalFallbackMarketResolver().Resolve(Ticket());

        Assert.Equal("GLOBAL", resolution.Market);
        // The signal is the audit trail. A bare "GLOBAL" is indistinguishable from a market
        // that was genuinely resolved as global, which is exactly what R-6 must not be.
        Assert.Equal(MarketSignal.Fallback, resolution.Signal);
    }

    [Fact]
    public void NeverGuessesFromTheTicketLanguage()
    {
        // Gorgias's own sample data pairs language "fr" with a US billing address, so language
        // is excluded outright rather than used as a last resort.
        var german = new GlobalFallbackMarketResolver().Resolve(Ticket(language: "de"));
        var french = new GlobalFallbackMarketResolver().Resolve(Ticket(language: "fr"));

        Assert.Equal(german, french);
    }

    [Fact]
    public void IsPureAndRepeatable()
    {
        var resolver = new GlobalFallbackMarketResolver();
        var ticket = Ticket();

        Assert.Equal(resolver.Resolve(ticket), resolver.Resolve(ticket));
    }

    private static TicketContext Ticket(string? language = null) => new()
    {
        Id = 42,
        Subject = "Where is my order?",
        Status = "open",
        Language = language,
        Customer = new TicketCustomer("Jane Doe", "jane@example.com"),
        Messages = [],
    };
}
