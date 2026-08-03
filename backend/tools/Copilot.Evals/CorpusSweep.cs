using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;

namespace Copilot.Evals;

/// <summary>
/// Runs the PII patterns over <b>every</b> indexed exemplar, not just the handful a run of the
/// eval suite happens to retrieve.
///
/// Eval class I sweeps the chunks its own queries pull back — a few dozen documents out of
/// 17,863. That was a reasonable sample while the corpus was switched off and nothing reached
/// an agent. It became an inadequate one the moment exemplars went live, because the coverage
/// of that check is decided by which questions the fixtures happen to ask.
///
/// This is not a substitute for the human review. Every leak class found so far was found by a
/// person reading exchanges, and none of them matched a pattern — that is precisely why the
/// review exists. What this can do is prove the pattern-shaped classes are at zero across the
/// whole corpus rather than across a sample, and say so with a number.
/// </summary>
public static class CorpusSweep
{
    private const int PageSize = 1000;

    public sealed record Result(int Documents, IReadOnlyList<PiiFinding> Findings);

    public static async Task<Result> RunAsync(
        Uri endpoint,
        string index,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var client = new SearchClient(endpoint, index, new AzureKeyCredential(apiKey));
        var findings = new List<PiiFinding>();
        var scanned = 0;

        // Paged with skip rather than a continuation token: Search caps skip at 100,000 and the
        // corpus is a fifth of that, so the simple loop is the honest one.
        for (var skip = 0; ; skip += PageSize)
        {
            var options = new SearchOptions
            {
                Size = PageSize,
                Skip = skip,
                Select = { "id", "ticketId", "title", "content" },
            };

            var response = await client.SearchAsync<SearchDocument>("*", options, cancellationToken);
            var page = 0;

            await foreach (var result in response.Value.GetResultsAsync()
                               .WithCancellation(cancellationToken))
            {
                page++;
                scanned++;

                var document = result.Document;
                var ticketId = Field(document, "ticketId");
                var where = ticketId.Length > 0 ? $"ticket {ticketId}" : Field(document, "id");
                var text = $"{Field(document, "title")}\n{Field(document, "content")}";

                findings.AddRange(PiiSweep.ScanText(text, where));
            }

            if (page < PageSize)
            {
                break;
            }

            Console.Write($"  swept {scanned:N0}\r");
        }

        Console.Write(new string(' ', 24));
        Console.Write('\r');
        return new Result(scanned, findings);
    }

    public static string Render(Result result)
    {
        var report = new System.Text.StringBuilder();
        report.AppendLine("# Exemplar corpus PII sweep");
        report.AppendLine();
        report.AppendLine($"Run: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC · "
                          + $"{result.Documents:N0} document(s) · "
                          + $"{PiiSweep.Patterns.Length} pattern(s)");
        report.AppendLine();
        report.AppendLine("| Pattern | Documents |");
        report.AppendLine("|---|---|");

        var byPattern = result.Findings
            .GroupBy(f => f.Pattern)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var (name, _) in PiiSweep.Patterns)
        {
            var hits = byPattern.TryGetValue(name, out var list) ? list.Count : 0;
            report.AppendLine($"| {name} | {hits:N0} |");
        }

        report.AppendLine();
        if (result.Findings.Count == 0)
        {
            report.AppendLine("No pattern-shaped personal data found in any indexed exemplar.");
            report.AppendLine();
            report.AppendLine("This does **not** mean the corpus is clean. Every leak class found "
                              + "so far was found by a person reading exchanges and matched no "
                              + "pattern. See `open-questions.md` D-3.");
            return report.ToString();
        }

        report.AppendLine($"## Findings ({result.Findings.Count})");
        report.AppendLine();
        report.AppendLine("Samples are masked; enough to locate the exchange, not enough to "
                          + "re-leak it.");
        report.AppendLine();

        foreach (var (pattern, hits) in byPattern.OrderByDescending(p => p.Value.Count))
        {
            report.AppendLine($"### {pattern} — {hits.Count:N0}");
            report.AppendLine();
            foreach (var finding in hits.Take(20))
            {
                report.AppendLine($"- `{finding.Where}` — {finding.Sample}");
            }

            if (hits.Count > 20)
            {
                report.AppendLine($"- …and {hits.Count - 20:N0} more");
            }

            report.AppendLine();
        }

        return report.ToString();
    }

    private static string Field(SearchDocument document, string name) =>
        document.TryGetValue(name, out var value) ? value?.ToString() ?? "" : "";
}
