using Copilot.Knowledge;
using Copilot.Pipeline;

namespace Copilot.Tests;

/// <summary>
/// The splitter decides what the customer sees and what the agent is told the draft was based
/// on. Both halves fail together when it misses the delimiter: the marker and the labels stay
/// in the reply an agent copies, and the sources list comes back empty.
///
/// It matched the delimiter literally until a draft wrote `---\nSOURCES---`, split over a line
/// break. That failed in the most misleading way available — the diagnostic reported "the model
/// never emitted a sources block", which was untrue, and the eval failure it produced was an
/// unrelated one about invented figures, because "P2" and "P4" were still sitting in the body.
/// </summary>
public sealed class SourceSplitterTests
{
    private static readonly Dictionary<string, KnowledgeChunk> Citable = new()
    {
        ["P1"] = Chunk("P1"),
        ["P2"] = Chunk("P2"),
    };

    [Theory]
    [InlineData("---SOURCES---")]
    [InlineData("---\nSOURCES---")]          // the shape found in a real draft
    [InlineData("---SOURCES---\n")]
    [InlineData("--- SOURCES ---")]
    [InlineData("----SOURCES----")]
    [InlineData("---sources---")]
    [InlineData("---\n SOURCES \n---")]
    public void RecognisesTheDelimiterHoweverTheModelWritesIt(string delimiter)
    {
        var splitter = SourceSplitterExtensions.SplitAll(
            $"Dear Robin,\n\nYou have 30 days to return it.\n\n{delimiter}\nP1\nP2");

        Assert.Equal("Dear Robin,\n\nYou have 30 days to return it.", splitter.Body);
        Assert.True(splitter.EmittedSourcesBlock);
        Assert.Equal(["P1", "P2"], splitter.ResolveCitations(Citable).Select(c => c.Label));
    }

    [Fact]
    public void LeavesTheReplyAloneWhenNoDelimiterArrives()
    {
        const string reply = "Dear Robin,\n\nWe cannot confirm that. Best regards, Support";
        var splitter = SourceSplitterExtensions.SplitAll(reply);

        Assert.Equal(reply, splitter.Body);
        Assert.False(splitter.EmittedSourcesBlock);
        Assert.Empty(splitter.ResolveCitations(Citable));
    }

    [Fact]
    public void NeverLeaksAPartialDelimiterWhileStreaming()
    {
        // The reason the splitter exists rather than a string split: an agent copying the reply
        // mid-stream must never find "---SOU" at the end of it.
        var splitter = new SourceSplitter();
        var emitted = "";
        foreach (var piece in new[] { "You have 30 ", "days.\n\n---", "\nSOU", "RCES---\nP1" })
        {
            emitted += splitter.Push(piece);
        }

        emitted += splitter.Complete();

        Assert.DoesNotContain("SOU", emitted);
        Assert.Equal("You have 30 days.", splitter.Body);
        Assert.Equal(["P1"], splitter.ResolveCitations(Citable).Select(c => c.Label));
    }

    [Fact]
    public void ReportsLabelsThatResolveToNothing()
    {
        // An internal label is deliberately unresolvable, and saying so is the difference
        // between "the model cited nothing" and "the model cited something it was shown".
        var splitter = SourceSplitterExtensions.SplitAll("A reply.\n---SOURCES---\nP1\nI3");

        Assert.Equal(["P1"], splitter.ResolveCitations(Citable).Select(c => c.Label));
        Assert.Equal(["I3"], splitter.UnresolvedLabels);
    }

    private static KnowledgeChunk Chunk(string label) => new()
    {
        Id = $"chunk-{label}",
        Title = label,
        Content = "content",
        Market = "GLOBAL",
        Topic = "returns",
        SourcePath = $"knowledge/policy/GLOBAL/{label}.md",
        Score = 2.5,
    };
}
