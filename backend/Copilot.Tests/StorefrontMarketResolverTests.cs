using Copilot.Domain;
using Copilot.Pipeline;
using Microsoft.Extensions.Logging.Abstractions;

namespace Copilot.Tests;

/// <summary>
/// A wrong market is a wrong answer with legal weight that reads perfectly fluently, so every
/// market gets a test rather than a representative sample.
/// </summary>
public sealed class StorefrontMarketResolverTests
{
    private static readonly StorefrontMarketResolver s_resolver =
        new(NullLogger<StorefrontMarketResolver>.Instance);

    [Theory]
    [InlineData("https://timeresistance.com/orders/abc", "US")]
    [InlineData("https://eu.timeresistance.com/orders/abc", "EU")]
    [InlineData("https://global.timeresistance.com/orders/abc", "GLOBAL")]
    [InlineData("https://ca.timeresistance.com/orders/abc", "CA")]
    [InlineData("https://au.timeresistance.com/orders/abc", "AU_NZ")]
    [InlineData("https://timeresistance.co.uk/orders/abc", "UK")]
    [InlineData("https://timeresistance.de/orders/abc", "DE")]
    [InlineData("https://timeresistance.fr/orders/abc", "FR")]
    [InlineData("https://timeresistance.es/orders/abc", "ES")]
    [InlineData("https://timeresistance.it/orders/abc", "IT")]
    [InlineData("https://timeresistance.nl/orders/abc", "NL")]
    [InlineData("https://timeresistance.pl/orders/abc", "PL")]
    [InlineData("https://timeresistance.se/orders/abc", "SE")]
    [InlineData("https://timeresistance.sg/orders/abc", "SG")]
    public void ResolvesEveryMarketFromTheOrderStorefront(string url, string expected)
    {
        var resolution = s_resolver.Resolve(Ticket(new MarketSignals { OrderUrls = [url] }));

        Assert.Equal(expected, resolution.Market);
        Assert.Equal(MarketSignal.ShopDomain, resolution.Signal);
    }

    [Fact]
    public void SubdomainStorefrontsBeatTheBareDomainTheyContain()
    {
        // "eu.timeresistance.com" contains "timeresistance.com"; matching the shorter one
        // first would silently label every EU order as US.
        var resolution = s_resolver.Resolve(
            Ticket(new MarketSignals { OrderUrls = ["https://eu.timeresistance.com/orders/1"] }));

        Assert.Equal("EU", resolution.Market);
    }

    [Theory]
    [InlineData("kundenservice@timeresistance.com", "DE")]
    [InlineData("serviceclient@timeresistance.com", "FR")]
    [InlineData("klantenservice@timeresistance.com", "NL")]
    public void LanguageQueuesOnTheSharedDomainAreNotUsTickets(string inbox, string expected)
    {
        // Observed in the live account: the German and French queues sit on the .com domain.
        // Reading the domain alone answers a German customer with US return terms.
        var resolution = s_resolver.Resolve(Ticket(new MarketSignals { SupportInboxes = [inbox] }));

        Assert.Equal(expected, resolution.Market);
        Assert.Equal(MarketSignal.RecipientAddress, resolution.Signal);
    }

    [Theory]
    [InlineData("community@timeresistance.co.uk", "UK")]
    [InlineData("bonjour@timeresistance.fr", "FR")]
    [InlineData("magazin@timeresistance.de", "DE")]
    [InlineData("care@timeresistance.com", "US")]
    public void ResolvesFromTheInboxDomainWhenTheLocalPartSaysNothing(string inbox, string expected)
    {
        Assert.Equal(expected, s_resolver.Resolve(
            Ticket(new MarketSignals { SupportInboxes = [inbox] })).Market);
    }

    [Fact]
    public void ChatPageResolvesTicketsThatHaveNoOrderAndNoInbox()
    {
        var resolution = s_resolver.Resolve(Ticket(new MarketSignals
        {
            ChatPages = ["https://timeresistance.pl/products/leather-bag"],
        }));

        Assert.Equal("PL", resolution.Market);
    }

    [Fact]
    public void TheOrderWinsWhenSignalsDisagree()
    {
        // Measured at 16% of tickets: ordered from one storefront, wrote to another inbox.
        // The terms that apply belong to the shop that took the money.
        var resolution = s_resolver.Resolve(Ticket(new MarketSignals
        {
            OrderUrls = ["https://timeresistance.de/orders/abc"],
            SupportInboxes = ["care@timeresistance.com"],
        }));

        Assert.Equal("DE", resolution.Market);
        Assert.Equal(MarketSignal.ShopDomain, resolution.Signal);
    }

    [Fact]
    public void ChatPageWinsOverTheInbox()
    {
        var resolution = s_resolver.Resolve(Ticket(new MarketSignals
        {
            ChatPages = ["https://timeresistance.es/collections/all"],
            SupportInboxes = ["care@timeresistance.com"],
        }));

        Assert.Equal("ES", resolution.Market);
    }

    [Fact]
    public void FallsBackToGlobalAndSaysSoWhenNothingIdentifiesTheStorefront()
    {
        var resolution = s_resolver.Resolve(Ticket(MarketSignals.None));

        Assert.Equal("GLOBAL", resolution.Market);
        Assert.Equal(MarketSignal.Fallback, resolution.Signal);
    }

    [Fact]
    public void UnrelatedDomainsDoNotResolveToAMarket()
    {
        var resolution = s_resolver.Resolve(Ticket(new MarketSignals
        {
            OrderUrls = ["https://example.com/orders/1"],
            SupportInboxes = ["someone@gmail.com"],
        }));

        Assert.Equal("GLOBAL", resolution.Market);
        Assert.Equal(MarketSignal.Fallback, resolution.Signal);
    }

    [Fact]
    public void NeverUsesTheTicketLanguage()
    {
        // Gorgias's own sample data pairs language "fr" with a US billing address.
        var german = s_resolver.Resolve(Ticket(MarketSignals.None, language: "de"));
        var french = s_resolver.Resolve(Ticket(MarketSignals.None, language: "fr"));

        Assert.Equal(german, french);
    }

    [Fact]
    public void IsPureAndRepeatable()
    {
        var ticket = Ticket(new MarketSignals { OrderUrls = ["https://timeresistance.it/x"] });

        Assert.Equal(s_resolver.Resolve(ticket), s_resolver.Resolve(ticket));
    }

    private static TicketContext Ticket(MarketSignals signals, string? language = null) => new()
    {
        Id = 42,
        Subject = "Where is my order?",
        Status = "closed",
        Language = language,
        MarketSignals = signals,
        Messages = [],
    };
}
