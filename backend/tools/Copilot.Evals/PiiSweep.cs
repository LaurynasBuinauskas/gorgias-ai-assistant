using System.Text.RegularExpressions;
using Copilot.Knowledge;

namespace Copilot.Evals;

/// <param name="Pattern">Which identifier class matched.</param>
/// <param name="Where">Draft, or the chunk id it was found in.</param>
/// <param name="Sample">A masked excerpt — enough to locate, not enough to re-leak.</param>
public sealed record PiiFinding(string Pattern, string Where, string Sample);

/// <summary>
/// Sweeps drafts and retrieved ticket chunks for personal data.
///
/// This is the **second** control on redaction, and it exists because the first one cannot
/// check itself. The extraction pipeline refuses to index a batch that still matches
/// identifier patterns; this tests what actually reached the index, using different code, so a
/// bug in the pipeline's own checker cannot hide behind it.
///
/// It sweeps retrieved chunks as well as the draft on purpose. A chunk carrying a customer's
/// email is a redaction defect whether or not the model happened to quote it — the leak is in
/// the index, and the next draft may not be so tactful.
/// </summary>
public static class PiiSweep
{
    /// <summary>
    /// Listed so the report can state exactly what was checked. An assertion whose coverage is
    /// implied rather than printed invites confidence it has not earned.
    /// </summary>
    public static readonly (string Name, Regex Pattern)[] Patterns =
    [
        ("email", new Regex(@"\b[\w.+-]+@[\w-]+\.[\w.-]{2,}\b", RegexOptions.Compiled)),
        ("phone", new Regex(@"(?<![\w#/-])\+?\d[\d\s().-]{8,17}\d(?![\w/-])", RegexOptions.Compiled)),
        ("iban", new Regex(@"\b[A-Z]{2}\d{2}[ ]?(?:[A-Z0-9]{4}[ ]?){2,7}[A-Z0-9]{1,4}\b", RegexOptions.Compiled)),
        ("card", new Regex(@"\b(?:\d{4}[ -]?){3}\d{4}\b", RegexOptions.Compiled)),
        ("uk-postcode", new Regex(@"\b[A-Z]{1,2}\d[A-Z\d]?\s*\d[A-Z]{2}\b", RegexOptions.Compiled)),
        ("nl-postcode", new Regex(@"\b\d{4}\s?[A-Z]{2}\b", RegexOptions.Compiled)),
        ("ca-postcode", new Regex(@"\b[A-Z]\d[A-Z]\s?\d[A-Z]\d\b", RegexOptions.Compiled)),
        ("order-reference", new Regex(@"#[A-Z]{2,3}#\d{3,7}", RegexOptions.Compiled)),
        ("tracking-number", new Regex(@"\b\d{12,22}\b", RegexOptions.Compiled)),
    ];

    /// <summary>Placeholders are the expected output of redaction, not a leak.</summary>
    private static readonly Regex s_placeholders = new(
        @"\[(?:CUSTOMER|AGENT|EMAIL|PHONE|ORDER|TRACKING|ADDRESS|POSTCODE|IBAN|CARD|SIGNATURE)\]",
        RegexOptions.Compiled);

    public static IReadOnlyList<PiiFinding> Sweep(string draft, IEnumerable<KnowledgeChunk> ticketChunks)
    {
        var findings = new List<PiiFinding>();
        findings.AddRange(Scan(draft, "draft"));

        foreach (var chunk in ticketChunks)
        {
            findings.AddRange(Scan($"{chunk.Title}\n{chunk.Content}", $"chunk:{chunk.Id}"));
        }

        return findings;
    }

    private static IEnumerable<PiiFinding> Scan(string text, string where)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        var masked = s_placeholders.Replace(text, " ");
        foreach (var (name, pattern) in Patterns)
        {
            var match = pattern.Match(masked);
            if (match.Success)
            {
                yield return new PiiFinding(name, where, Mask(match.Value));
            }
        }
    }

    /// <summary>
    /// Masks the middle of a hit. A report that reproduces the leak in full is itself a leak,
    /// and it gets committed, pasted into tickets and read by people who did not need to see it.
    /// </summary>
    private static string Mask(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= 4
            ? new string('*', trimmed.Length)
            : $"{trimmed[..2]}{new string('*', Math.Min(trimmed.Length - 4, 12))}{trimmed[^2..]}";
    }
}
