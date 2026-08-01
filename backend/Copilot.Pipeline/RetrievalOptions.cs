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

    /// <summary>Zero until `R-4` indexes closed tickets; querying an empty corpus buys nothing.</summary>
    public int TicketTopK { get; set; }

    /// <summary>
    /// Below this reranker score the policy corpus is treated as not covering the question,
    /// and the pipeline declines instead of improvising. This converts "confidently wrong"
    /// into "honestly silent" and is the most important guardrail in the beta.
    ///
    /// Calibrated 2026-08-01 against the live index rather than guessed. Covered questions
    /// ("how long do I have to return an item", "what is your warranty") scored 2.71-2.89;
    /// uncovered ones ("wholesale pricing for bulk corporate orders", "file my tax return
    /// with the IRS") scored 1.53-1.79. 2.2 sits above every uncovered sample and below every
    /// covered one. Revisit once the eval suite gives real data.
    /// </summary>
    public double MinimumPolicyScore { get; set; } = 2.2;
}
