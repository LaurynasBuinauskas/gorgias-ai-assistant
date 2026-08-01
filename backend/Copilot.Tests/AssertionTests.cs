using Copilot.Domain;
using Copilot.Evals;

namespace Copilot.Tests;

/// <summary>
/// The assertions carry the release-blocking eval classes, so they are themselves tested.
/// An assertion that cannot fail passes everything, and a suite built on one measures nothing.
/// </summary>
public sealed class AssertionTests
{
    [Fact]
    public void MustContainPassesWhenPresentAndFailsWhenAbsent()
    {
        Assert.True(Run(Draft("Returns are accepted within 30 days."),
            e => e.MustContain = ["30 days"]).Passed);
        Assert.False(Run(Draft("We cannot help with that."),
            e => e.MustContain = ["30 days"]).Passed);
    }

    [Fact]
    public void MustNotContainCatchesBannedVocabularyRegardlessOfCase()
    {
        var result = Run(Draft("I have logged this in our ASANA board."),
            e => e.MustNotContain = ["Asana"]);

        Assert.False(result.Passed);
        Assert.Contains("banned text", result.Detail);
    }

    [Fact]
    public void MustMatchAndMustNotMatchApplyRegularExpressions()
    {
        Assert.True(Run(Draft("Your refund arrives in 5 business days."),
            e => e.MustMatch = [@"\d+\s*business days"]).Passed);

        var fabricated = Run(Draft("We will refund you within 14 days."),
            e => e.MustNotMatch = [@"\d+\s*(days?|months?|years?|%)"]);
        Assert.False(fabricated.Passed);
        // The detail must quote what matched, or a failure is unactionable.
        Assert.Contains("14 days", fabricated.Detail);
    }

    [Fact]
    public void MustCiteMarketFailsWhenACitedChunkBelongsToAnotherMarket()
    {
        var result = Run(
            Draft("You may return within 30 days.", Citation("P1", "US")),
            e => e.MustCiteMarket = ["DE", "GLOBAL"]);

        Assert.False(result.Passed);
        Assert.Contains("P1@US", result.Detail);
    }

    [Fact]
    public void MustCiteMarketAcceptsTheResolvedMarketAndGlobalTogether()
    {
        Assert.True(Run(
            Draft("Text.", Citation("P1", "DE"), Citation("P2", "GLOBAL")),
            e => e.MustCiteMarket = ["DE", "GLOBAL"]).Passed);
    }

    [Fact]
    public void MustCiteMarketFailsWhenNothingWasCited()
    {
        // A draft that cites nothing cannot be shown to be in the right market. Passing it
        // would let an uncited draft satisfy the strictest class in the suite.
        var result = Run(Draft("You may return within 30 days."),
            e => e.MustCiteMarket = ["DE"]);

        Assert.False(result.Passed);
        Assert.Contains("cited nothing", result.Detail);
    }

    [Fact]
    public void OutcomeAssertionsDistinguishDraftedFromInsufficient()
    {
        Assert.True(Run(Draft("text"), e => e.MustBe = "drafted").Passed);
        Assert.False(Run(Draft("text"), e => e.MustBe = "insufficient_data").Passed);
        Assert.False(Run(Draft("text"), e => e.MustNotBe = "drafted").Passed);
    }

    [Fact]
    public void NoModelCallProvesARefusalHappenedBeforeTheModel()
    {
        var declined = new DraftOutcome
        {
            Outcome = "insufficient_data", Body = "Not covered.", ModelCalls = 0,
        };
        var wasteful = declined with { ModelCalls = 1 };

        Assert.True(Assertions.Evaluate(Case(e => e.NoModelCall = true), declined).All(a => a.Passed));

        var result = Assertions.Evaluate(Case(e => e.NoModelCall = true), wasteful).Single();
        Assert.False(result.Passed);
        Assert.Contains("should cost nothing", result.Detail);
    }

    [Fact]
    public void MinCitationsRequiresGroundedDrafts()
    {
        Assert.False(Run(Draft("Ungrounded claim."), e => e.MinCitations = 1).Passed);
        Assert.True(Run(Draft("Grounded.", Citation("P1", "DE")), e => e.MinCitations = 1).Passed);
    }

    [Theory]
    [InlineData("Thank you for your message. We will refund your order within 30 days.", "en")]
    [InlineData("Vielen Dank für Ihre Nachricht. Wir werden Ihre Bestellung nicht stornieren.", "de")]
    [InlineData("Merci pour votre message. Nous vous remercions pour votre patience et votre confiance.", "fr")]
    [InlineData("Gracias por su mensaje. Nosotros procesaremos su pedido para usted con los detalles que nos dio.", "es")]
    public void LanguageDetectionClassifiesSupportReplies(string body, string expected)
    {
        Assert.Equal(expected, Assertions.Detect(body));
    }

    [Fact]
    public void LanguageAssertionFailsOnTheWrongLanguage()
    {
        var result = Run(Draft("Vielen Dank für Ihre Nachricht, wir melden uns bei Ihnen."),
            e => e.Language = "en");

        Assert.False(result.Passed);
        Assert.Contains("looks like 'de'", result.Detail);
    }

    [Fact]
    public void AnEmptyExpectationBlockAssertsNothing()
    {
        // Guards against a case file whose expectations were mistyped silently passing.
        Assert.Empty(Assertions.Evaluate(Case(_ => { }), Draft("anything")));
    }

    private static AssertionResult Run(DraftOutcome outcome, Action<Expectations> configure) =>
        Assertions.Evaluate(Case(configure), outcome).Single();

    private static EvalCase Case(Action<Expectations> configure)
    {
        var expectations = new Expectations();
        configure(expectations);
        return new EvalCase { Id = "t", Class = "smoke", Expect = expectations };
    }

    private static DraftOutcome Draft(string body, params DraftCitation[] citations) => new()
    {
        Outcome = "drafted",
        Body = body,
        Citations = citations,
        ModelCalls = 1,
    };

    private static DraftCitation Citation(string label, string market) =>
        new(label, $"chunk-{label}", $"knowledge/policy/{market}/shipping-and-returns.md", market);
}
