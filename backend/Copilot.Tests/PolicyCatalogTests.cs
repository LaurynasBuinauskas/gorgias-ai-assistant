using Copilot.Api.Uploads;

namespace Copilot.Tests;

/// <summary>
/// The grouping behind "what policy is live right now": chunks collapse into documents,
/// and the order is the one a policy page reads in — GLOBAL first, then markets.
/// </summary>
public sealed class PolicyCatalogTests
{
    [Fact]
    public void CollapsesChunksIntoDocumentsInReadingOrder()
    {
        var documents = PolicyCatalog.Group(
        [
            ("knowledge/policy/US/warranty.md", "US", "warranty"),
            ("knowledge/policy/DE/returns.md", "DE", "shipping-and-returns"),
            ("knowledge/policy/DE/returns.md", "DE", "shipping-and-returns"),
            ("knowledge/policy/GLOBAL/faqs.md", "GLOBAL", "faqs"),
            ("staged/policy/DE/care.md", "DE", "care"),
        ]);

        Assert.Equal(
            ["knowledge/policy/GLOBAL/faqs.md", "staged/policy/DE/care.md",
             "knowledge/policy/DE/returns.md", "knowledge/policy/US/warranty.md"],
            documents.Select(d => d.SourcePath));
        Assert.Equal(2, documents.Single(d => d.Topic == "shipping-and-returns").Chunks);
    }

    [Fact]
    public void DropsChunksWithoutASourcePath()
    {
        var documents = PolicyCatalog.Group([("", "DE", "x")]);

        Assert.Empty(documents);
    }
}
