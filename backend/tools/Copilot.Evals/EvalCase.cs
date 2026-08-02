namespace Copilot.Evals;

/// <summary>
/// One eval case, as written in YAML. Cases are data rather than code so that adding
/// coverage — which beta feedback will demand constantly — needs no compilation.
/// </summary>
public sealed class EvalCase
{
    public string Id { get; set; } = "";

    /// <summary>Which class this belongs to; decides the threshold it is judged against.</summary>
    public string Class { get; set; } = "";

    /// <summary>File under <c>fixtures/tickets/</c>.</summary>
    public string Fixture { get; set; } = "";

    /// <summary>
    /// Market the harness resolves for this case. Supplied here rather than by the production
    /// resolver so market *filtering* can be tested before market *resolution* is answered —
    /// and so a case can deliberately force the wrong market to prove an assertion bites.
    /// </summary>
    public string Market { get; set; } = "GLOBAL";

    /// <summary>Optional agent instruction, as if typed into the panel.</summary>
    public string? Instruction { get; set; }

    public Expectations Expect { get; set; } = new();

    /// <summary>
    /// Inverts the verdict: the case passes when its assertions <i>fail</i>.
    ///
    /// For self-tests that prove the harness can detect a failure at all. Without this such a
    /// case has to sit permanently red, and a suite with a known-red case trains everyone to
    /// ignore red.
    /// </summary>
    public bool ExpectFailure { get; set; }

    public string SourcePath { get; set; } = "";
}

/// <summary>What must and must not be true of the draft. Every field is optional.</summary>
public sealed class Expectations
{
    public List<string> MustContain { get; set; } = [];

    public List<string> MustNotContain { get; set; } = [];

    public List<string> MustMatch { get; set; } = [];

    public List<string> MustNotMatch { get; set; } = [];

    /// <summary>Every cited chunk must belong to one of these markets.</summary>
    public List<string> MustCiteMarket { get; set; } = [];

    /// <summary>Pipeline outcome: <c>drafted</c> or <c>insufficient_data</c>.</summary>
    public string? MustBe { get; set; }

    public string? MustNotBe { get; set; }

    /// <summary>Two-letter code the draft must be written in, e.g. <c>en</c>.</summary>
    public string? Language { get; set; }

    /// <summary>The draft must cite at least this many resolvable sources.</summary>
    public int? MinCitations { get; set; }

    /// <summary>Assert the model was never called — proves a refusal cost nothing.</summary>
    public bool? NoModelCall { get; set; }

    /// <summary>
    /// Sweep the draft <b>and every retrieved ticket chunk</b> for personal data. Chunks are
    /// swept as well as the draft because a chunk carrying an email is a redaction defect
    /// whether or not the model quoted it this time.
    /// </summary>
    public bool? NoPii { get; set; }
}
