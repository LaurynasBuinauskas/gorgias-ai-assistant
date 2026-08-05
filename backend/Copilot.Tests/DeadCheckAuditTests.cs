using Copilot.Evals;

namespace Copilot.Tests;

/// <summary>
/// The audit that keeps banned-text assertions honest. The defining regression is class E:
/// twenty <c>must_not_match</c> assertions carried a literal backspace byte where <c>\b</c>
/// was intended and passed unconditionally for weeks. Each test here is one of the ways a
/// check has actually died in this repository, plus the ways the audit itself could lie.
/// </summary>
public sealed class DeadCheckAuditTests
{
    private static EvalCase Case(Action<EvalCase> configure)
    {
        var testCase = new EvalCase { Id = "audit-test", Class = "fabrication" };
        configure(testCase);
        return testCase;
    }

    [Fact]
    public void PassesWhenEveryBannedPatternFiresOnACanary()
    {
        var testCase = Case(c =>
        {
            c.Expect.MustNotMatch = [@"\b\d+\s*%\s*(discount|off)\b"];
            c.Expect.MustNotContain = ["ITALY10"];
            c.MustFlag =
            [
                "We can offer you a 10% discount on your next order.",
                "Please use the code ITALY10 at checkout.",
            ];
        });

        Assert.Empty(DeadCheckAudit.Audit(testCase));
    }

    [Fact]
    public void JudgesCanariesCaseInsensitively_LikeTheRealAssertions()
    {
        // The audit must share Assertions.Evaluate's semantics exactly; an audit that judged
        // case-sensitively would report a live check as dead.
        var testCase = Case(c =>
        {
            c.Expect.MustNotContain = ["ITALY10"];
            c.MustFlag = ["please use the code italy10 at checkout."];
        });

        Assert.Empty(DeadCheckAudit.Audit(testCase));
    }

    [Fact]
    public void ReportsTheBackspaceByteThatKilledClassE()
    {
        // "\b" in double-quoted YAML — the literal byte, exactly as the dead files carried it.
        var testCase = Case(c =>
        {
            c.Expect.MustNotMatch = ["(guarantee|promise)[^.]{0,60}\\d"];
            c.MustFlag = ["We guarantee delivery within 5 business days."];
        });

        var violations = DeadCheckAudit.Audit(testCase);

        Assert.Contains(violations, v => v.Contains("control character 0x08"));
    }

    [Fact]
    public void ReportsAPatternNoCanaryCanTrip()
    {
        var testCase = Case(c =>
        {
            c.Expect.MustNotMatch = [@"\bUSPS\b", @"\bHermes\b"];
            c.MustFlag = ["Your parcel was handed to USPS."];
        });

        var violations = DeadCheckAudit.Audit(testCase);

        Assert.Single(violations);
        Assert.Contains("Hermes", violations[0]);
        Assert.StartsWith("dead check", violations[0]);
    }

    [Fact]
    public void ReportsACanaryThatNothingFlags()
    {
        // A bad example the assertions cannot see is a coverage hole, found exactly the way
        // the discount fabrication was: someone writes down the bad draft, and nothing fires.
        var testCase = Case(c =>
        {
            c.Expect.MustNotContain = ["ITALY10"];
            c.MustFlag =
            [
                "Please use the code ITALY10 at checkout.",
                "We offer a 10% discount to new customers.",
            ];
        });

        var violations = DeadCheckAudit.Audit(testCase);

        Assert.Single(violations);
        Assert.Contains("trips no banned-text assertion", violations[0]);
    }

    [Fact]
    public void ReportsBannedTextAssertionsWithNoCanariesAtAll()
    {
        var testCase = Case(c => c.Expect.MustNotContain = ["Asana", "Odoo"]);

        var violations = DeadCheckAudit.Audit(testCase);

        Assert.Single(violations);
        Assert.Contains("no must_flag examples", violations[0]);
    }

    [Fact]
    public void ReportsARegexThatDoesNotCompile()
    {
        var testCase = Case(c =>
        {
            c.Expect.MustNotMatch = ["(unclosed"];
            c.MustFlag = ["anything"];
        });

        var violations = DeadCheckAudit.Audit(testCase);

        Assert.Contains(violations, v => v.Contains("not a valid regex"));
    }

    [Fact]
    public void RequiresNothingOfACaseWithoutBannedTextAssertions()
    {
        // Positive assertions fail loudly when broken — a dead must_contain is permanently
        // red, not silently green — so they need no canaries.
        var testCase = Case(c =>
        {
            c.Expect.MustContain = ["return label"];
            c.Expect.MustBe = "drafted";
        });

        Assert.Empty(DeadCheckAudit.Audit(testCase));
    }
}
