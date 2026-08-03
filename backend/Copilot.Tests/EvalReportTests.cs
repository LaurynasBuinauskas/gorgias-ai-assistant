using Copilot.Evals;

namespace Copilot.Tests;

/// <summary>
/// The report is what a go/no-go decision is read off, so what it is allowed to call PASS is
/// itself a tested property. The specific hazard here: class J asserts that no exemplar's
/// specifics reached the draft, which holds trivially when no exemplar was retrieved. Reported
/// as a green blocking class, that row claims the ticket corpus was checked when it was not.
/// </summary>
public sealed class EvalReportTests
{
    [Fact]
    public void ExemplarClassIsNotReportedAsPassingWhenExemplarsAreOff()
    {
        var (markdown, blockingFailure) = Report.Render([Passing("j-verbatim-reuse", "exemplar")],
            ticketTopK: 0);

        Assert.Contains("NOT EXERCISED", markdown);
        Assert.Contains("not exercised by this run", markdown);
        // Not a failure — exemplars being off is a configuration, not a regression.
        Assert.False(blockingFailure);
    }

    [Fact]
    public void ExemplarClassIsJudgedNormallyWhenExemplarsAreOn()
    {
        var (markdown, _) = Report.Render([Passing("j-verbatim-reuse", "exemplar")], ticketTopK: 3);

        Assert.DoesNotContain("NOT EXERCISED", markdown);
        Assert.Contains("1/1 (100%)", markdown);
    }

    [Fact]
    public void AnExercisedExemplarFailureStillBlocks()
    {
        var (_, blockingFailure) = Report.Render([Failing("j-verbatim-reuse", "exemplar")],
            ticketTopK: 3);

        Assert.True(blockingFailure);
    }

    [Fact]
    public void OtherClassesAreUnaffectedByTheExemplarSwitch()
    {
        var (markdown, blockingFailure) = Report.Render([Failing("c-returns-de", "market")],
            ticketTopK: 0);

        Assert.Contains("**FAIL**", markdown);
        Assert.True(blockingFailure);
    }

    private static CaseResult Passing(string id, string className) =>
        Result(id, className, held: true);

    private static CaseResult Failing(string id, string className) =>
        Result(id, className, held: false);

    private static CaseResult Result(string id, string className, bool held) =>
        new(new EvalCase { Id = id, Class = className },
            new DraftOutcome { Outcome = "drafted", Body = "a draft" },
            [new AssertionResult("assertion", held, held ? "" : "did not hold")]);
}
