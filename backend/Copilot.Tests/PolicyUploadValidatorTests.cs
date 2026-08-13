using Copilot.Api.Uploads;

namespace Copilot.Tests;

/// <summary>
/// The boundary between what a client worker sends and what the ingest pipeline sees.
/// The refusals matter as much as the acceptances: each message must tell the uploader
/// what to change, because there is no developer in this loop by design.
/// </summary>
public sealed class PolicyUploadValidatorTests
{
    private static readonly PolicyUploadOptions s_options = new();

    private static string? Validate(
        string fileName = "returns.md",
        long size = 4_000,
        string market = "DE",
        string topic = "shipping-and-returns",
        string uploadedBy = "Rasa") =>
        PolicyUploadValidator.Validate(fileName, size, market, topic, uploadedBy, s_options);

    [Theory]
    [InlineData("returns.md")]
    [InlineData("Warranty Terms.docx")]
    public void AcceptsMarkdownAndWord(string fileName) =>
        Assert.Null(Validate(fileName: fileName));

    [Fact]
    public void RefusesPdfAndSaysWhy()
    {
        var refusal = Validate(fileName: "policy.pdf");

        Assert.NotNull(refusal);
        Assert.Contains("PDF", refusal);
        Assert.Contains("accented", refusal);
    }

    [Theory]
    [InlineData("policy.exe")]
    [InlineData("policy")]
    [InlineData("policy.md.html")]
    public void RefusesEveryOtherExtension(string fileName) =>
        Assert.NotNull(Validate(fileName: fileName));

    [Theory]
    [InlineData("../escape.md")]
    [InlineData("a/b.md")]
    [InlineData("..\\up.md")]
    public void RefusesPathShapedNames(string fileName) =>
        Assert.NotNull(Validate(fileName: fileName));

    [Fact]
    public void RefusesEmptyAndOversizedFiles()
    {
        Assert.NotNull(Validate(size: 0));
        Assert.NotNull(Validate(size: s_options.MaxFileBytes + 1));
        Assert.Null(Validate(size: s_options.MaxFileBytes));
    }

    [Fact]
    public void RefusesAnUnknownMarketAndListsTheRealOnes()
    {
        var refusal = Validate(market: "USA");

        Assert.NotNull(refusal);
        Assert.Contains("US", refusal);
        Assert.Contains("AU_NZ", refusal);
    }

    [Theory]
    [InlineData("Shipping")]
    [InlineData("shipping_and_returns")]
    [InlineData("a")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    public void RefusesMalformedTopics(string topic) =>
        Assert.NotNull(Validate(topic: topic));

    [Fact]
    public void RequiresAttribution()
    {
        Assert.NotNull(Validate(uploadedBy: ""));
        Assert.NotNull(Validate(uploadedBy: "   "));
        Assert.NotNull(Validate(uploadedBy: new string('x', 101)));
    }
}
