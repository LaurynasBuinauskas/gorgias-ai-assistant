using Azure;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Copilot.Knowledge;
using Microsoft.Extensions.Options;

namespace Copilot.Api.Uploads;

/// <summary>One live policy document, as retrieval actually serves it.</summary>
public sealed record PolicyDocument(string SourcePath, string Market, string Topic, int Chunks);

/// <summary>
/// What policy is currently live, read from the index rather than from git — after a
/// publish, the index is the truth and git may lag it. Policy corpus only: templates and
/// internal guidance are deliberately not listed on a page client workers use.
/// </summary>
public sealed class PolicyCatalog(IOptions<KnowledgeOptions> options)
{
    // Same credential fallback as AzureSearchKnowledgeStore: production has no Search key
    // in configuration — the App Service's managed identity is the credential — while local
    // and CI runs pass a key. The first deploy of this class proved the mismatch with a 500.
    private readonly SearchClient _client = string.IsNullOrWhiteSpace(options.Value.ApiKey)
        ? new SearchClient(
            new Uri(options.Value.Endpoint), options.Value.IndexName, new DefaultAzureCredential())
        : new SearchClient(
            new Uri(options.Value.Endpoint), options.Value.IndexName,
            new AzureKeyCredential(options.Value.ApiKey));

    public async Task<IReadOnlyList<PolicyDocument>> ListCurrentAsync(
        CancellationToken cancellationToken)
    {
        var response = await _client.SearchAsync<SearchDocument>("*", new SearchOptions
        {
            Filter = "corpus eq 'policy'",
            Size = 1000,
            Select = { "sourcePath", "market", "topic" },
        }, cancellationToken);

        var chunks = new List<(string SourcePath, string Market, string Topic)>();
        await foreach (var result in response.Value.GetResultsAsync())
        {
            chunks.Add((
                Field(result.Document, "sourcePath"),
                Field(result.Document, "market"),
                Field(result.Document, "topic")));
        }

        return Group(chunks);
    }

    /// <summary>GLOBAL first, then markets alphabetically — the order a policy page reads in.</summary>
    public static IReadOnlyList<PolicyDocument> Group(
        IEnumerable<(string SourcePath, string Market, string Topic)> chunks) =>
        [.. chunks
            .Where(chunk => chunk.SourcePath.Length > 0)
            .GroupBy(chunk => chunk.SourcePath)
            .Select(group => new PolicyDocument(
                group.Key, group.First().Market, group.First().Topic, group.Count()))
            .OrderBy(document => document.Market == "GLOBAL" ? 0 : 1)
            .ThenBy(document => document.Market, StringComparer.Ordinal)
            .ThenBy(document => document.Topic, StringComparer.Ordinal)];

    private static string Field(SearchDocument document, string name) =>
        document.TryGetValue(name, out var value) ? value?.ToString() ?? "" : "";
}
