using Copilot.Evals;

namespace Copilot.Tests;

/// <summary>
/// The PII patterns had no tests, which meant a sweep reporting zero findings was
/// indistinguishable from a sweep that could not find anything. This repo has already shipped
/// that exact failure once — a PII eval class passed green over an empty index and stayed green
/// through four rounds in which humans found four leak classes.
///
/// So every pattern is shown to bite on a planted value, and the redaction placeholders are
/// shown not to trip it. This does not make the corpus clean, and no test here can: the leaks
/// found so far matched no pattern at all. It makes the number the sweep prints meaningful.
/// </summary>
public sealed class PiiSweepTests
{
    [Theory]
    [InlineData("email", "please write to alex.whitfield@example.com about this")]
    [InlineData("phone", "call me on +44 7700 900123 tomorrow")]
    [InlineData("iban", "my account is GB29 NWBK 6016 1331 9268 19")]
    [InlineData("card", "the card was 4111 1111 1111 1111")]
    [InlineData("uk-postcode", "it should go to SW1A 1AA instead")]
    [InlineData("nl-postcode", "the address is 1017 CE Amsterdam")]
    [InlineData("ca-postcode", "deliver to K1A 0B1 please")]
    [InlineData("order-reference", "my order is #TR#4429012 from March")]
    [InlineData("tracking-number", "tracking says 998877665544332211 is stuck")]
    public void EveryPatternBitesOnAPlantedValue(string expected, string text)
    {
        var findings = PiiSweep.ScanText(text, "unit");

        Assert.Contains(expected, findings.Select(f => f.Pattern));
    }

    [Fact]
    public void RedactionPlaceholdersAreNotTreatedAsLeaks()
    {
        // What a correctly redacted exchange looks like. If this tripped, the sweep would cry
        // wolf on all 17,863 documents and be switched off within a day.
        const string redacted = """
            Customer asked: Hello, my order [ORDER] has not arrived and [TRACKING] has not
            updated. You can reach me at [EMAIL] or [PHONE].

            Support replied: Hello [CUSTOMER], thank you for reaching out. I have checked
            [ORDER] and will send an update to [EMAIL]. [SIGNATURE]
            """;

        Assert.Empty(PiiSweep.ScanText(redacted, "unit"));
    }

    [Fact]
    public void FindingsAreMaskedSoAReportIsNotItselfALeak()
    {
        var finding = Assert.Single(
            PiiSweep.ScanText("write to alex.whitfield@example.com", "unit"),
            f => f.Pattern == "email");

        Assert.DoesNotContain("whitfield", finding.Sample);
        Assert.Contains("*", finding.Sample);
    }

    [Fact]
    public void OrdinaryReplyTextIsNotFlagged()
    {
        // Guards the other direction: a sweep that fires on prose is a sweep nobody reads.
        const string ordinary = """
            Thank you for your message. Returns are accepted within 30 days of delivery, and
            we ship with DHL, FedEx or UPS depending on the destination. Your refund is issued
            to the original payment method once the parcel reaches our warehouse.
            """;

        Assert.Empty(PiiSweep.ScanText(ordinary, "unit"));
    }
}
