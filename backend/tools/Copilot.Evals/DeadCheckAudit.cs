using System.Text.RegularExpressions;

namespace Copilot.Evals;

/// <summary>
/// Proves every banned-text assertion can still fire.
///
/// A <c>must_not_*</c> assertion fails open: broken, it passes forever and reports nothing.
/// Class E ran for weeks with 20 assertions that could never match because a YAML
/// double-quoted <c>"\b"</c> had become a literal backspace byte — and four other checks
/// were found equally dead in the same day, all by accident. This audit makes that search
/// systematic: each case carries <c>must_flag</c> examples of the bad drafts it exists to
/// catch, and a pattern none of them can trip is reported as dead before a single model call
/// is paid for.
///
/// Canaries are judged by <see cref="Assertions.Evaluate"/> itself, not a reimplementation,
/// so the audit can never drift from what the real run would do.
/// </summary>
public static class DeadCheckAudit
{
    public static IReadOnlyList<string> Audit(EvalCase testCase)
    {
        var expect = testCase.Expect;
        var violations = new List<string>();

        ScanText(expect.MustContain, "must_contain", violations);
        ScanText(expect.MustNotContain, "must_not_contain", violations);
        ScanPatterns(expect.MustMatch, "must_match", violations);
        ScanPatterns(expect.MustNotMatch, "must_not_match", violations);

        var bannedTextCount = expect.MustNotContain.Count + expect.MustNotMatch.Count;
        if (bannedTextCount > 0 && testCase.MustFlag.Count == 0)
        {
            violations.Add(
                $"{bannedTextCount} banned-text assertion(s) but no must_flag examples — "
                + "nothing proves they can fire");
        }

        // A pattern that no longer compiles would throw inside Evaluate; the scans above have
        // already reported it, so skip the canary pass rather than crash it.
        if (violations.Count > 0)
        {
            return violations;
        }

        var fired = new HashSet<string>();
        foreach (var canary in testCase.MustFlag)
        {
            var outcome = new DraftOutcome { Outcome = "drafted", Body = canary };
            var tripped = Assertions.Evaluate(testCase, outcome)
                .Where(result => !result.Passed && IsBannedText(result.Name))
                .Select(result => result.Name)
                .ToList();

            if (tripped.Count == 0)
            {
                violations.Add(
                    $"must_flag example trips no banned-text assertion: \"{Excerpt(canary)}\"");
            }

            fired.UnionWith(tripped);
        }

        foreach (var needle in expect.MustNotContain.Where(
                     needle => !fired.Contains($"must_not_contain({needle})")))
        {
            violations.Add($"dead check: must_not_contain(\"{needle}\") fires on no must_flag example");
        }

        foreach (var pattern in expect.MustNotMatch.Where(
                     pattern => !fired.Contains($"must_not_match({pattern})")))
        {
            violations.Add($"dead check: must_not_match(/{pattern}/) fires on no must_flag example");
        }

        return violations;
    }

    private static bool IsBannedText(string assertionName) =>
        assertionName.StartsWith("must_not_contain(", StringComparison.Ordinal)
        || assertionName.StartsWith("must_not_match(", StringComparison.Ordinal);

    private static void ScanPatterns(List<string> patterns, string field, List<string> violations)
    {
        ScanText(patterns, field, violations);
        foreach (var pattern in patterns)
        {
            try
            {
                _ = new Regex(pattern, RegexOptions.IgnoreCase);
            }
            catch (ArgumentException error)
            {
                violations.Add($"{field}(/{pattern}/) is not a valid regex: {error.Message}");
            }
        }
    }

    /// <summary>
    /// A control character inside a pattern is how class E died: YAML turns a double-quoted
    /// <c>"\b"</c> into a backspace byte, which the regex engine then matches against nothing.
    /// </summary>
    private static void ScanText(List<string> values, string field, List<string> violations)
    {
        foreach (var value in values)
        {
            char? control = value.Where(char.IsControl).Cast<char?>().FirstOrDefault();
            if (control is not null)
            {
                violations.Add(
                    $"{field}(\"{Excerpt(value)}\") contains control character 0x{(int)control.Value:X2}"
                    + " — a YAML double-quoted \"\\b\" becomes a literal backspace byte;"
                    + " single-quote the value so the escape stays two characters");
            }
        }
    }

    private static string Excerpt(string value)
    {
        var printable = string.Concat(value.Select(c => char.IsControl(c) ? '\u2400' : c));
        return printable.Length <= 60 ? printable : printable[..57] + "...";
    }
}
