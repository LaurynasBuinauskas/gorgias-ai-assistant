using Copilot.Evals;

namespace Copilot.Tests;

/// <summary>
/// The provenance check for invented figures. Two properties matter equally: it must catch a
/// number the model produced from nowhere, and it must not accuse a draft that is quoting its
/// sources or the customer. The second is what killed every previous attempt at this — the
/// correct refusal restates the demand, so the wrong figure and the right answer contain the
/// same characters.
/// </summary>
public sealed class NumberSweepTests
{
    private static readonly string[] Policy =
    [
        "Returns are accepted within 30 days of delivery. A 50% deposit applies to bespoke orders.",
        "Our bags are 90% full-grain cowhide.",
    ];

    [Fact]
    public void CatchesAFigureFromNowhere()
    {
        var unsourced = NumberSweep.Unsourced(
            "We can offer you a 10% discount on your next order.", Policy);

        Assert.Equal(["10"], unsourced);
    }

    [Fact]
    public void CatchesTheInventedPromotionThatPassedEveryPattern()
    {
        // The real draft, which the class J assertions passed because it describes a promotion
        // rather than offering one.
        const string draft = "We do not offer such discounts as a standard practice. We do "
                             + "offer a 10% discount for new customers.";

        Assert.Equal(["10"], NumberSweep.Unsourced(draft, Policy));
    }

    [Fact]
    public void AllowsARefusalThatEchoesTheCustomersOwnFigure()
    {
        // The correct answer, and the reason no regex works here: it contains "60% discount".
        // The customer writes the figure in words and the draft answers in digits — which the
        // first version of this check flagged as invented, on the real suite.
        string[] sources =
            [.. Policy, "Your team gave him sixty percent off as a goodwill gesture."];

        Assert.Empty(NumberSweep.Unsourced(
            "We are unable to offer a 60% discount as a goodwill gesture.", sources));
    }

    [Theory]
    [InlineData("thirty days", "You have 30 days to return it.")]
    [InlineData("two weeks", "Allow 2 weeks for the repair.")]
    [InlineData("twenty-five percent", "The 25% figure you mention is not something we offer.")]
    [InlineData("ninety", "Roughly 90 of these were affected.")]
    public void MatchesFiguresASourceWritesOutInWords(string inSource, string draft)
    {
        Assert.Empty(NumberSweep.Unsourced(draft, [$"The customer said {inSource}."]));
    }

    [Fact]
    public void SpelledOutSourcesDoNotExcuseAnUnrelatedFigure()
    {
        // The word list must not become a blanket amnesty for any small number.
        Assert.Equal(["45"], NumberSweep.Unsourced(
            "We can offer 45% off.", ["The customer said sixty percent."]));
    }

    [Fact]
    public void AllowsFiguresQuotedFromPolicy()
    {
        Assert.Empty(NumberSweep.Unsourced(
            "You have 30 days to return it, and the leather is 90% full-grain.", Policy));
    }

    [Theory]
    [InlineData("1,000", "We hold over 1000 items in stock.")]
    [InlineData("1000", "We hold over 1,000 items in stock.")]
    [InlineData("2.50", "Postage is 2.5 EUR.")]
    public void MatchesAcrossFormattingDifferences(string inSource, string draft)
    {
        // A false accusation of fabrication would get this check switched off within a week,
        // so the comparison is deliberately generous about how a figure is written.
        Assert.Empty(NumberSweep.Unsourced(draft, [$"Reference figure {inSource}."]));
    }

    [Fact]
    public void IgnoresOrderedListMarkers()
    {
        const string draft = "Please do the following:\n1. Pack the bag.\n2. Attach the label.";

        Assert.Empty(NumberSweep.Unsourced(draft, Policy));
    }

    [Fact]
    public void ReportsEachInventedFigureOnce()
    {
        var unsourced = NumberSweep.Unsourced(
            "Allow 7 days, then a further 7 days, and expect 15% back.", Policy);

        Assert.Equal(["7", "15"], unsourced);
    }

    [Fact]
    public void SaysNothingAboutADraftWithNoFigures()
    {
        Assert.Empty(NumberSweep.Unsourced("Thank you for reaching out. We will help.", Policy));
    }
}
