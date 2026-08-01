using System.Text.RegularExpressions;
using Copilot.Domain;

namespace Copilot.Evals;

/// <param name="Name">Which assertion ran, so a failure names itself.</param>
/// <param name="Passed">Whether it held.</param>
/// <param name="Detail">Why it failed, in terms someone can act on.</param>
public sealed record AssertionResult(string Name, bool Passed, string Detail = "");

/// <summary>The draft a case produced, plus everything an assertion needs to judge it.</summary>
public sealed record DraftOutcome
{
    public required string Outcome { get; init; }

    public required string Body { get; init; }

    public IReadOnlyList<DraftCitation> Citations { get; init; } = [];

    public int ModelCalls { get; init; }
}

/// <summary>
/// Deterministic checks. These carry the release-blocking classes deliberately: string and
/// regex assertions are cheap, repeatable and immune to model mood, which a judge is not.
/// </summary>
public static class Assertions
{
    public static IReadOnlyList<AssertionResult> Evaluate(EvalCase testCase, DraftOutcome outcome)
    {
        var expect = testCase.Expect;
        var results = new List<AssertionResult>();

        foreach (var needle in expect.MustContain)
        {
            results.Add(new AssertionResult(
                $"must_contain({needle})",
                outcome.Body.Contains(needle, StringComparison.OrdinalIgnoreCase),
                $"draft does not contain \"{needle}\""));
        }

        foreach (var needle in expect.MustNotContain)
        {
            var found = outcome.Body.Contains(needle, StringComparison.OrdinalIgnoreCase);
            results.Add(new AssertionResult(
                $"must_not_contain({needle})",
                !found,
                found ? $"draft contains banned text \"{needle}\"" : ""));
        }

        foreach (var pattern in expect.MustMatch)
        {
            results.Add(new AssertionResult(
                $"must_match({pattern})",
                Regex.IsMatch(outcome.Body, pattern, RegexOptions.IgnoreCase),
                $"draft does not match /{pattern}/"));
        }

        foreach (var pattern in expect.MustNotMatch)
        {
            var match = Regex.Match(outcome.Body, pattern, RegexOptions.IgnoreCase);
            results.Add(new AssertionResult(
                $"must_not_match({pattern})",
                !match.Success,
                match.Success ? $"draft matched /{pattern}/ at \"{match.Value}\"" : ""));
        }

        if (expect.MustCiteMarket.Count > 0)
        {
            results.Add(CiteMarket(expect.MustCiteMarket, outcome));
        }

        if (expect.MustBe is { } required)
        {
            results.Add(new AssertionResult(
                $"must_be({required})",
                string.Equals(outcome.Outcome, required, StringComparison.OrdinalIgnoreCase),
                $"outcome was {outcome.Outcome}, expected {required}"));
        }

        if (expect.MustNotBe is { } forbidden)
        {
            var matched = string.Equals(outcome.Outcome, forbidden, StringComparison.OrdinalIgnoreCase);
            results.Add(new AssertionResult(
                $"must_not_be({forbidden})",
                !matched,
                matched ? $"outcome was {forbidden}" : ""));
        }

        if (expect.MinCitations is { } minimum)
        {
            results.Add(new AssertionResult(
                $"min_citations({minimum})",
                outcome.Citations.Count >= minimum,
                $"draft cited {outcome.Citations.Count} source(s), expected at least {minimum}"));
        }

        if (expect.NoModelCall == true)
        {
            results.Add(new AssertionResult(
                "no_model_call",
                outcome.ModelCalls == 0,
                $"the model was called {outcome.ModelCalls} time(s); a refusal should cost nothing"));
        }

        if (expect.Language is { } language)
        {
            results.Add(Language(language, outcome.Body));
        }

        return results;
    }

    /// <summary>
    /// Every cited chunk must belong to an allowed market. This is the assertion that catches
    /// a wrong-market answer — the failure that reads perfectly fluently and carries legal
    /// weight, and which no amount of spot-checking reliably finds.
    /// </summary>
    private static AssertionResult CiteMarket(List<string> allowed, DraftOutcome outcome)
    {
        if (outcome.Citations.Count == 0)
        {
            return new AssertionResult("must_cite_market", false,
                "draft cited nothing, so its market cannot be verified");
        }

        var wrong = outcome.Citations
            .Where(c => !allowed.Contains(c.Market, StringComparer.OrdinalIgnoreCase))
            .Select(c => $"{c.Label}@{c.Market}")
            .ToArray();

        return new AssertionResult(
            $"must_cite_market([{string.Join(", ", allowed)}])",
            wrong.Length == 0,
            wrong.Length == 0 ? "" : $"cited other markets: {string.Join(", ", wrong)}");
    }

    /// <summary>
    /// Language detection by stop-word frequency. Deliberately crude: it only needs to tell
    /// English from German, French, Spanish, Italian and Lithuanian on a support reply, and a
    /// dependency-free check that is obvious when wrong beats a library that is subtly wrong.
    /// </summary>
    private static AssertionResult Language(string expected, string body)
    {
        var detected = Detect(body);
        return new AssertionResult(
            $"language({expected})",
            string.Equals(detected, expected, StringComparison.OrdinalIgnoreCase),
            $"draft looks like '{detected}', expected '{expected}'");
    }

    public static string Detect(string body)
    {
        var words = Regex.Matches(body.ToLowerInvariant(), @"\p{L}+")
            .Select(m => m.Value)
            .ToHashSet();

        var scores = new Dictionary<string, int>
        {
            ["en"] = Count(words, ["the", "and", "you", "your", "we", "please", "with", "for", "that"]),
            ["de"] = Count(words, ["und", "sie", "ihre", "wir", "bitte", "mit", "nicht", "der", "die"]),
            ["fr"] = Count(words, ["et", "vous", "votre", "nous", "merci", "pour", "avec", "les", "que"]),
            ["es"] = Count(words, ["y", "usted", "su", "nosotros", "gracias", "para", "con", "los", "que"]),
            ["it"] = Count(words, ["e", "lei", "vostro", "noi", "grazie", "per", "con", "gli", "che"]),
            ["lt"] = Count(words, ["ir", "jūs", "jūsų", "mes", "ačiū", "prašome", "su", "kad"]),
        };

        var best = scores.MaxBy(pair => pair.Value);
        return best.Value == 0 ? "unknown" : best.Key;
    }

    private static int Count(HashSet<string> words, string[] markers) =>
        markers.Count(words.Contains);
}
