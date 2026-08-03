namespace Copilot.Pipeline;

/// <summary>
/// How much knowledge to retrieve, and how sure we must be before answering at all.
/// </summary>
public sealed class RetrievalOptions
{
    public const string SectionName = "Retrieval";

    /// <summary>
    /// Rollback lever 2 from `launch-plan.md` §9: turning this off bypasses retrieval and
    /// reverts to the ticket-only prompt, degrading quality to today's known-acceptable
    /// behaviour without taking the assistant away. One app setting, no deploy.
    /// </summary>
    public bool Enabled { get; set; } = true;

    public int PolicyTopK { get; set; } = 4;

    public int TemplateTopK { get; set; } = 2;

    /// <summary>Retrieved separately and never placed in a quotable block.</summary>
    public int InternalTopK { get; set; } = 2;

    /// <summary>
    /// Past resolved exchanges shown to the model as style references. Enabled on 2026-08-03;
    /// `appsettings.json` sets 3 and App Service matches it.
    ///
    /// Zero disables the corpus outright — <see cref="KnowledgeRetriever"/> short-circuits on
    /// `topK &lt;= 0` without querying the store — which makes this the rollback lever for
    /// anything the ticket corpus causes. `/v1/config` reports `exemplars` so the state is
    /// observable rather than assumed.
    /// </summary>
    public int TicketTopK { get; set; }

    /// <summary>
    /// Below this reranker score the policy corpus is treated as not covering the question.
    ///
    /// **This is a floor, not the primary coverage control** — measured, not assumed. On clean
    /// hand-written questions the score separates cleanly: covered 2.71-2.89, uncovered
    /// 1.53-1.79. On 18 real tickets it does not. "Return Item", which policy plainly covers,
    /// scored 2.186, while "Christmas Greetings from all of us at Zryya!" — not a policy
    /// question at all — scored 2.923, the highest in the sample. Real ticket text carries
    /// signatures, quoted history and pleasantries, and the reranker measures similarity to
    /// *some* policy chunk rather than whether policy answers the question.
    ///
    /// A threshold of 2.2 therefore declined a genuine returns question and admitted a holiday
    /// greeting. Set to 1.6 — below the lowest observed real ticket (1.677) — so it fires only
    /// when retrieval found essentially nothing, and coverage is carried instead by the prompt
    /// rule telling the model to say when the policy shown does not cover the question.
    ///
    /// The cost of that choice is real: uncovered questions now reach the model, so they cost
    /// tokens. Eval class D measures whether declining actually happens; see `open-questions.md`
    /// D-6 for the decision this needs.
    /// </summary>
    public double MinimumPolicyScore { get; set; } = 1.6;

    /// <summary>
    /// **This threshold is on the semantic reranker's scale and is meaningless without it.**
    ///
    /// Measured 2026-08-02: with reranking off, Search returns reciprocal-rank-fusion scores
    /// clustered around 0.032 that do not separate covered from uncovered questions at all —
    /// "wholesale prices" scored 0.0333 against "how long do I have to return an item" at
    /// 0.0325. There is no threshold on that scale that gates anything.
    ///
    /// So `Knowledge:UseSemanticRanking` and this value move together: turning reranking off
    /// requires dropping this to zero, which disables the gate and leaves coverage to the
    /// prompt rule alone. The pipeline warns at startup if they are set inconsistently, because
    /// the failure mode otherwise is every draft silently returning insufficient_data.
    /// </summary>
    public bool SemanticRankingEnabled { get; set; } = true;
}
