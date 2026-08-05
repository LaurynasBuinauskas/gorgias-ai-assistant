using System.Text.Json;
using Copilot.Api.Contracts;
using Copilot.Pipeline;

namespace Copilot.Tests;

/// <summary>
/// Pins the exact wire JSON of the stream's "progress" frames. The panel parses these keys
/// by name with no shared schema, so the byte-for-byte shape is the contract: a drifted key
/// would not fail anything at runtime, it would just leave the timeline silently empty.
/// The panel's own stream tests parse these same shapes back.
/// </summary>
public sealed class DraftStreamProgressV1Tests
{
    private static string Json(DraftChunk chunk) =>
        JsonSerializer.Serialize(DraftStreamProgressV1.From(chunk), JsonSerializerOptions.Web);

    [Fact]
    public void SearchedFrameCarriesTheKeysThePanelParses() =>
        Assert.Equal(
            """{"stage":"searched","market":"DE","signal":"recipientaddress","policy":4,"templates":1,"pastTickets":3,"internalGuides":2}""",
            Json(new DraftChunk.Searched("DE", "RecipientAddress", 4, 1, 3, 2)));

    [Theory]
    [InlineData(CoverageDecision.Passed, "passed")]
    [InlineData(CoverageDecision.Declined, "declined")]
    [InlineData(CoverageDecision.Skipped, "skipped")]
    public void CoverageFrameLowersTheDecision(CoverageDecision decision, string wire) =>
        Assert.Equal(
            $$"""{"stage":"coverage","decision":"{{wire}}"}""",
            Json(new DraftChunk.Coverage(decision)));

    [Fact]
    public void DraftingFrameIsBareStage() =>
        Assert.Equal("""{"stage":"drafting"}""", Json(new DraftChunk.Drafting()));

    [Fact]
    public void NonProgressChunksProduceNoFrame()
    {
        Assert.Null(DraftStreamProgressV1.From(new DraftChunk.Started("d1")));
        Assert.Null(DraftStreamProgressV1.From(new DraftChunk.Delta("text")));
        Assert.Null(DraftStreamProgressV1.From(new DraftChunk.Sources([])));
        Assert.Null(DraftStreamProgressV1.From(new DraftChunk.Insufficient("no")));
    }
}
