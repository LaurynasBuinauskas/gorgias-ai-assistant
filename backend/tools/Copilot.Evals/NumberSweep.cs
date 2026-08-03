using System.Text.RegularExpressions;

namespace Copilot.Evals;

/// <summary>
/// Finds figures in a draft that appear in neither the ticket nor anything retrieved for it.
///
/// This exists because two fabrications got past the suite on the same day and neither could
/// have been caught by a pattern. The drafts invented a promo code discount and then a "10%
/// for new customers" promotion that appears in no policy document — while the *correct*
/// refusal reads "we are unable to offer a 60% discount", echoing the customer's own figure.
/// Any regex banning a percentage near "discount" fails the right answer, which is exactly how
/// the class D and injection assertions went wrong before.
///
/// What actually separates them is provenance, not vocabulary: a figure the model produced
/// from neither the ticket nor its sources is invented, whatever words surround it. That is
/// computable, and the harness already holds both halves.
///
/// Deliberately blunt about *which* numbers it checks — every one of them. A timeframe, a
/// percentage, a price and a quantity are the same failure wearing different clothes, and
/// carving out exceptions is how a check stops checking.
/// </summary>
public static class NumberSweep
{
    /// <summary>Digit runs, keeping decimal and thousands punctuation attached.</summary>
    private static readonly Regex s_number = new(@"\d+(?:[.,]\d+)*", RegexOptions.Compiled);

    /// <summary>
    /// Ordered list markers ("1." at the start of a line) and nothing else. A model laying out
    /// steps is not inventing a fact, and this is the one shape common enough to be worth
    /// excluding — every other number stays in scope.
    /// </summary>
    private static readonly Regex s_listMarker = new(@"^\s{0,3}\d{1,2}[.)]\s", RegexOptions.Compiled | RegexOptions.Multiline);

    public static IReadOnlyList<string> Unsourced(string draft, IEnumerable<string> sources)
    {
        if (string.IsNullOrWhiteSpace(draft))
        {
            return [];
        }

        var sourced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            foreach (var value in Numbers(source))
            {
                sourced.Add(value);
            }

            foreach (var value in SpelledNumbers(source))
            {
                sourced.Add(value);
            }
        }

        var withoutMarkers = s_listMarker.Replace(draft, " ");
        var findings = new List<string>();
        foreach (var value in Numbers(withoutMarkers))
        {
            if (!sourced.Contains(value) && !findings.Contains(value))
            {
                findings.Add(value);
            }
        }

        return findings;
    }

    /// <summary>
    /// Normalised so that a figure written one way in policy and another in a draft still
    /// matches: thousands separators dropped, trailing decimal zeros trimmed. Being generous
    /// here is deliberate — a false accusation of fabrication would get this check switched
    /// off, and a missed one only leaves the suite where it already was.
    /// </summary>
    private static IEnumerable<string> Numbers(string text)
    {
        foreach (Match match in s_number.Matches(text ?? ""))
        {
            yield return Normalise(match.Value);
        }
    }

    private static readonly Dictionary<string, int> s_units = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zero"] = 0, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5,
        ["six"] = 6, ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10,
        ["eleven"] = 11, ["twelve"] = 12, ["thirteen"] = 13, ["fourteen"] = 14,
        ["fifteen"] = 15, ["sixteen"] = 16, ["seventeen"] = 17, ["eighteen"] = 18,
        ["nineteen"] = 19,
    };

    private static readonly Dictionary<string, int> s_tens = new(StringComparer.OrdinalIgnoreCase)
    {
        ["twenty"] = 20, ["thirty"] = 30, ["forty"] = 40, ["fifty"] = 50,
        ["sixty"] = 60, ["seventy"] = 70, ["eighty"] = 80, ["ninety"] = 90,
    };

    private static readonly Regex s_word = new(@"[A-Za-z]+", RegexOptions.Compiled);

    /// <summary>
    /// Numbers a source writes out in words, so the draft may write them in digits.
    ///
    /// Found the hard way: a customer wrote "sixty percent off" and the correct refusal came
    /// back "unable to offer a 60% discount". Flagging that as invented would have been the
    /// same false accusation this check exists to avoid, and the first thing to get it
    /// switched off. English only — the drafts are always English, and a customer writing
    /// "sechzig" is writing a figure the draft will quote from the ticket in words too.
    /// </summary>
    private static IEnumerable<string> SpelledNumbers(string text)
    {
        var words = s_word.Matches(text ?? "").Select(match => match.Value).ToArray();
        for (var index = 0; index < words.Length; index++)
        {
            if (s_units.TryGetValue(words[index], out var unit))
            {
                yield return unit.ToString();
                continue;
            }

            if (!s_tens.TryGetValue(words[index], out var ten))
            {
                continue;
            }

            yield return ten.ToString();

            // "twenty-five" arrives as two words either way, the hyphen being punctuation.
            if (index + 1 < words.Length
                && s_units.TryGetValue(words[index + 1], out var trailing)
                && trailing is > 0 and < 10)
            {
                yield return (ten + trailing).ToString();
            }
        }
    }

    private static string Normalise(string raw)
    {
        // "1,000" and "1.000" are both a thousand somewhere in Europe; "2.5" is not. Treat a
        // separator followed by exactly three digits as thousands, anything else as a decimal.
        var value = Regex.Replace(raw, @"[.,](?=\d{3}(?:\D|$))", "");
        if (value.Contains('.') || value.Contains(','))
        {
            value = value.Replace(',', '.').TrimEnd('0').TrimEnd('.');
        }

        return value.TrimStart('0') is { Length: > 0 } trimmed ? trimmed : "0";
    }
}
